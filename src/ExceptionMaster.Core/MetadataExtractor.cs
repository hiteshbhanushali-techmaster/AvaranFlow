using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ExceptionMaster.Core
{
    /// <summary>
    /// Thread-safe helper that safely captures host and runtime diagnostic metadata with minimal performance overhead.
    /// </summary>
    public static class MetadataExtractor
    {
        private static readonly string CachedMachineName;
        private static readonly string CachedProcessName;
        private static readonly int CachedProcessId;
        private static readonly string CachedAppVersion;
        private static readonly string CachedEnvironmentName;

        static MetadataExtractor()
        {
            try
            {
                CachedMachineName = Environment.MachineName;
            }
            catch
            {
                CachedMachineName = "UNKNOWN_MACHINE";
            }

            try
            {
                using (var currentProcess = Process.GetCurrentProcess())
                {
                    CachedProcessName = currentProcess.ProcessName;
                    CachedProcessId = currentProcess.Id;
                }
            }
            catch
            {
                CachedProcessName = "UNKNOWN_PROCESS";
                CachedProcessId = 0;
            }

            try
            {
                var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                CachedAppVersion = entryAssembly?.GetName()?.Version?.ToString() ?? "1.0.0.0";
            }
            catch
            {
                CachedAppVersion = "1.0.0.0";
            }

            try
            {
                var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") 
                          ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                          ?? "Production";
                CachedEnvironmentName = env;
            }
            catch
            {
                CachedEnvironmentName = "Production";
            }
        }

        public static string MachineName => CachedMachineName;
        public static string ProcessName => CachedProcessName;
        public static int ProcessId => CachedProcessId;
        public static string AppVersion => CachedAppVersion;
        public static string EnvironmentName => CachedEnvironmentName;

        public static int CurrentThreadId => Thread.CurrentThread.ManagedThreadId;

        /// <summary>
        /// Recursively extracts and formats inner exception hierarchy, including AggregateException trees.
        /// </summary>
        public static string FormatInnerExceptions(Exception ex)
        {
            if (ex == null) return string.Empty;

            var sb = new StringBuilder();
            if (ex is AggregateException aggEx)
            {
                var flattened = aggEx.Flatten();
                int idx = 1;
                foreach (var inner in flattened.InnerExceptions)
                {
                    sb.AppendLine($"[Inner {idx++}] {inner.GetType().FullName}: {inner.Message}");
                    if (!string.IsNullOrEmpty(inner.StackTrace))
                    {
                        sb.AppendLine(inner.StackTrace);
                    }
                }
            }
            else if (ex.InnerException != null)
            {
                int level = 1;
                var current = ex.InnerException;
                while (current != null && level <= 10) // Bound traversal to 10 levels to prevent cyclic structures
                {
                    sb.AppendLine($"[Inner Level {level++}] {current.GetType().FullName}: {current.Message}");
                    if (!string.IsNullOrEmpty(current.StackTrace))
                    {
                        sb.AppendLine(current.StackTrace);
                    }
                    current = current.InnerException;
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Safely extracts the top stack frame or method signature for aggregation key calculation.
        /// </summary>
        public static string ExtractTopStackFrame(Exception ex)
        {
            if (ex == null) return "NoException";

            try
            {
                if (ex.TargetSite != null)
                {
                    return $"{ex.TargetSite.DeclaringType?.FullName}.{ex.TargetSite.Name}";
                }
            }
            catch
            {
                // TargetSite may throw in partial-trust or dynamic method scenarios
            }

            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                var lines = ex.StackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    return lines[0].Trim();
                }
            }

            return ex.Source ?? "UnknownMethod";
        }
    }
}
