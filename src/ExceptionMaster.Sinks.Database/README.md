# ExceptionMaster.Sinks.Database

Parameterized SQL Server database sink for ExceptionMaster with atomic aggregation upsert, connection retries, and fallback coordination.

## Features
- Fully parameterized commands preventing SQL injection.
- Atomic SQL MERGE upsert tracking aggregate counts across distributed application processes.
- Automatic idempotent schema creation (`ExceptionMasterLogs` table and composite indexes).
- Transient network retry recovery with exponential backoff.
- Coordinates with Dispatcher Fallback Chain to route events to File or Console if SQL remains unavailable.

## Usage
```csharp
using ExceptionMaster.Core;
using ExceptionMaster.Sinks.Database;

ExceptionMaster.Configure(options =>
{
    options.Sinks.Add(new SqlServerDatabaseSink(new DatabaseSinkOptions
    {
        ConnectionString = "Server=localhost;Database=Telemetry;Integrated Security=True;",
        TableName = "ExceptionMasterLogs",
        BatchSize = 100,
        CommandTimeoutSeconds = 15,
        AutoCreateSchema = true
    }));
});
```
