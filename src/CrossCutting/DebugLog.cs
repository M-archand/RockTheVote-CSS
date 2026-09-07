using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace cs2_rockthevote
{
    internal static class DebugLog
    {
        public static ILogger For(ILogger logger, Config config)
            => config.General.DebugLogging ? logger : NullLogger.Instance;
    }
}
