# ExceptionMaster — Canonical Architecture Document

## 1. System Overview and Goals

**ExceptionMaster** is a resilient, failure-contained exception logging and aggregation framework designed primarily for classic .NET Framework 4.6.1+ applications and .NET Standard 2.0 compliant runtimes.

The fundamental design imperative of ExceptionMaster is **absolute failure containment**: under no circumstance will any failure in the logging pipeline—including database outages, disk saturation, missing directories, serialization faults, or malfunctioning event subscribers—propagate back as an exception to the calling application.

### Key Capabilities
1. **Core Zero-Dependency Runtime**: `AvaranFlow.Core` contains zero external dependencies (no NLog, no third-party libraries).
2. **Deterministic Fallback Routing**: Fallback pipelines (e.g., Database → File → Console) ensure critical diagnostic telemetry is preserved when primary sinks fail.
3. **Multi-Sink Fan-Out**: Dispatch exceptions to multiple independent destinations concurrently without cross-sink failure coupling.
4. **Thread-Safe Deduplication & Periodic Aggregation**: In-memory aggregation consolidates repeated exceptions within configurable time windows, tracking `FirstSeenUtc`, `LastSeenUtc`, deterministic `WindowStartUtc`, and atomic `Count`. Periodic flushes emit aggregate summaries to sinks without lost counts.
5. **Bounded Asynchronous Dispatch & Buffering**: Non-blocking asynchronous event batching with configurable flush intervals, queue capacities, and bounded overflow policies (`DropNewest`, `DropOldest`, `BlockWithTimeout`) with deterministic semaphore accounting.
6. **Bounded Async Notifications with Isolated Reentrancy**: Bounded background notification queue for `ExceptionRaised` with per-context recursion protection, subscriber isolation, and subscriber preservation across reconfigurations.
7. **Safe Lifecycle Management**: Explicit thread-safe `Configure()`, `Flush()`, and `Shutdown()` semantics with deterministic timeout guarantees and separation of library vs. host resources.

---

## 2. Component Architecture and Boundaries

```
+------------------------------------------------------------------------+
|                          Application Code                              |
+------------------------------------------------------------------------+
                                     |
                                     v
+------------------------------------------------------------------------+
|                          ExceptionMaster API                           |
|       Log(ex), Configure(opts), Flush(timeout), Shutdown(timeout)      |
+------------------------------------------------------------------------+
                                     |
                                     v
+------------------------------------------------------------------------+
|                         Metadata Extractor                             |
|  Captures immutable snapshot: Machine, Process, Thread, Version, etc.  |
+------------------------------------------------------------------------+
                                     |
                                     v
+------------------------------------------------------------------------+
|                        Aggregation Engine                              |
|   Key Normalization | Atomic CAS Counters | Periodic Summaries         |
+------------------------------------------------------------------------+
                 |                                      |
                 v                                      v
+----------------------------------+  +----------------------------------+
|     Bounded Event Broadcaster    |  |     Async Dispatch Pipeline      |
|  (Bounded Queue & Worker Task)   |  |   (Bounded Buffer / Batching)    |
|  - Preserved across Configure()  |  |   - DropNewest / DropOldest      |
|  - Subscriber Isolation          |  |   - Periodic Timer & Summaries   |
|  - AsyncLocal Reentrancy Guard   |  |   - Fallback Router              |
+----------------------------------+  +----------------------------------+
                                                        |
                                                        v
                                      +----------------------------------+
                                      |    Fan-Out & Fallback Router     |
                                      +----------------------------------+
                                                        |
            +--------------------+----------------------+--------------------+
            |                    |                      |                    |
            v                    v                      v                    v
  +------------------+  +------------------+  +-------------------+  +---------------+
  |   Console Sink   |  |    File Sink     |  |   Database Sink   |  | Custom Sinks  |
  | (Safe Formatting)|  | (Size Rotation)  |  |  (Batch Upsert)   |  | (IException-  |
  +------------------+  +------------------+  +-------------------+  |     Sink)     |
                                                        |            +---------------+
                                                        v
                                              +-------------------+
                                              |   NLog Adapter    |
                                              | (Isolated Package)|
                                              +-------------------+
```

