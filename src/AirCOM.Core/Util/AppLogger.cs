using Microsoft.Extensions.Logging;

namespace AirCOM.Core.Util;

/// <summary>
/// Thin wrapper over <c>Microsoft.Extensions.Logging</c> providing a static ambient logger
/// for early-boot code that lacks DI. Production code should prefer constructor-injected loggers.
/// </summary>
public static class AppLogger
{
    private static ILoggerFactory? _factory;
    private static ILogger? _logger;

    public static void Initialize(ILoggerFactory? factory)
    {
        _factory = factory;
        _logger = factory?.CreateLogger("AirCOM");
    }

    public static ILogger Logger => _logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
