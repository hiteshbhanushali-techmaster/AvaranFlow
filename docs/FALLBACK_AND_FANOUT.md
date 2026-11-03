# ExceptionMaster — Fallback and Fan-Out Routing Specification

## 1. Architectural Distinction

- **Fan-Out**: Distributes an exception event to multiple independent sinks in parallel or sequential isolated steps. A failure in one sink does NOT prevent delivery to any other sink.
- **Fallback**: Defines an ordered contingency chain: Primary $\to$ Fallback 1 $\to$ Fallback 2. Destination $N+1$ is invoked if and only if Destination $N$ fails.

---

## 2. Dispatcher Semantics & Failure Containment

```text
[ExceptionMaster.Log(ex)]
           | (Never throws to host application)
           v
   [DispatchPipeline]
           |
           +---> [Fan-Out Sink 1: Console] ----------------------------> Success
           |
           +---> [Fallback Chain]
                     |
                     v
                 [Primary: SQL Sink] ---------> (Throws SqlException / Timeout)
                     |                                       |
                     | (Dispatcher Catches Failure)           |
                     v                                       v
                 [Fallback 1: File Sink] -------------> Success
                     |
                     v (If File Sink also fails)
                 [Fallback 2: Console Sink] ----------> Emergency Diagnostic Log
```

### Sink Failure Propagation Contract
1. **Sinks Must NOT Swallow Unhandled Errors**:
   Sinks (such as [`SqlServerDatabaseSink`](file:///d:/Hitesh/Code%20backups/ExceptionMaster/src/AvaranFlow.Sinks.Database/SqlServerDatabaseSink.cs), [`FileSink`](file:///d:/Hitesh/Code%20backups/ExceptionMaster/src/AvaranFlow.Sinks.File/FileSink.cs), and [`NLogSink`](file:///d:/Hitesh/Code%20backups/ExceptionMaster/src/AvaranFlow.NLog/NLogSink.cs)) do not catch and discard write exceptions internally. They throw the underlying I/O or network exception back to `DispatchPipeline`.
2. **Dispatcher Isolates and Triggers Fallback**:
   `DispatchPipeline.DispatchToSinksAsync` catches exceptions thrown by primary sinks. It records sink failure metrics (`SinkWriteFailures`), activates the configured `FallbackChain`, and proceeds down the cascade.
3. **Application Host Isolation**:
   No exception thrown by a sink or fallback ever escapes `DispatchPipeline` to the host application thread. If all sinks and fallbacks fail, an emergency fallback log is optionally output to `Console.Error` (if `EnableEmergencyDiagnostics` is enabled), and internal failure counters are incremented.

---

## 3. Duplicate Delivery Prevention

In mixed topologies where a sink is registered both as an independent Fan-Out target and as a fallback target for another sink (e.g., File Sink is both a fan-out destination and the fallback destination for SQL Server):

- `DispatchPipeline` tracks `successfullyWrittenSinks` for the active dispatch context.
- If File Sink already successfully processed the batch during Fan-Out, fallback routing to File Sink detects that File Sink already wrote the event and avoids redundant duplicate writes.
- Events routed through fallback pipelines carry their original `OccurrenceId` and active chain context.
