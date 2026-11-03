using System.Collections.Generic;

namespace AvaranFlow.Alerting.Destinations
{
    /// <summary>
    /// Provides read-only resolution and enumeration of registered alert destinations.
    /// </summary>
    public interface IDestinationRegistry
    {
        /// <summary>
        /// Attempts to find a registered destination by its case-insensitive name.
        /// </summary>
        IAlertDestination? FindDestination(string name);

        /// <summary>
        /// Gets all currently registered destinations.
        /// </summary>
        IReadOnlyList<IAlertDestination> GetAll();

        /// <summary>
        /// Determines whether a destination with the specified name is registered.
        /// </summary>
        bool Contains(string name);
    }
}
