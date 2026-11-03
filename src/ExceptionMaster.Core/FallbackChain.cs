using System;
using System.Collections.Generic;

namespace ExceptionMaster.Core
{
    /// <summary>
    /// Represents a primary sink and an ordered list of fallback sinks to invoke if the primary sink fails.
    /// </summary>
    public sealed class FallbackChain
    {
        public IExceptionSink Primary { get; set; }
        public IList<IExceptionSink> Fallbacks { get; } = new List<IExceptionSink>();

        public FallbackChain()
        {
        }

        public FallbackChain(IExceptionSink primary, params IExceptionSink[] fallbacks)
        {
            Primary = primary;
            if (fallbacks != null)
            {
                foreach (var fb in fallbacks)
                {
                    if (fb != null)
                    {
                        Fallbacks.Add(fb);
                    }
                }
            }
        }
    }
}
