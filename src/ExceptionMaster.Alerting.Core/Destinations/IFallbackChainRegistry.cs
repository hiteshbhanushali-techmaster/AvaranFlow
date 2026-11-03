using System.Collections.Generic;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Provides read-only lookup and enumeration of configured fallback chains.
    /// </summary>
    public interface IFallbackChainRegistry
    {
        /// <summary>
        /// Finds a fallback chain by its case-insensitive identifier.
        /// </summary>
        FallbackChain? FindChain(string chainId);

        /// <summary>
        /// Gets all configured fallback chains.
        /// </summary>
        IReadOnlyList<FallbackChain> GetAll();

        /// <summary>
        /// Determines whether a fallback chain with the specified identifier is registered.
        /// </summary>
        bool Contains(string chainId);
    }
}
