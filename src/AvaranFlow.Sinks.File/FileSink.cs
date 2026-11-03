using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Core;

namespace AvaranFlow.Sinks.File
{
    public sealed class FileSinkOptions
    {
        public string LogDirectory { get; set; }
        public string FileNamePattern { get; set; } = "exceptions-{Date}.log";
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
        public int MaxArchiveFiles { get; set; } = 10;
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        public bool IncludeStackTrace { get; set; } = true;
        public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss.fff";

        public FileSinkOptions()
        {
            try
            {
                LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            }
            catch
            {
                LogDirectory = "logs";
            }
        }
    }

    /// <summary>
    /// Resilient rolling file sink with size-based rotation and bounded archive retention.
    /// </summary>
    public sealed class FileSink : IExceptionSink
    {
        private readonly object _fileLock = new object();
        private readonly FileSinkOptions _options;

        public string Name => "FileSink";

        public FileSink(FileSinkOptions options = null)
        {
            _options = options ?? new FileSinkOptions();
            EnsureDirectoryExists();
        }

        public Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
        {
            if (context == null || cancellationToken.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            WriteToFile(new[] { context });
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken)
        {
            if (contexts == null || contexts.Count == 0 || cancellationToken.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            WriteToFile(contexts);
            return Task.CompletedTask;
        }

        private void WriteToFile(IReadOnlyList<ExceptionContext> contexts)
        {
            lock (_fileLock)
            {
                EnsureDirectoryExists();
                string currentFilePath = GetCurrentFilePath();

                var sb = new StringBuilder();
                foreach (var context in contexts)
                {
                    FormatContext(sb, context);
                }

                byte[] bytes = _options.Encoding.GetBytes(sb.ToString());

                // Check for size-based rotation
                if (System.IO.File.Exists(currentFilePath))
                {
                    var fileInfo = new FileInfo(currentFilePath);
                    if (fileInfo.Length + bytes.Length >= _options.MaxFileSizeBytes)
                    {
                        RotateFile(currentFilePath);
                    }
                }

                // Write with retry for transient file lock
                WriteBytesWithRetry(currentFilePath, bytes);
            }
        }

        private void WriteBytesWithRetry(string filePath, byte[] bytes)
        {
            const int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Flush();
                    }
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    Thread.Sleep(50);
                }
            }
        }

        private void RotateFile(string currentFilePath)
        {
            try
            {
                string directory = Path.GetDirectoryName(currentFilePath) ?? _options.LogDirectory;
                string baseName = Path.GetFileNameWithoutExtension(currentFilePath);
                string extension = Path.GetExtension(currentFilePath);

                // Find next available archive sequence index
                int archiveIndex = 1;
                string archivePath;
                do
                {
                    archivePath = Path.Combine(directory, $"{baseName}.{archiveIndex}{extension}");
                    archiveIndex++;
                } while (System.IO.File.Exists(archivePath));

                System.IO.File.Move(currentFilePath, archivePath);

                // Purge archives exceeding MaxArchiveFiles
                if (_options.MaxArchiveFiles > 0)
                {
                    var archives = Directory.GetFiles(directory, $"{baseName}.*{extension}")
                        .Select(f => new FileInfo(f))
                        .OrderBy(f => f.CreationTimeUtc)
                        .ToList();

                    while (archives.Count > _options.MaxArchiveFiles)
                    {
                        try
                        {
                            archives[0].Delete();
                            archives.RemoveAt(0);
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Safe swallow
            }
        }

        private string GetCurrentFilePath()
        {
            string dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
            string fileName = _options.FileNamePattern.Replace("{Date}", dateStr);
            return Path.Combine(_options.LogDirectory, fileName);
        }

        private void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_options.LogDirectory))
                {
                    Directory.CreateDirectory(_options.LogDirectory);
                }
            }
            catch
            {
                // Safe swallow
            }
        }

        private void FormatContext(StringBuilder sb, ExceptionContext context)
        {
            sb.AppendFormat("[{0}] [{1}] [P:{2}:{3}] [T:{4}] [Key:{5}] [Count:{6}] {7}: {8}",
                context.LastSeenUtc.ToString(_options.TimestampFormat),
                context.Category,
                context.ProcessName,
                context.ProcessId,
                context.ThreadId,
                context.ExceptionKey,
                context.Count,
                context.ExceptionType,
                context.Message);
            sb.AppendLine();

            if (!string.IsNullOrEmpty(context.CorrelationId))
            {
                sb.AppendLine($"  CorrelationId: {context.CorrelationId}");
            }

            if (_options.IncludeStackTrace && !string.IsNullOrEmpty(context.StackTrace))
            {
                sb.AppendLine("  StackTrace:");
                sb.AppendLine(context.StackTrace);
            }

            if (!string.IsNullOrEmpty(context.InnerExceptionDetails))
            {
                sb.AppendLine("  InnerExceptions:");
                sb.AppendLine(context.InnerExceptionDetails);
            }

            sb.AppendLine(new string('-', 80));
        }

        public void Flush()
        {
            // Buffered streams are flushed after write
        }

        public void Dispose()
        {
            Flush();
        }
    }
}
