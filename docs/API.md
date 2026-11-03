# ExceptionMaster — Public API Reference

## 1. Primary Entry Point: `ExceptionMaster`

Located in namespace `ExceptionMaster.Core`.

### Static Methods

```csharp
namespace ExceptionMaster.Core
{
    public static class ExceptionMaster
    {
        // Primary logging APIs
        public static void Log(
            Exception exception,
            [CallerMemberName] string caller = null,
            string category = null,
            string correlationId = null,
            bool isFatal = false);

        public static void Log(
            Exception exception,
            ExceptionLogOptions customOptions,
            [CallerMemberName] string caller = null);

        // Configuration and lifecycle
        public static void Configure(Action<ExceptionMasterOptions> configure);
        public static void ConfigureDefaults();
        public static void Flush(TimeSpan? timeout = null);
        public static void Shutdown(TimeSpan? timeout = null);

        // Health counters and telemetry
        public static ExceptionMasterHealth GetHealth();

        // Fire-and-forget notification event
        public static event Action<ExceptionContext> ExceptionRaised;
    }
}
```

### Behavioral Contracts
1. **`Log(...)`**:
   - Thread-safe, non-blocking (when buffering/async is enabled).
   - Never throws exceptions back to caller.
   - If `exception` is null, safely ignored.
   - Snapshots all diagnostic metadata at capture time.
2. **`Configure(Action<ExceptionMasterOptions>)`**:
   - Thread-safe configuration update.
   - Flushes existing pipeline before swapping active configuration snapshot.
   - Preserves running logger operation during reconfiguration.
3. **`Flush(TimeSpan? timeout)`**:
   - Drains all buffered events to sinks.
   - Times out after specified interval (default: 5 seconds), never hangs indefinitely.
4. **`Shutdown(TimeSpan? timeout)`**:
   - Flushes buffers, cancels background timers/workers, disposes managed sinks.
   - Safe against repeated invocations.

---

## 2. Configuration Options: `ExceptionMasterOptions`

```csharp
namespace ExceptionMaster.Core
{
    public sealed class ExceptionMasterOptions
    {
        // Core behavioral switches
        public bool EnableAggregation { get; set; } = true;
        public TimeSpan AggregationWindow { get; set; } = TimeSpan.FromMinutes(5);
        public int MaxAggregationEntries { get; set; } = 10000;

        // Buffering & asynchronous dispatch
        public bool EnableAsyncDispatch { get; set; } = true;
        public int BufferCapacity { get; set; } = 1000;
        public int BatchSize { get; set; } = 50;
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);
        public QueueOverflowPolicy OverflowPolicy { get; set; } = QueueOverflowPolicy.DropNewest;
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

        // Emergency fallback console write
        public bool EnableEmergencyDiagnostics { get; set; } = true;

        // Sinks registration
        public IList<IExceptionSink> Sinks { get; } = new List<IExceptionSink>();
        public IList<FallbackChain> FallbackChains { get; } = new List<FallbackChain>();

        // Custom key generator
        public Func<Exception, string> CustomKeyGenerator { get; set; }
    }

    public enum QueueOverflowPolicy
    {
        DropNewest,
        DropOldest,
        BlockWithTimeout
    }
}
```

---

## 3. Sink Interfaces: `IExceptionSink` & `FallbackChain`

```csharp
namespace ExceptionMaster.Core
{
    public interface IExceptionSink : IDisposable
    {
        string Name { get; }
        Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken);
        Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken);
        void Flush();
    }

    public sealed class FallbackChain
    {
        public IExceptionSink Primary { get; set; }
        public IList<IExceptionSink> Fallbacks { get; } = new List<IExceptionSink>();
    }
}
```

---

## 4. Diagnostics & Health: `ExceptionMasterHealth`

```csharp
namespace ExceptionMaster.Core
{
    public sealed class ExceptionMasterHealth
    {
        public long TotalLogged { get; }
        public long TotalAggregated { get; }
        public long TotalDropped { get; }
        public long TotalDispatched { get; }
        public long TotalSinkFailures { get; }
        public long TotalFallbackInvocations { get; }
        public int PendingQueueDepth { get; }
        public bool IsShutdown { get; }
    }
}
```
