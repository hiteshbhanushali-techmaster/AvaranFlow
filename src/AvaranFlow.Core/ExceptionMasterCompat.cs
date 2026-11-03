using System;
using System.Runtime.CompilerServices;

namespace AvaranFlow.Core
{
    /// <summary>
    /// Backward-compatibility facade for existing code referencing ExceptionMaster.
    /// Redirects all calls to <see cref="AvaranFlow"/>.
    /// </summary>
    [Obsolete("Use AvaranFlow instead. ExceptionMaster is retained for backward compatibility.")]
    public static class ExceptionMaster
    {
        public static event Action<ExceptionContext> ExceptionRaised
        {
            add => AvaranFlow.ExceptionRaised += value;
            remove => AvaranFlow.ExceptionRaised -= value;
        }

        public static void Configure(Action<AvaranFlowOptions> configure) => AvaranFlow.Configure(configure);
        public static void ConfigureDefaults() => AvaranFlow.ConfigureDefaults();
        public static void Log(
            Exception exception,
            [CallerMemberName] string caller = null,
            string category = null,
            string correlationId = null,
            bool isFatal = false)
            => AvaranFlow.Log(exception, caller, category, correlationId, isFatal);

        public static void Log(
            Exception exception,
            ExceptionLogOptions customOptions,
            [CallerMemberName] string caller = null)
            => AvaranFlow.Log(exception, customOptions, caller);

        public static void Flush(TimeSpan? timeout = null) => AvaranFlow.Flush(timeout);
        public static void Shutdown(TimeSpan? timeout = null) => AvaranFlow.Shutdown(timeout);
        public static AvaranFlowHealth GetHealth() => AvaranFlow.GetHealth();
    }
}

namespace ExceptionMaster.Core
{
    /// <summary>
    /// Backward-compatibility namespace facade for legacy code using `using ExceptionMaster.Core;`.
    /// </summary>
    [Obsolete("Use AvaranFlow.Core.AvaranFlow instead.")]
    public static class ExceptionMaster
    {
        public static event Action<AvaranFlow.Core.ExceptionContext> ExceptionRaised
        {
            add => AvaranFlow.Core.AvaranFlow.ExceptionRaised += value;
            remove => AvaranFlow.Core.AvaranFlow.ExceptionRaised -= value;
        }

        public static void Configure(Action<AvaranFlow.Core.AvaranFlowOptions> configure) => AvaranFlow.Core.AvaranFlow.Configure(configure);
        public static void ConfigureDefaults() => AvaranFlow.Core.AvaranFlow.ConfigureDefaults();
        public static void Log(
            Exception exception,
            [CallerMemberName] string caller = null,
            string category = null,
            string correlationId = null,
            bool isFatal = false)
            => AvaranFlow.Core.AvaranFlow.Log(exception, caller, category, correlationId, isFatal);

        public static void Log(
            Exception exception,
            AvaranFlow.Core.ExceptionLogOptions customOptions,
            [CallerMemberName] string caller = null)
            => AvaranFlow.Core.AvaranFlow.Log(exception, customOptions, caller);

        public static void Flush(TimeSpan? timeout = null) => AvaranFlow.Core.AvaranFlow.Flush(timeout);
        public static void Shutdown(TimeSpan? timeout = null) => AvaranFlow.Core.AvaranFlow.Shutdown(timeout);
        public static AvaranFlow.Core.AvaranFlowHealth GetHealth() => AvaranFlow.Core.AvaranFlow.GetHealth();
    }
}
