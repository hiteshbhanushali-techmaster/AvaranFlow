# ExceptionMaster.Sinks.Console

Thread-safe console sink for ExceptionMaster.

## Features
- Resilient against disconnected consoles or headless GUI processes.
- Optional ANSI color formatting for fatal, error, and diagnostic messages.
- Formats message, exception type, thread ID, category, and inner exceptions cleanly.

## Usage
```csharp
using ExceptionMaster.Core;
using ExceptionMaster.Sinks.Console;

ExceptionMaster.Configure(options =>
{
    options.Sinks.Add(new ConsoleSink(new ConsoleSinkOptions
    {
        UseColors = true,
        IncludeStackTrace = true
    }));
});
```
