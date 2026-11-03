using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AvaranFlow.Alerting.Destinations
{
    /// <summary>
    /// Thread-safe immutable registry of fallback chains.
    /// Provides case-insensitive lookup and duplicate chain ID enforcement.
    /// </summary>
    public sealed class FallbackChainRegistry : IFallbackChainRegistry
    {
        public static readonly FallbackChainRegistry Empty = new FallbackChainRegistry(new FallbackChain[0]);

        private readonly Dictionary<string, FallbackChain> _chains;
        private readonly ReadOnlyCollection<FallbackChain> _chainList;

        public FallbackChainRegistry(IEnumerable<FallbackChain>? chains)
        {
            _chains = new Dictionary<string, FallbackChain>(StringComparer.OrdinalIgnoreCase);

            if (chains != null)
            {
                foreach (var chain in chains)
                {
                    if (chain == null)
                        throw new ArgumentNullException(nameof(chains), "FallbackChain entry cannot be null.");

                    string key = chain.ChainId;
                    if (_chains.ContainsKey(key))
                    {
                        throw new ArgumentException(
                            $"Duplicate fallback chain ID detected: '{key}'. Chain IDs must be unique (case-insensitive).",
                            nameof(chains));
                    }

                    _chains[key] = chain;
                }
            }

            _chainList = new ReadOnlyCollection<FallbackChain>(_chains.Values.ToList());
        }

        public FallbackChain? FindChain(string? chainId)
        {
            if (string.IsNullOrWhiteSpace(chainId)) return null;
            return _chains.TryGetValue(chainId!.Trim(), out var chain) ? chain : null;
        }

        public IReadOnlyList<FallbackChain> GetAll()
        {
            return _chainList;
        }

        public bool Contains(string? chainId)
        {
            if (string.IsNullOrWhiteSpace(chainId)) return false;
            return _chains.ContainsKey(chainId!.Trim());
        }
    }
}
