# AvaranFlow.Alerting.Core

Headless, high-throughput, failure-isolated policy and routing engine for ExceptionMaster.

`AvaranFlow.Alerting.Core` sits between exception capture in `ExceptionMaster` and external alert destinations. It enforces deterministic rule matching, dynamic severity resolution, rule-scoped suppression, threshold escalation, destination grouping, fan-out dispatch, and fallback cascades.

## Architecture

```
ExceptionMaster
      ↓ (ExceptionContext)
AvaranFlow.Alerting.Core
      ├── Ingestion & Bounded Pipeline (DropNewest / DropOldest / BlockWithTimeout)
      ├── Severity Resolution (Warning, Error, Critical)
      ├── Rule Evaluation (Deterministic Ordering, Multi-Rule Matching)
      ├── Rule-Scoped Suppression (Cooldown, Windowing, Threshold)
      ├── Rule-Scoped Escalation (Level Elevation, Step Promotion)
      ├── Destination Registry & Groups (Sequential / Concurrent Fan-Out)
      └── Fallback Chains (Cascading Recovery)
            ↓ (AlertEnvelope)
      Destinations
         ├── Notifier (Desktop UI via AvaranFlow.Alerting.Notifier)
         ├── Custom / Third-Party Destinations
         └── Fallback Destinations
```

`AvaranFlow.Alerting.Core` is strictly headless, cross-platform (`netstandard2.0` and `net8.0`), and contains zero UI or desktop dependencies.

## V1 Capabilities

- **Deterministic Rule Evaluation**: Rules execute in defined order (`Order` ascending). All matching rules execute without hidden short-circuiting.
- **Dynamic Severity Resolution**: Resolves effective alert severity (`Warning`, `Error`, `Critical`) prior to rule evaluation.
- **Rule-Scoped Suppression**: Rate-limiting and cooldown windows tracked per rule and dimension key (`ExceptionKey`, `ExceptionType`, `Source`, `CustomProperty`). Suppression suppresses delivery without preventing escalation tracking.
- **Rule-Scoped Escalation**: Escalates alert severity based on occurrence counts within evaluation windows, resetting automatically on window expiry.
- **Destination Routing & Groups**: Route alerts to single destinations or logical destination groups.
- **Fan-Out Dispatch**: Dispatches alerts to multiple destinations sequentially or concurrently.
- **Fallback Chains**: Automatically cascades to secondary and tertiary destinations if a primary destination delivery fails.
- **Bounded Pipeline**: Asynchronous in-memory buffer with configurable capacity, worker concurrency, and overflow policies (`DropNewest`, `DropOldest`, `BlockWithTimeout`).
- **Lifecycle Management**: Clean generational reconfigurations (`Reconfigure`), non-destructive flush (`FlushAsync`), and graceful shutdown (`ShutdownAsync`).
- **Concurrency Controls**: Global semaphore-backed destination concurrency budgeting (`GlobalMaxDestinationConcurrency`) shared across all rules and generations.
- **Safe AlertEnvelope Boundary**: Prevents raw `Exception` instances and unredacted objects from leaking to external destinations.
- **Failure Isolation**: Sinks, destinations, and rule evaluation errors never escape or crash host application threads.

## Installation

```bash
dotnet add package AvaranFlow.Alerting.Core --version 1.0.0
```

## Basic Usage

```csharp
using System;
using System.Threading.Tasks;
using AvaranFlow.Alerting;
using AvaranFlow.Alerting.Configuration;
using AvaranFlow.Alerting.Destinations;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Models;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Alerting.Suppression;

// 1. Initialize Alerting with rules and destinations
Alerting.Initialize(builder =>
{
    builder.WithBoundedQueue(capacity: 5000, overflowPolicy: QueueOverflowPolicy.DropNewest)
           .WithConcurrency(workerConcurrency: 2, globalMaxDestinationConcurrency: 16);

    // Register a custom headless destination
    builder.RegisterDestination(new CustomConsoleDestination("Console"));

    // Register an optional suppression policy
    builder.RegisterSuppressionPolicy(new SuppressionPolicy(
        policyId: "cooldown-5m",
        dimension: SuppressionDimension.ExceptionKey,
        duration: TimeSpan.FromMinutes(5)));

    // Define alerting rules
    builder.AddRule(new AlertRule(
        ruleId: "critical-rule",
        name: "Critical Exception Rule",
        condition: RuleCondition.Severity(PredicateOperator.GreaterThanOrEqual, AlertSeverity.Critical),
        order: 10,
        targetDestinations: new[] { "Console" },
        suppressionPolicyId: "cooldown-5m"));
});

// 2. Alerting automatically processes exceptions captured via ExceptionMaster.
// Manual ingestion is also supported:
// Alerting.Ingest(exceptionContext);

// 3. Flush in-flight alerts before routine checkpoints
await Alerting.FlushAsync(TimeSpan.FromSeconds(5));

// 4. Gracefully shutdown on application exit
await Alerting.ShutdownAsync(TimeSpan.FromSeconds(10));
```

## Security Model

- **AlertEnvelope**: The boundary object delivered to all destinations. It contains only sanitized string keys, severity levels, titles, summaries, timestamps, occurrence counts, and opaque action tokens.
- **Raw Exceptions Never Cross**: Raw `System.Exception` instances and unmanaged runtime resources never cross the destination boundary.
- **Opaque AlertActionToken**: Diagnostic and remediation tokens carry opaque identifiers; destination adapters cannot mutate or execute unsanctioned code through tokens.

## Delivery Semantics

> **IMPORTANT**: Successful destination delivery means **successful admission into the destination engine**.
> 
> For asynchronous destinations, successful delivery confirms that the target destination accepted the `AlertEnvelope` into its ingress pipeline. It does not imply that an end user has acknowledged or viewed the notification.

## Verified Limitations

- **In-Memory State**: Rule suppression counters and escalation trackers are maintained in-memory and reset upon application restart.
- **Generational Draining**: During `Reconfigure`, older pipeline generations are drained within a bounded timeout (`ReconfigureDrainTimeout`); alerts exceeding this timeout are cancelled to prevent thread leaks.
- **Destination Isolation**: Destinations must handle their own internal transport timeouts; destination timeouts configured on Alerting will cancel the destination delivery token if the timeout expires.

## License

MIT License. Copyright (c) 2026 ExceptionMaster Contributors.
