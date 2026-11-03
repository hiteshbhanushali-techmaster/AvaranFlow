using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AvaranFlow.Alerting.Models;

namespace AvaranFlow.Alerting.Destinations
{
    /// <summary>
    /// Represents the immutable aggregate outcome of a fan-out dispatch to one or more independent destinations.
    /// Captures individual destination outcomes, overall success, and total elapsed duration.
    /// </summary>
    public sealed class FanOutDeliveryResult
    {
        private static readonly IReadOnlyList<AlertDeliveryResult> EmptyResults =
            new ReadOnlyCollection<AlertDeliveryResult>(new AlertDeliveryResult[0]);

        public bool AllSucceeded { get; }
        public bool AnySucceeded { get; }
        public IReadOnlyList<AlertDeliveryResult> Results { get; }
        public TimeSpan TotalElapsed { get; }

        public FanOutDeliveryResult(
            IEnumerable<AlertDeliveryResult>? results,
            TimeSpan totalElapsed)
        {
            TotalElapsed = totalElapsed >= TimeSpan.Zero ? totalElapsed : TimeSpan.Zero;

            if (results != null)
            {
                var list = results.Where(r => r != null).ToList();
                Results = new ReadOnlyCollection<AlertDeliveryResult>(list);
                AllSucceeded = list.Count > 0 && list.All(r => r.Success);
                AnySucceeded = list.Any(r => r.Success);
            }
            else
            {
                Results = EmptyResults;
                AllSucceeded = false;
                AnySucceeded = false;
            }
        }

        public override string ToString()
        {
            int successCount = Results.Count(r => r.Success);
            return $"FanOutDeliveryResult: {successCount}/{Results.Count} succeeded in {TotalElapsed.TotalMilliseconds:F1}ms";
        }
    }
}
