using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Core;

namespace ExceptionMaster.Sinks.Database
{
    public sealed class DatabaseSinkOptions
    {
        public string ConnectionString { get; set; }
        public string TableName { get; set; } = "ExceptionMasterLogs";
        public string SchemaName { get; set; } = "dbo";
        public int BatchSize { get; set; } = 100;
        public int CommandTimeoutSeconds { get; set; } = 30;
        public int MaxRetries { get; set; } = 3;
        public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);
        public bool AutoCreateSchema { get; set; } = true;
    }

    /// <summary>
    /// Parameterized SQL Server database sink with atomic merge aggregation, connection retry, and failure isolation.
    /// </summary>
    public sealed class SqlServerDatabaseSink : IExceptionSink
    {
        private readonly DatabaseSinkOptions _options;
        private readonly string _fullTableName;
        private bool _schemaVerified;
        private readonly object _schemaLock = new object();

        public string Name => "SqlServerDatabaseSink";

        public SqlServerDatabaseSink(DatabaseSinkOptions options)
        {
            _options = options ?? new DatabaseSinkOptions();
            _fullTableName = $"[{_options.SchemaName}].[{_options.TableName}]";
        }

        public async Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
        {
            if (context == null || string.IsNullOrWhiteSpace(_options.ConnectionString)) return;
            await WriteBatchAsync(new[] { context }, cancellationToken).ConfigureAwait(false);
        }

        public async Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken)
        {
            if (contexts == null || contexts.Count == 0 || string.IsNullOrWhiteSpace(_options.ConnectionString))
            {
                return;
            }

            EnsureSchema();

            int attempt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using (var connection = new SqlConnection(_options.ConnectionString))
                    {
                        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                        using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                        {
                            foreach (var context in contexts)
                            {
                                using (var command = CreateMergeCommand(connection, transaction, context))
                                {
                                    command.CommandTimeout = _options.CommandTimeoutSeconds;
                                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                                }
                            }

                            transaction.Commit();
                        }
                    }
                    return; // Batch written successfully!
                }
                catch (Exception ex)
                {
                    attempt++;
                    if (attempt >= _options.MaxRetries || cancellationToken.IsCancellationRequested)
                    {
                        // Rethrow to let Dispatcher Fallback Chain engage fallbacks (File -> Console)
                        throw new InvalidOperationException($"SqlServerDatabaseSink failed after {attempt} attempts: {ex.Message}", ex);
                    }

                    int delayMs = (int)(_options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private SqlCommand CreateMergeCommand(SqlConnection connection, SqlTransaction transaction, ExceptionContext context)
        {
            string sql = $@"
MERGE {_fullTableName} WITH (HOLDLOCK) AS target
USING (VALUES (
    @ExceptionKey, @OccurrenceId, @ExceptionType, @Message, @StackTrace,
    @Caller, @Category, @MachineName, @ProcessName, @ProcessId, @ThreadId,
    @AppVersion, @CorrelationId, @IsFatal, @EnvironmentName, @Source,
    @InnerExceptionDetails, @FirstSeenUtc, @LastSeenUtc, @WindowStartUtc, @BatchCount, @SchemaVersion
)) AS source (
    ExceptionKey, OccurrenceId, ExceptionType, Message, StackTrace,
    Caller, Category, MachineName, ProcessName, ProcessId, ThreadId,
    AppVersion, CorrelationId, IsFatal, EnvironmentName, Source,
    InnerExceptionDetails, FirstSeenUtc, LastSeenUtc, WindowStartUtc, BatchCount, SchemaVersion
)
ON target.ExceptionKey = source.ExceptionKey 
   AND target.WindowStartUtc = source.WindowStartUtc
WHEN MATCHED THEN
    UPDATE SET 
        target.OccurrenceCount = target.OccurrenceCount + source.BatchCount,
        target.LastSeenUtc = CASE WHEN source.LastSeenUtc > target.LastSeenUtc THEN source.LastSeenUtc ELSE target.LastSeenUtc END,
        target.UpdatedAtUtc = SYSUTCDATETIME(),
        target.IsFatal = target.IsFatal | source.IsFatal
WHEN NOT MATCHED THEN
    INSERT (
        ExceptionKey, OccurrenceId, ExceptionType, Message, StackTrace,
        Caller, Category, MachineName, ProcessName, ProcessId, ThreadId,
        AppVersion, CorrelationId, IsFatal, EnvironmentName, Source,
        InnerExceptionDetails, FirstSeenUtc, LastSeenUtc, WindowStartUtc, OccurrenceCount, SchemaVersion
    )
    VALUES (
        source.ExceptionKey, source.OccurrenceId, source.ExceptionType, source.Message, source.StackTrace,
        source.Caller, source.Category, source.MachineName, source.ProcessName, source.ProcessId, source.ThreadId,
        source.AppVersion, source.CorrelationId, source.IsFatal, source.EnvironmentName, source.Source,
        source.InnerExceptionDetails, source.FirstSeenUtc, source.LastSeenUtc, source.WindowStartUtc, source.BatchCount, source.SchemaVersion
    );";

            var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@ExceptionKey", SqlDbType.NVarChar, 450).Value = context.ExceptionKey ?? string.Empty;
            command.Parameters.Add("@OccurrenceId", SqlDbType.UniqueIdentifier).Value = context.OccurrenceId;
            command.Parameters.Add("@ExceptionType", SqlDbType.NVarChar, 250).Value = context.ExceptionType ?? string.Empty;
            command.Parameters.Add("@Message", SqlDbType.NVarChar, -1).Value = context.Message ?? string.Empty;
            command.Parameters.Add("@StackTrace", SqlDbType.NVarChar, -1).Value = (object)context.StackTrace ?? DBNull.Value;
            command.Parameters.Add("@Caller", SqlDbType.NVarChar, 200).Value = (object)context.Caller ?? DBNull.Value;
            command.Parameters.Add("@Category", SqlDbType.NVarChar, 100).Value = (object)context.Category ?? DBNull.Value;
            command.Parameters.Add("@MachineName", SqlDbType.NVarChar, 128).Value = context.MachineName ?? string.Empty;
            command.Parameters.Add("@ProcessName", SqlDbType.NVarChar, 128).Value = context.ProcessName ?? string.Empty;
            command.Parameters.Add("@ProcessId", SqlDbType.Int).Value = context.ProcessId;
            command.Parameters.Add("@ThreadId", SqlDbType.Int).Value = context.ThreadId;
            command.Parameters.Add("@AppVersion", SqlDbType.NVarChar, 64).Value = (object)context.AppVersion ?? DBNull.Value;
            command.Parameters.Add("@CorrelationId", SqlDbType.NVarChar, 128).Value = (object)context.CorrelationId ?? DBNull.Value;
            command.Parameters.Add("@IsFatal", SqlDbType.Bit).Value = context.IsFatal;
            command.Parameters.Add("@EnvironmentName", SqlDbType.NVarChar, 64).Value = (object)context.EnvironmentName ?? DBNull.Value;
            command.Parameters.Add("@Source", SqlDbType.NVarChar, 256).Value = (object)context.Source ?? DBNull.Value;
            command.Parameters.Add("@InnerExceptionDetails", SqlDbType.NVarChar, -1).Value = (object)context.InnerExceptionDetails ?? DBNull.Value;
            command.Parameters.Add("@FirstSeenUtc", SqlDbType.DateTime2).Value = context.FirstSeenUtc;
            command.Parameters.Add("@LastSeenUtc", SqlDbType.DateTime2).Value = context.LastSeenUtc;
            command.Parameters.Add("@WindowStartUtc", SqlDbType.DateTime2).Value = context.WindowStartUtc;
            command.Parameters.Add("@BatchCount", SqlDbType.BigInt).Value = context.DeltaCount > 0 ? context.DeltaCount : context.Count;
            command.Parameters.Add("@SchemaVersion", SqlDbType.Int).Value = context.SchemaVersion;

            return command;
        }

        private void EnsureSchema()
        {
            if (!_options.AutoCreateSchema || _schemaVerified || string.IsNullOrWhiteSpace(_options.ConnectionString))
            {
                return;
            }

            lock (_schemaLock)
            {
                if (_schemaVerified) return;

                try
                {
                    using (var connection = new SqlConnection(_options.ConnectionString))
                    {
                        connection.Open();
                        string createSql = $@"
IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = '{_options.SchemaName}' AND t.name = '{_options.TableName}')
BEGIN
    CREATE TABLE {_fullTableName}
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
        [ExceptionKey] NVARCHAR(450) NOT NULL,
        [OccurrenceId] UNIQUEIDENTIFIER NOT NULL,
        [ExceptionType] NVARCHAR(250) NOT NULL,
        [Message] NVARCHAR(MAX) NOT NULL,
        [StackTrace] NVARCHAR(MAX) NULL,
        [Caller] NVARCHAR(200) NULL,
        [Category] NVARCHAR(100) NULL,
        [MachineName] NVARCHAR(128) NOT NULL,
        [ProcessName] NVARCHAR(128) NOT NULL,
        [ProcessId] INT NOT NULL,
        [ThreadId] INT NOT NULL,
        [AppVersion] NVARCHAR(64) NULL,
        [CorrelationId] NVARCHAR(128) NULL,
        [IsFatal] BIT NOT NULL DEFAULT (0),
        [EnvironmentName] NVARCHAR(64) NULL,
        [Source] NVARCHAR(256) NULL,
        [InnerExceptionDetails] NVARCHAR(MAX) NULL,
        [FirstSeenUtc] DATETIME2(7) NOT NULL,
        [LastSeenUtc] DATETIME2(7) NOT NULL,
        [WindowStartUtc] DATETIME2(7) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [OccurrenceCount] BIGINT NOT NULL DEFAULT (1),
        [SchemaVersion] INT NOT NULL DEFAULT (1),
        [CreatedAtUtc] DATETIME2(7) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [UpdatedAtUtc] DATETIME2(7) NOT NULL DEFAULT (SYSUTCDATETIME())
    );

    CREATE UNIQUE NONCLUSTERED INDEX [UX_{_options.TableName}_Key_Window]
    ON {_fullTableName} ([ExceptionKey], [WindowStartUtc]);

    CREATE NONCLUSTERED INDEX [IX_{_options.TableName}_Key_LastSeen]
    ON {_fullTableName} ([ExceptionKey], [LastSeenUtc] DESC)
    INCLUDE ([OccurrenceCount], [FirstSeenUtc], [IsFatal]);
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('{_fullTableName}') AND name = 'WindowStartUtc')
    BEGIN
        ALTER TABLE {_fullTableName} ADD [WindowStartUtc] DATETIME2(7) NOT NULL CONSTRAINT [DF_{_options.TableName}_WindowStartUtc] DEFAULT (SYSUTCDATETIME());
    END

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('{_fullTableName}') AND name = 'UX_{_options.TableName}_Key_Window')
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX [UX_{_options.TableName}_Key_Window]
        ON {_fullTableName} ([ExceptionKey], [WindowStartUtc]);
    END
END";
                        using (var cmd = new SqlCommand(createSql, connection))
                        {
                            cmd.CommandTimeout = _options.CommandTimeoutSeconds;
                            cmd.ExecuteNonQuery();
                        }
                    }

                    _schemaVerified = true;
                }
                catch
                {
                    // If schema verification fails due to permissions or offline DB, do not crash; retry on next write
                }
            }
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
        }
    }
}
