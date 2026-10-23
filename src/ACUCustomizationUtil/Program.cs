using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Reflection;

using ACUCustomizationUtils.Builders.Commands;
using ACUCustomizationUtils.Builders.DI;
using ACUCustomizationUtils.Builders.Log;
using ACUCustomizationUtils.Common;

using Serilog;

using Spectre.Console;

[assembly: AssemblyVersion("26.08.21.1408")]

namespace ACUCustomizationUtils;

/// <summary>
/// This class is the point of building an application and the start of user input processing
/// </summary>
/// <remarks>
/// Authored by Aleksej Slusar
/// email: aleksej.slusar@sprinterra.com
/// Copyright Sprinterra(c) 2023
/// </remarks>
public static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            SerilogBuilder.Build();
        }
        catch (ArgumentException ex) when (ex.ParamName == "ACU_CONSOLE_ONLY")
        {
            // Do not route configuration rejection through a potentially file-backed logger.
            Console.Error.WriteLine("ACU_CONSOLE_ONLY must be exactly true or false, or unset.");
            return 1;
        }
        try
        {
            using Microsoft.Extensions.Hosting.IHost host = HostBuilder.Build(args);
            System.CommandLine.Command rootCommand = new RootCommandBuilder(host.Services).BuildCommand();

            Parser parser = new CommandLineBuilder(rootCommand)
                .UseDefaults()
                .UseHelp(ctx =>
                {
                    ctx.HelpBuilder.CustomizeLayout(_ =>
                        HelpBuilder
                            .Default.GetLayout()
                            .Skip(1) // Skip the default command description section.
                            .Prepend(_ =>
                            {
                                //AnsiConsole.Write(new FigletText(Messages.Acu));
                                AnsiConsole.WriteLine(rootCommand.Description!);
                                AnsiConsole.WriteLine(Messages.Copyright);
                            })
                    );
                })
                .Build();

            //return await rootCommand.InvokeAsync(args);
            return await parser.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
