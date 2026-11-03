using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Thread-safe immutable registry of alert destinations.
    /// Provides case-insensitive lookup, duplicate detection, and lifecycle disposal isolation.
    /// </summary>
    public sealed class DestinationRegistry : IDestinationRegistry, IDisposable
    {
        public static readonly DestinationRegistry Empty = new DestinationRegistry(new IAlertDestination[0]);

        private readonly Dictionary<string, IAlertDestination> _destinations;
        private readonly ReadOnlyCollection<IAlertDestination> _destinationList;
        private int _isDisposed;

        public DestinationRegistry(IEnumerable<IAlertDestination>? destinations)
        {
            _destinations = new Dictionary<string, IAlertDestination>(StringComparer.OrdinalIgnoreCase);

            if (destinations != null)
            {
                foreach (var destination in destinations)
                {
                    if (destination == null)
                        throw new ArgumentNullException(nameof(destinations), "Destination entry cannot be null.");

                    if (string.IsNullOrWhiteSpace(destination.Name))
                        throw new ArgumentException("Destination name cannot be null or whitespace.", nameof(destinations));

                    string key = destination.Name.Trim();
                    if (_destinations.ContainsKey(key))
                    {
                        throw new ArgumentException(
                            $"Duplicate destination name detected: '{key}'. Destination names must be unique (case-insensitive).",
                            nameof(destinations));
                    }

                    _destinations[key] = destination;
                }
            }

            _destinationList = new ReadOnlyCollection<IAlertDestination>(_destinations.Values.ToList());
        }

        public IAlertDestination? FindDestination(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _destinations.TryGetValue(name!.Trim(), out var dest) ? dest : null;
        }

        public IReadOnlyList<IAlertDestination> GetAll()
        {
            return _destinationList;
        }

        public bool Contains(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return _destinations.ContainsKey(name!.Trim());
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

            foreach (var dest in _destinationList)
            {
                try
                {
                    dest.Dispose();
                }
                catch
                {
                    // Failure containment: an exception during destination disposal must never bubble up
                }
            }
        }
    }
}
