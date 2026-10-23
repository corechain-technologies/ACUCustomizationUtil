using System.Security.Cryptography;
using System.Text.Json;
using ACUCustomizationUtils.Builders.Log;
using Serilog;
using Serilog.Debugging;

// Only this entry point, the actual logger builder and its actual string extensions are compiled.
// No ACU project reference, host, services, commands, configuration or child processes.
if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]) || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Pass an existing absolute task-owned scratch directory.");
    return 2;
}
string root = Path.Combine(args[0], "logging-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string originalDirectory = Environment.CurrentDirectory;
string? originalFlag = Environment.GetEnvironmentVariable("ACU_CONSOLE_ONLY");
TextWriter output = Console.Out;
int failed = 0, total = 0;
try
{
    foreach (bool silent in new[] { false, true })
    foreach (string flag in new[] { "unset", "false", "true", "TRUE", "1", " true", "typo" })
    foreach (string fixture in new[] { "empty", "existing", "blocked" })
    {
        total++;
        string name = $"{(silent ? "silent" : "normal")}-{flag.Replace(' ', '_')}-{fixture}";
        string directory = Path.Combine(root, $"{total:D3}-{name}");
        Directory.CreateDirectory(directory);
        Environment.CurrentDirectory = directory;
        Environment.SetEnvironmentVariable("ACU_CONSOLE_ONLY", flag == "unset" ? null : flag);
        // Cover today's rolling file and adjacent dates across midnight. Never read real logs.
        for (int day = -1; day <= 1 && fixture != "empty"; day++)
        {
            string file = $"acu-log{DateTime.Now.AddDays(day):yyyyMMdd}.txt";
            if (fixture == "blocked") Directory.CreateDirectory(file);
            else File.WriteAllText(file, "SYNTHETIC-PRESERVED\n");
        }
        if (fixture == "existing") File.WriteAllText("acu-log.txt", "SYNTHETIC-UNROLLED\n");
        var before = Snapshot(directory);
        using var captured = new StringWriter();
        using var diagnostics = new StringWriter();
        Console.SetOut(captured);
        SelfLog.Enable(diagnostics);
        Exception? exception = null;
        var previous = Log.Logger;
        bool unchangedLogger = false;
        try
        {
            ILogger logger;
            if (silent) logger = SerilogBuilder.BuildSilent(typeof(SyntheticContext));
            else { SerilogBuilder.Build(); logger = Log.Logger; }
            logger.Debug("SYNTHETIC-DEBUG");
            logger.Information("SYNTHETIC-INFO");
            logger.Error(new InvalidOperationException("SYNTHETIC-FAULT"), "SYNTHETIC-ERROR");
        }
        catch (Exception ex) { exception = ex; }
        finally
        {
            unchangedLogger = ReferenceEquals(previous, Log.Logger);
            Log.CloseAndFlush();
            SelfLog.Disable();
            Console.SetOut(output);
        }
        var errors = new List<string>();
        bool invalid = flag is not ("unset" or "false" or "true");
        if (invalid)
        {
            if (exception is not ArgumentException) errors.Add("invalid setting not rejected");
            else if (((ArgumentException)exception).ParamName != "ACU_CONSOLE_ONLY") errors.Add("invalid setting not identified");
            if (!unchangedLogger) errors.Add("invalid setting replaced global logger");
            if (captured.ToString().Length != 0) errors.Add("invalid setting emitted console logs");
        }
        else
        {
            if (exception != null) errors.Add("unexpected " + exception.GetType().Name);
            bool info = captured.ToString().Contains("SYNTHETIC-INFO");
            if (silent ? captured.ToString().Length != 0 : !info) errors.Add("console semantics changed");
            if (!silent && !captured.ToString().Contains("SYNTHETIC-ERROR")) errors.Add("normal error output missing");
            if (!silent && captured.ToString().Contains("SYNTHETIC-DEBUG")) errors.Add("normal debug threshold changed");
        }
        if (invalid || flag == "true")
        {
            if (!before.SequenceEqual(Snapshot(directory))) errors.Add("filesystem changed");
            if (diagnostics.ToString().Length != 0) errors.Add("sink fault: file sink may have been constructed");
        }
        else if (fixture != "blocked")
        {
            string logs = string.Join("\n", Directory.GetFiles(directory, "acu-log*.txt").Select(File.ReadAllText));
            if (!logs.Contains("SYNTHETIC-INFO") || !logs.Contains("SYNTHETIC-ERROR")) errors.Add("default file positive control failed");
            if (logs.Contains("SYNTHETIC-DEBUG") != silent) errors.Add("file threshold changed");
        }
        else if (diagnostics.ToString().Length == 0) errors.Add("blocked default sink did not report a fault");
        if (errors.Count != 0) failed++;
        output.WriteLine(JsonSerializer.Serialize(new { name, passed = errors.Count == 0, errors }));
    }
}
finally
{
    Console.SetOut(output);
    SelfLog.Disable();
    Log.CloseAndFlush();
    Environment.CurrentDirectory = originalDirectory;
    Environment.SetEnvironmentVariable("ACU_CONSOLE_ONLY", originalFlag);
}
output.WriteLine(JsonSerializer.Serialize(new { total, failed, root }));
return failed == 0 ? 0 : 1;

static string[] Snapshot(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
    .Select(path => Directory.Exists(path)
        ? "D:" + Path.GetRelativePath(root, path)
        : "F:" + Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            + ":" + File.GetLastWriteTimeUtc(path).Ticks)
    .OrderBy(value => value, StringComparer.Ordinal).ToArray();

sealed class SyntheticContext { }
