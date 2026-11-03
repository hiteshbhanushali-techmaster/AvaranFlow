using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Thread-safe immutable registry of destination groups.
    /// Provides case-insensitive lookup and duplicate group ID enforcement.
    /// </summary>
    public sealed class DestinationGroupRegistry : IDestinationGroupRegistry
    {
        public static readonly DestinationGroupRegistry Empty = new DestinationGroupRegistry(new DestinationGroup[0]);

        private readonly Dictionary<string, DestinationGroup> _groups;
        private readonly ReadOnlyCollection<DestinationGroup> _groupList;

        public DestinationGroupRegistry(IEnumerable<DestinationGroup>? groups)
        {
            _groups = new Dictionary<string, DestinationGroup>(StringComparer.OrdinalIgnoreCase);

            if (groups != null)
            {
                foreach (var group in groups)
                {
                    if (group == null)
                        throw new ArgumentNullException(nameof(groups), "DestinationGroup entry cannot be null.");

                    string key = group.GroupId;
                    if (_groups.ContainsKey(key))
                    {
                        throw new ArgumentException(
                            $"Duplicate destination group ID detected: '{key}'. Group IDs must be unique (case-insensitive).",
                            nameof(groups));
                    }

                    _groups[key] = group;
                }
            }

            _groupList = new ReadOnlyCollection<DestinationGroup>(_groups.Values.ToList());
        }

        public DestinationGroup? FindGroup(string? groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId)) return null;
            return _groups.TryGetValue(groupId!.Trim(), out var group) ? group : null;
        }

        public IReadOnlyList<DestinationGroup> GetAll()
        {
            return _groupList;
        }

        public bool Contains(string? groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId)) return false;
            return _groups.ContainsKey(groupId!.Trim());
        }
    }
}
