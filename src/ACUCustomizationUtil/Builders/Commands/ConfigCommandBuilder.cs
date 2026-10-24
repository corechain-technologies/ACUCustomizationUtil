using System.CommandLine;
using System.CommandLine.Binding;
using System.Text.Json;

using ACUCustomizationUtils.Builders.Commands.Common;
using ACUCustomizationUtils.Configuration.ACU;
using ACUCustomizationUtils.Helpers;

namespace ACUCustomizationUtils.Builders.Commands;

/// <summary>
/// `acu config show`: print the effective configuration as JSON, with every
/// <c>[Secret]</c> property masked, and exit. Resolution goes through the same
/// <see cref="CommandParametersBinder"/> path as every other command, so file
/// defaults, overlay copying and derived values are exactly what a real command
/// would see. No ERP, database, filesystem write or network access occurs.
/// </summary>
public class ConfigCommandBuilder : CommandBuilderBase
{
    public override Command BuildCommand()
    {
        Command showCommand = new Command("show", "Print the effective configuration as JSON with secrets masked.");

        showCommand.SetHandler(
            config =>
            {
                string json = JsonSerializer.Serialize(
                    ConfigurationHelper.ToMaskedDocument(config),
                    new JsonSerializerOptions { WriteIndented = true }
                );
                Console.Out.WriteLine(json);
                return Task.CompletedTask;
            },
            new ConfigShowBinder(ConfigOption!, UserConfigOption!)
        );

        return new Command("config", "Inspect the resolved configuration.") { showCommand };
    }
}

/// <summary>
/// Binder with no command-specific options. The empty <see cref="AcuConfiguration"/>
/// still runs <c>OnDeserialized</c> (like every binder), so the result reflects the
/// defaults a real command input would contribute.
/// </summary>
public class ConfigShowBinder(
    Option<FileInfo> configFile,
    Option<FileInfo> userConfigFile
) : CommandParametersBinder(configFile, userConfigFile)
{
    protected override IAcuConfiguration GetUserConfiguration(
        BindingContext bindingContext,
        Option<string>?[] commandOptions
    )
    {
        return new AcuConfiguration();
    }
}
