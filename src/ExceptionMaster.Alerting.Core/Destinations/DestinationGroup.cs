using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Represents an immutable named group of destinations targeted together for independent fan-out delivery.
    /// Groups contain direct destination references only; nested groups and circular references are disallowed.
    /// </summary>
    public sealed class DestinationGroup
    {
        public string GroupId { get; }
        public string Name { get; }
        public IReadOnlyList<string> DestinationNames { get; }

        public DestinationGroup(string groupId, string? name, IEnumerable<string> destinationNames)
        {
            if (string.IsNullOrWhiteSpace(groupId))
                throw new ArgumentException("GroupId must not be null or whitespace.", nameof(groupId));

            GroupId = groupId.Trim();
            Name = string.IsNullOrWhiteSpace(name) ? GroupId : name!.Trim();

            if (destinationNames == null)
                throw new ArgumentNullException(nameof(destinationNames), "DestinationNames cannot be null.");

            var uniqueDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();

            foreach (var dest in destinationNames)
            {
                if (string.IsNullOrWhiteSpace(dest))
                    throw new ArgumentException("Group destination name cannot be null or whitespace.", nameof(destinationNames));

                string clean = dest.Trim();
                if (!uniqueDestinations.Add(clean))
                {
                    throw new ArgumentException(
                        $"Duplicate destination reference '{clean}' in destination group '{GroupId}'. Destinations in a group must be unique.",
                        nameof(destinationNames));
                }

                list.Add(clean);
            }

            if (list.Count == 0)
            {
                throw new ArgumentException($"Destination group '{GroupId}' must contain at least one destination.", nameof(destinationNames));
            }

            DestinationNames = new ReadOnlyCollection<string>(list);
        }

        public override string ToString()
        {
            return $"DestinationGroup [{GroupId}: {string.Join(", ", DestinationNames)}]";
        }
    }
}
