using System.Collections.Generic;

namespace AvaranFlow.Alerting.Destinations
{
    /// <summary>
    /// Provides read-only lookup and enumeration of configured destination groups.
    /// </summary>
    public interface IDestinationGroupRegistry
    {
        /// <summary>
        /// Finds a destination group by its case-insensitive identifier.
        /// </summary>
        DestinationGroup? FindGroup(string groupId);

        /// <summary>
        /// Gets all configured destination groups.
        /// </summary>
        IReadOnlyList<DestinationGroup> GetAll();

        /// <summary>
        /// Determines whether a group with the specified identifier is registered.
        /// </summary>
        bool Contains(string groupId);
    }
}
