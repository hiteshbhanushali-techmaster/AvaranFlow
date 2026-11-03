using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvaranFlow.Core
{
    /// <summary>
    /// Contract implemented by all ExceptionMaster logging destinations.
    /// </summary>
    public interface IExceptionSink : IDisposable
    {
        /// <summary>
        /// Gets the unique identifier/name of this sink.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Writes an individual exception context asynchronously.
        /// </summary>
        Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Writes a batch of exception contexts asynchronously.
        /// </summary>
        Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken);

        /// <summary>
        /// Synchronously flushes any buffered data inside the sink.
        /// </summary>
        void Flush();
    }
}
