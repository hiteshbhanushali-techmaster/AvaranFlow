# Contributing to AvaranFlow

Thank you for your interest in contributing to **AvaranFlow**!

AvaranFlow is a failure-contained, high-performance .NET exception processing, deduplication, aggregation, and alerting infrastructure.

---

## Code of Conduct

We are committed to providing a welcoming, inclusive, and harassment-free experience for everyone. Please be respectful and constructive in all communications and reviews.

---

## Core Tenets & Architecture Principles

1. **Failure Containment:**
   - Exception handling, logging, sinks, and alerting must NEVER throw unhandled exceptions into the host application or crash worker threads.
2. **Low-Latency Producer Path:**
   - The producer path (`AvaranFlow.Log` or `ExceptionMaster.Log`) must remain non-blocking and minimal, delegating heavy I/O to background pipelines.
3. **Bounded Memory Guarantees:**
   - All queues and caches must enforce bounded capacity limits (`DropOldest`, `DropNewest`, `BlockWithTimeout`) to prevent memory exhaustion during exception storms.
4. **Deterministic Alerting:**
   - Rules evaluate strictly in ascending order. All matching rules execute without hidden short-circuiting.
5. **No Vendor Lock-In in Core:**
   - `AvaranFlow.Core` and `AvaranFlow.Alerting.Core` have zero database or vendor dependencies. Sinks and integrations remain optional satellite packages.
6. **Pass All Tests:**
   - Pull requests must pass 100% of the test suite across all target frameworks (`net461`, `net48`, `netstandard2.0`, `net8.0`).

---

## Development & Testing Workflow

### Prerequisites
- Windows 10/11 or Windows Server 2019+
- .NET 8.0 SDK
- Visual Studio 2022 (version 17.8+) or Visual Studio Code with C# Dev Kit
- .NET Framework 4.8 / 4.6.1 Developer Pack

### Building the Solution
```powershell
# Restore dependencies
dotnet restore AvaranFlow.sln

# Build Release configuration
dotnet build AvaranFlow.sln -c Release
```

### Running the Test Suites
```powershell
dotnet test AvaranFlow.sln -c Release
```

### Running the Benchmark Suite
```powershell
dotnet run --project benchmarks/AvaranFlow.Benchmarks/AvaranFlow.Benchmarks.csproj -c Release
```

---

## Submitting Pull Requests

1. Fork the repository on GitHub.
2. Create a feature branch: `git checkout -b feature/my-enhancement`.
3. Verify that all 470+ tests pass cleanly in Release mode.
4. Push to your fork and submit a Pull Request against `main`.