### Project Decomposition

| Project | Responsibility | Dependencies | Target Frameworks |
| :--- | :--- | :--- | :--- |
| **`AvaranFlow.Core`** | Public API, metadata capture, aggregation engine, dispatch pipeline, buffering, sink contracts, lifecycle management. | None | `net461;netstandard2.0` |
| **`AvaranFlow.Sinks.Console`** | Console rendering with optional color formatting and safe output stream disconnection handling. | `AvaranFlow.Core` | `net461;netstandard2.0` |
| **`AvaranFlow.Sinks.File`** | Rolling file persistence with size-based rotation, retention limits, and file share isolation. | `AvaranFlow.Core` | `net461;netstandard2.0` |
| **`AvaranFlow.Sinks.Database`** | Parameterized SQL Server persistence with batch upsert, deterministic windowing, and timeout bounds. | `AvaranFlow.Core` | `net461;netstandard2.0` |
| **`AvaranFlow.NLog`** | Optional adapter relaying `ExceptionContext` to host NLog targets without altering host configuration. | `AvaranFlow.Core`, `NLog (>= 4.7.15)` | `net461;netstandard2.0` |

---

## 3. Core Domain Model: `ExceptionContext`

The `ExceptionContext` is an immutable snapshot representing an individual occurrence or aggregated sequence of exceptions.

```csharp
public sealed class ExceptionContext
{
    public Exception Exception { get; }
    public string Message { get; }
    public string ExceptionType { get; }
    public string StackTrace { get; }
    public DateTime FirstSeenUtc { get; }
    public DateTime LastSeenUtc { get; }
    public DateTime WindowStartUtc { get; }
    public long Count { get; }
    public long DeltaCount { get; }
    public string Caller { get; }
    public string Category { get; }
    public string MachineName { get; }
    public string ProcessName { get; }
    public int ProcessId { get; }
    public int ThreadId { get; }
    public string AppVersion { get; }
    public string CorrelationId { get; }
    public bool IsFatal { get; }
    public string ExceptionKey { get; }
    public string EnvironmentName { get; }
    public string Source { get; }
    public string InnerExceptionDetails { get; }
    public Guid OccurrenceId { get; }
    public int SchemaVersion { get; }
}
```

---

## 4. Aggregation & Deduplication Mechanics

1. **Aggregation Key**:
   Normalized composite: `ExceptionType + "|" + NormalizeMessage(Message) + "|" + ExtractTopMethodSignature(StackTrace)`. Message normalization truncates to 200 characters, replaces GUIDs with `#GUID`, and replaces hex identifiers with `#HEX`.
2. **Synchronization Strategy**:
   Atomic compare-and-swap state replacement per key (`AggregateState` containing immutable snapshot of counts and timestamps) backed by a concurrent dictionary with off-hot-path eviction.
3. **Count & Window Semantics**:
   - `FirstSeenUtc`: Immutable timestamp of initial occurrence.
   - `LastSeenUtc`: Monotonically updated timestamp of most recent occurrence.
   - `WindowStartUtc`: Deterministic window boundary aligned to `AggregationWindow`.
   - `Count`: Exact cumulative count of observed occurrences in active aggregation window.
   - `DeltaCount`: Incremental count observed since the previous periodic summary flush.
4. **Periodic Summaries**:
   Repeated occurrences update in-memory counters atomically. The `DispatchPipeline` background loop regularly calls `AggregationEngine.GetPeriodicSummaryContexts()` to enqueue updated snapshots to sinks.
5. **Eviction & Shutdown Drain**:
   Expired entries are evicted during background maintenance cycles, emitting final summary snapshots before removal. Upon `ExceptionMaster.Shutdown()`, all remaining pending aggregate deltas are drained to sinks.

