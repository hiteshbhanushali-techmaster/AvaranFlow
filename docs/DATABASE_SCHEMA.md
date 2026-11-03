# ExceptionMaster — Database Schema & SQL Specification

## 1. Supported Database Engines

- **Supported Engines**: Microsoft SQL Server 2012, 2014, 2016, 2017, 2019, 2022, Azure SQL Database, and SQL Server LocalDB.
- **Provider**: `System.Data.SqlClient` (in-box in .NET Framework 4.6.1+; referenced via NuGet on .NET Standard 2.0).
- **Security**: All SQL operations are strictly parameterized. Connection credentials are never included in exception logs or health telemetry.

---

## 2. Table Definition (`ExceptionMasterLogs`)

```sql
CREATE TABLE [dbo].[ExceptionMasterLogs]
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
    [IsFatal] BIT NOT NULL CONSTRAINT [DF_ExceptionMasterLogs_IsFatal] DEFAULT (0),
    [EnvironmentName] NVARCHAR(64) NULL,
    [Source] NVARCHAR(256) NULL,
    [InnerExceptionDetails] NVARCHAR(MAX) NULL,
    [FirstSeenUtc] DATETIME2(7) NOT NULL,
    [LastSeenUtc] DATETIME2(7) NOT NULL,
    [WindowStartUtc] DATETIME2(7) NOT NULL CONSTRAINT [DF_ExceptionMasterLogs_WindowStartUtc] DEFAULT (SYSUTCDATETIME()),
    [OccurrenceCount] BIGINT NOT NULL CONSTRAINT [DF_ExceptionMasterLogs_OccurrenceCount] DEFAULT (1),
    [SchemaVersion] INT NOT NULL CONSTRAINT [DF_ExceptionMasterLogs_SchemaVersion] DEFAULT (1),
    [CreatedAtUtc] DATETIME2(7) NOT NULL CONSTRAINT [DF_ExceptionMasterLogs_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedAtUtc] DATETIME2(7) NOT NULL CONSTRAINT [DF_ExceptionMasterLogs_UpdatedAtUtc] DEFAULT (SYSUTCDATETIME())
);
GO

-- Unique Index for deterministic aggregation windowing without RangeS-U deadlocks
CREATE UNIQUE NONCLUSTERED INDEX [UX_ExceptionMasterLogs_Key_Window]
ON [dbo].[ExceptionMasterLogs] ([ExceptionKey], [WindowStartUtc])
INCLUDE ([OccurrenceCount], [FirstSeenUtc], [LastSeenUtc], [IsFatal]);
GO

-- Secondary index for fast temporal filtering and queries
CREATE NONCLUSTERED INDEX [IX_ExceptionMasterLogs_LastSeen]
ON [dbo].[ExceptionMasterLogs] ([LastSeenUtc] DESC);
GO

-- Index for queries filtering by CorrelationId
CREATE NONCLUSTERED INDEX [IX_ExceptionMasterLogs_CorrelationId]
ON [dbo].[ExceptionMasterLogs] ([CorrelationId])
WHERE [CorrelationId] IS NOT NULL;
GO
```

---

## 3. Atomic Upsert Command (Concurrency-Safe)

To aggregate occurrences across multiple threads or multiple process instances without race conditions, lost increments, or deadlocks, `SqlServerDatabaseSink` computes a deterministic aggregation window boundary:

$$\text{WindowStartUtc} = \text{floor}\left(\frac{\text{FirstSeenUtc}}{\text{AggregationWindow}}\right) \times \text{AggregationWindow}$$

This allows exact point-matching in `MERGE ... WITH (HOLDLOCK)` backed by the unique constraint `UX_ExceptionMasterLogs_Key_Window`:

```sql
MERGE [dbo].[ExceptionMasterLogs] WITH (HOLDLOCK) AS target
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
    );
```

### Why Deterministic `WindowStartUtc` Is Superior to Sliding `DATEADD`
1. **Deadlock Elimination**: Sliding `DATEADD(minute, -5, LastSeenUtc)` queries execute range scans with RangeS-U locks across shifting keys, causing frequent lock conversion deadlocks under concurrent writers. Deterministic `WindowStartUtc` enables point lookups on single index keys.
2. **Duplicate Key Prevention**: Backed by `UNIQUE INDEX UX_ExceptionMasterLogs_Key_Window`, concurrent transactions cannot insert duplicate rows for the same exception in the same window.
3. **Multi-Process Delta Aggregation**: Sinks emit `DeltaCount` (the batch delta observed since the last flush). The SQL `OccurrenceCount + source.BatchCount` accurately accumulates increments across multiple distributed instances without double-counting.
