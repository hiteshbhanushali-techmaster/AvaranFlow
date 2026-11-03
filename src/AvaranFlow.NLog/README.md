# AvaranFlow.NLog

Optional NLog adapter for ExceptionMaster.

## Features
- Connects ExceptionMaster telemetry directly to your existing host NLog configuration.
- **Non-Invasive**: Does not install default `NLog.config`, overwrite targets, or dispose host `LogManager`.
- Preserves full diagnostic metadata, exception keys, and aggregate counts in `LogEventInfo.Properties`.

## Usage
```csharp
using AvaranFlow.Core;
using AvaranFlow.NLog;

ExceptionMaster.Configure(options =>
{
    // Forwards events to the host NLog logger named "ExceptionMaster"
    options.Sinks.Add(new NLogSink("ExceptionMaster"));
});
```
