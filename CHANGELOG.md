# Changelog

All notable changes to **AvaranFlow** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-11-03

### Added
- **Initial Public Open-Source Release of AvaranFlow** (formerly ExceptionMaster).
- **Failure Containment Engine (`AvaranFlow.Core`):**
  - Exception capture with rich caller/machine context and stack trace sanitization.
  - In-memory atomic CAS aggregation and deduplication preventing storm saturation.
  - Asynchronous bounded dispatch pipeline supporting `DropOldest`, `DropNewest`, and `BlockWithTimeout`.
  - Multi-sink fan-out coordinator and deterministic fallback cascade execution.
  - Graceful queue flushing and process shutdown APIs.
- **Headless Alerting Policy Engine (`AvaranFlow.Alerting.Core`):**
  - Deterministic ascending-order rule evaluation with all-match execution.
  - Dynamic severity resolution (`Warning`, `Error`, `Critical`).
  - Rule-scoped suppression (cooldown, sliding windows, and rate thresholds).
  - Rule-scoped escalation with severity promotion and multi-step escalation tracking.
  - Destination groups with sequential and concurrent fan-out dispatch.
  - Global destination concurrency semaphore budget.
  - Generational dynamic reconfiguration (`Reconfigure`) with graceful drain.
  - Sanitized `AlertEnvelope` boundary preventing raw exceptions from leaking to destinations.
- **First-Party Sinks:**
  - `AvaranFlow.Sinks.Console`: Colorized failure-contained console output.
  - `AvaranFlow.Sinks.File`: Rolling, size-rotated, retention-managed log file sink.
  - `AvaranFlow.Sinks.Database`: High-throughput SQL Server batch upsert sink with transient retry resilience.
  - `AvaranFlow.NLog`: Optional non-intrusive adapter for NLog.
- **Performance Benchmarks (`AvaranFlow.Benchmarks`):**
  - Microsecond latency profiling via BenchmarkDotNet measuring producer logging, atomic CAS aggregation, and Alerting rule evaluation.
- **Comprehensive Test Suite:**
  - 470 automated tests passing 100% across .NET Framework 4.8 and modern .NET 8.0.