---

## 5. Buffering, Asynchronous Dispatch, and Overflow

1. **Producer Contract**:
   When asynchronous logging is active, `Log()` snapshots metadata, checks aggregation, and pushes a context reference into a bounded queue. The producer thread completes in microseconds.
2. **Overflow Policies & Precise Accounting**:
   - `DropNewest`: Discards incoming item and atomically increments the dropped event counter.
   - `DropOldest`: Evicts the oldest item from the queue and releases its capacity permit, ensuring capacity accounting remains strictly in sync with queue state.
   - `BlockWithTimeout`: Bounded wait up to a strict timeout, failing safe if the queue remains saturated.
   - *Fatal Event Guarantee*: Fatal exceptions (`IsFatal == true`) bypass capacity permit acquisition while carrying an explicit tracking flag (`QueueItem.HoldsCapacityPermit = false`), guaranteeing fatal events are queued even when buffers are full without corrupting permit accounting.
3. **Signal Coherence**:
   Worker signaling uses an edge-triggered `SemaphoreSlim(0, 1)` rather than counting permits, eliminating ghost signals and semaphore inflation.

---

## 6. Sinks and Fallback Architecture

### `IExceptionSink` Contract
```csharp
public interface IExceptionSink : IDisposable
{
    string Name { get; }
    Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken);
    Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken);
    void Flush();
}
```

### Fan-Out and Fallback Rules
- **Fan-Out**: Each primary registered sink operates independently. A fault in Sink A (e.g. Database) does not halt dispatch to Sink B (e.g. File).
- **Fallback Chain**: Primary sinks throw errors to `DispatchPipeline`. The dispatcher catches the error, activates the fallback chain, and attempts writing to contingency destinations (e.g. Database $\to$ File $\to$ Console).
- **Duplicate Delivery Prevention**: If a sink appears in both fan-out and fallback lists, `DispatchPipeline` tracks successful writes per batch and avoids duplicate writes.
- **Absolute Failure Containment**: Failures in sinks and fallbacks are contained within `DispatchPipeline` and never escape to the host application.

---

## 7. Bounded Event Notifications & Reentrancy Isolation

1. **Bounded EventBroadcaster**:
   `EventBroadcaster` uses a bounded `ConcurrentQueue<ExceptionContext>` and dedicated worker task to deliver notifications to `ExceptionMaster.ExceptionRaised` subscribers without spawning unbounded `Task.Run` instances.
2. **Subscription Preservation**:
   Subscriber delegates registered via `ExceptionMaster.ExceptionRaised += ...` are stored in a static multi-cast delegate protected by synchronization locks. Re-calling `ExceptionMaster.Configure(...)` rebinds existing subscribers without dropping listeners or leaking resources.
3. **Subscriber Isolation**:
   Subscriber invocations are protected with individual `try-catch` blocks. An exception thrown by one subscriber is logged via emergency diagnostics and does not prevent subsequent subscribers from receiving the notification.
4. **Reentrancy Protection**:
   `AsyncLocal<bool>` flags whether the current execution flow is within a subscriber callback. If a subscriber invokes `ExceptionMaster.Log(ex)`, the exception is logged to sinks but suppressed from re-raising `ExceptionRaised`, preventing infinite recursive loops.

---

## 8. Lifecycle & Durability Guarantees

1. `Configure(Action<AvaranFlowOptions> configure)` applies atomic configuration transitions while preserving subscriber registrations.
2. `Flush(TimeSpan timeout)` drains queue items, waits for active in-flight writes to finish, and flushes pending aggregation summaries.
3. `Shutdown(TimeSpan timeout)` drains all pending queue items and aggregation summaries, stops worker tasks, and disposes library-managed sinks.
4. **Durability Limits**: In-memory queues and aggregation deltas reside in volatile process RAM. Durability across abrupt process termination (`Process.Kill()`, power failure) is not claimed; persistent storage occurs upon successful sink write.
