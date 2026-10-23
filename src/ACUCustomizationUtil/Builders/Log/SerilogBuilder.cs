using ACUCustomizationUtils.Extensions;

using Serilog;
using Serilog.Events;

namespace ACUCustomizationUtils.Builders.Log;

/// <summary>
/// This class is the point of building an application Logger
/// </summary>
/// <remarks>
/// Authored by Aleksej Slusar
/// email: aleksej.slusar@sprinterra.com
/// Copyright Sprinterra(c) 2023
/// </remarks>
public static class SerilogBuilder
{
    // Process-only opt-in; reject typos before constructing any sink.
    private static bool ConsoleOnly() => Environment.GetEnvironmentVariable("ACU_CONSOLE_ONLY") switch
    {
        null => false,
        "false" => false,
        "true" => true,
        _ => throw new ArgumentException("ACU_CONSOLE_ONLY must be exactly true or false, or unset.", "ACU_CONSOLE_ONLY")
    };

    public static void Build()
    {
        bool consoleOnly = ConsoleOnly();
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console();
        if (!consoleOnly)
            configuration.WriteTo.File("acu-log.txt", rollingInterval: RollingInterval.Day, shared: true);
        Serilog.Log.Logger = configuration.CreateLogger();
    }

    public static ILogger BuildSilent(Type contextType)
    {
        bool consoleOnly = ConsoleOnly();
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext();
        // ProcessHelper already prints child output. Keep this path silent to avoid duplicates.
        if (!consoleOnly)
            configuration.WriteTo.File("acu-log.txt", rollingInterval: RollingInterval.Day, shared: true);
        ILogger logger = Serilog.Log.Logger = configuration.CreateLogger();

        return logger.ForContext(contextType);
    }

    public static string Format(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        bool error = message.IsErrorInfo();
        string level = error ? "ERR" : "INF";

        return $"[{timestamp} {level}] {message}";
    }
}
