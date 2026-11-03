# ExceptionMaster — Configuration Guide

## 1. Zero-Configuration Mode

When no prior `Configure()` call is performed, calling `ExceptionMaster.Log(ex)` initializes **Safe Defaults**:
- In-memory aggregation enabled (5-minute window).
- Asynchronous dispatch enabled with bounded queue (1000 items).
- Safe Console Sink registered (output safely swallowed if no console attached).
- No external files or databases required.

```csharp
using ExceptionMaster.Core;

// Zero setup required
ExceptionMaster.Log(ex);
```

---

## 2. Common Scenarios

### Scenario A: File Logging with Size-Based Rotation

```csharp
using ExceptionMaster.Core;
using ExceptionMaster.Sinks.File;

ExceptionMaster.Configure(options =>
{
    options.EnableAsyncDispatch = true;
    options.FlushInterval = TimeSpan.FromSeconds(3);

    options.Sinks.Add(new FileSink(new FileSinkOptions
    {
        LogDirectory = @"C:\Logs\App",
        FileNamePattern = "exceptions-{Date}.log",
        MaxFileSizeBytes = 10 * 1024 * 1024, // 10 MB
        MaxArchiveFiles = 5,
        IncludeStackTrace = true
    }));
});
```

### Scenario B: Database Logging with File and Console Fallback

```csharp
using ExceptionMaster.Core;
using ExceptionMaster.Sinks.Database;
using ExceptionMaster.Sinks.File;
using ExceptionMaster.Sinks.Console;

ExceptionMaster.Configure(options =>
{
    var sqlSink = new SqlServerDatabaseSink(new DatabaseSinkOptions
    {
        ConnectionString = "Server=localhost;Database=Telemetry;Integrated Security=True;",
        BatchSize = 100,
        CommandTimeoutSeconds = 15
    });

    var fileSink = new FileSink(new FileSinkOptions { LogDirectory = @"C:\Logs\Fallback" });
    var consoleSink = new ConsoleSink();

    // Configure fallback chain: SQL -> File -> Console
    var chain = new FallbackChain { Primary = sqlSink };
    chain.Fallbacks.Add(fileSink);
    chain.Fallbacks.Add(consoleSink);

    options.FallbackChains.Add(chain);
});
```

### Scenario C: Multi-Sink Fan-Out

```csharp
ExceptionMaster.Configure(options =>
{
    // Fan-out: writes to File and Console independently
    options.Sinks.Add(new FileSink(new FileSinkOptions { LogDirectory = @"C:\Logs" }));
    options.Sinks.Add(new ConsoleSink());
});
```

### Scenario D: Existing NLog Integration

```csharp
using ExceptionMaster.Core;
using ExceptionMaster.Sinks.NLog;

ExceptionMaster.Configure(options =>
{
    // Relays exception events to host NLog logger named "ExceptionMaster"
    // Does NOT overwrite or alter existing NLog.config
    options.Sinks.Add(new NLogSink("ExceptionMasterLogger"));
});
```

---

## 3. Configuration Precedence & Reconfiguration Rules

1. **Precedence**: Explicit code configuration (`Configure()`) overrides safe defaults.
2. **Reconfiguration**:
   - Calling `Configure()` repeatedly flushes active buffers and swaps the pipeline snapshot atomically.
   - Sinks owned by the previous configuration are drained and disposed.
   - Host-owned loggers (like NLog `LogManager`) are never disposed.
3. **Partial Configuration**:
   - If an individual sink fails initialization (e.g., invalid path or connection string), it is marked unhealthy; all other configured sinks continue normal operation.
