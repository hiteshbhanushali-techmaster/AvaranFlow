# ExceptionMaster.Sinks.File

Size-rotated, failure-contained rolling file sink for ExceptionMaster with bounded archive retention.

## Features
- Size-based rotation (e.g. 10MB per file) and retention limits (e.g. 10 archive files).
- Transient file lock retry recovery.
- Safe directory creation and path handling.
- Absolute failure isolation: disk-full, locked files, and permission errors never propagate to the caller.

## Usage
```csharp
using ExceptionMaster.Core;
using ExceptionMaster.Sinks.File;

ExceptionMaster.Configure(options =>
{
    options.Sinks.Add(new FileSink(new FileSinkOptions
    {
        LogDirectory = @"C:\Logs\MyApp",
        FileNamePattern = "app-errors-{Date}.log",
        MaxFileSizeBytes = 10 * 1024 * 1024, // 10 MB
        MaxArchiveFiles = 5,
        IncludeStackTrace = true
    }));
});
```
