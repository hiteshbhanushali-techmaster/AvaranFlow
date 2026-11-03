# ExceptionMaster — Requirements Traceability Matrix

| Req ID | Requirement Description | Implementing Component | Test Verification |
| :--- | :--- | :--- | :--- |
| **REQ-01** | Absolute failure isolation: internal logging faults never throw to caller | `ExceptionMaster.Log`, `DispatchPipeline` | `FailureInjection.Tests/FailureIsolationTests` |
| **REQ-02** | Zero mandatory NLog dependency in Core | `AvaranFlow.Core.csproj` | Package dependency inspection & `Core.Tests` |
| **REQ-03** | Work without external configuration files; safe defaults | `AvaranFlowOptions.CreateDefault()` | `Core.Tests/DefaultsTests` |
| **REQ-04** | Target classic .NET Framework 4.6.1+ & evaluate .NET Standard 2.0 | Multi-target build in all csproj | `dotnet build` & execution across targets |
| **REQ-05** | Complete diagnostic metadata snapshot (all 20+ fields) | `MetadataExtractor`, `ExceptionContext` | `Core.Tests/ContextMetadataTests` |
| **REQ-06** | In-memory exception deduplication & atomic aggregation | `AggregationEngine`, `AggregateState` | `Concurrency.Tests/AggregationConcurrencyTests` |
| **REQ-07** | Bounded asynchronous dispatch & buffer overflow protection | `DispatchPipeline`, `BoundedChannel/Queue` | `Core.Tests/BufferOverflowTests` |
| **REQ-08** | Multi-sink fan-out without cross-sink fault propagation | `DispatchPipeline.DispatchFanOutAsync` | `Sinks.Tests/FanOutTests` |
| **REQ-09** | Deterministic fallback chains (SQL -> File -> Console) | `FallbackChain`, `FallbackRouter` | `FailureInjection.Tests/FallbackRoutingTests` |
| **REQ-10** | Rotating file sink with size caps and archive retention | `FileSink`, `FileSinkOptions` | `Sinks.Tests/FileSinkTests` |
| **REQ-11** | Safe console sink with missing console isolation | `ConsoleSink` | `Sinks.Tests/ConsoleSinkTests` |
| **REQ-12** | Database sink with parameterized batch upsert & retries | `SqlServerDatabaseSink` | `Integration.Tests/DatabaseSinkTests` |
| **REQ-13** | Optional NLog adapter without modifying host NLog state | `AvaranFlow.NLog` | `Sinks.Tests/NLogAdapterTests` |
| **REQ-14** | Fire-and-forget notification with reentrancy protection | `EventBroadcaster`, `ReentrancyScope` | `FailureInjection.Tests/RecursionProtectionTests` |
| **REQ-15** | Thread-safe lifecycle: Flush, Shutdown, Reconfigure | `ExceptionMaster.Flush/Shutdown/Configure` | `Concurrency.Tests/LifecycleConcurrencyTests` |
| **REQ-16** | Valid NuGet packages and executable samples | `src/*.csproj` pack & `samples/*` | NuGet pack & sample execution verification |
