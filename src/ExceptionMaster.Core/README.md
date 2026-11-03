# ExceptionMaster.Core

Resilient, failure-contained exception logging, deduplication, and aggregation framework for classic .NET Framework 4.6.1+ and .NET Standard 2.0.

## Features
- **Zero Mandatory Third-Party Dependencies**: Works out-of-the-box without NLog or external configuration files.
- **Absolute Failure Isolation**: Logging errors never propagate back to the caller.
- **In-Memory Aggregation**: Repeated exceptions within configurable windows update atomic occurrence counts and timestamps.
- **Bounded Asynchronous Buffering**: Configurable flush intervals, batch sizes, and overflow policies (`DropNewest`, `DropOldest`, `BlockWithTimeout`).
- **Fire-and-Forget Notifications**: Asynchronous event dispatch with subscriber isolation and execution-context recursion protection.

## Quick Start
```csharp
using ExceptionMaster.Core;

// 1. Zero-configuration logging
ExceptionMaster.Log(ex);

// 2. Custom category and correlation ID
ExceptionMaster.Log(ex, category: "Payment", correlationId: "order-45678");

// 3. Graceful shutdown
ExceptionMaster.Shutdown(TimeSpan.FromSeconds(5));
```
