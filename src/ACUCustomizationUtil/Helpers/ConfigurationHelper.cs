using System.Reflection;
using System.Text.Json;

using ACUCustomizationUtils.Configuration;
using ACUCustomizationUtils.Configuration.ACU;
using ACUCustomizationUtils.Extensions;

using Microsoft.Extensions.Logging;

namespace ACUCustomizationUtils.Helpers;

public static class ConfigurationHelper
{
    // Placeholder rendered in the console/log in place of any config property marked [Secret].
    private const string MaskText = "***";

    public static void WriteConfig(IAcuConfiguration config)
    {
        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        string configJson = JsonSerializer.Serialize(config, options);
        File.WriteAllText("acu.json", configJson);
    }

    public static IAcuConfiguration GetConfiguration(
        FileSystemInfo? configFile,
        FileSystemInfo? userConfigFile,
        IAcuConfiguration? userInput = null
    )
    {
        //Read configurations
        IAcuConfiguration config = ReadConfig(configFile?.FullName);
        IAcuConfiguration userConfig = ReadConfig(userConfigFile?.FullName);

        //Merge configurations
        IAcuConfiguration result;
        if (config.IsNotNull)
        {
            config.CopyValues(userConfig).CopyValues(userInput);
            result = config;
        }
        else if (userConfig.IsNotNull)
        {
            userConfig.CopyValues(userInput);
            result = userConfig;
        }
        else if (userInput != null)
        {
            result = userInput;
        }
        else
        {
            throw new Exception($"ACU configuration is null or empty");
        }

        //Apply defaults once, after the merge. Applied per source, a default (e.g. iisAppPool =
        //DefaultAppPool) became an explicit value that overrode the same field from earlier sources.
        result.OnDeserialized();

        //Derive the DB connection string after the merge so that discrete DB fields
        //(dbProvider, sqlServerName, dbName, dbUser, dbPassword) from any source are respected
        result.Site.DeriveDbConnectionString();

        return result;
    }

    public static void PrintConfiguration<T>(
        IAcuConfiguration config,
        ILogger<T> logger,
        params string[]? types
    )
    {
        List<(string?, string, object)> res = new List<(string?, string, object)>();
        ReadConfigurationValues(config, nameof(IAcuConfiguration), res);
        if (!res.Any())
            return;

        logger.LogInformation("Current configuration parameters:");
        foreach ((string type, string name, object value) in res.Where(r => types?.Contains(r.Item1) ?? true))
            logger.LogInformation("[{Type}] {Key} : {Value}", type, name, value);
    }

    /// <summary>
    /// Project the configuration into a JSON-ready document keyed by section and
    /// camelCase property name, with every <c>[Secret]</c> property replaced by the
    /// mask. Shares the reflection walk with <see cref="PrintConfiguration{T}"/>, so
    /// the two can never disagree about what is secret.
    /// </summary>
    public static SortedDictionary<string, SortedDictionary<string, object?>> ToMaskedDocument(IAcuConfiguration config)
    {
        List<(string?, string, object)> values = new List<(string?, string, object)>();
        ReadConfigurationValues(config, nameof(IAcuConfiguration), values);

        SortedDictionary<string, SortedDictionary<string, object?>> document = new(StringComparer.Ordinal);
        foreach ((string? section, string name, object value) in values)
        {
            if (section == null || section == nameof(IAcuConfiguration))
                continue;
            string sectionKey = JsonNamingPolicy.CamelCase.ConvertName(section);
            if (!document.TryGetValue(sectionKey, out SortedDictionary<string, object?>? fields))
                document[sectionKey] = fields = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            fields[JsonNamingPolicy.CamelCase.ConvertName(name)] = value switch
            {
                Uri uri => uri.ToString(),
                _ => value,
            };
        }

        return document;
    }

    private static IAcuConfiguration ReadConfig(string? filename)
    {
        if (filename == null || !File.Exists(filename))
            return AcuNullConfiguration.Instance;
        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        string data = File.ReadAllText(filename);
        IAcuConfiguration? config = JsonSerializer.Deserialize<IAcuConfiguration>(data, options);
        return config!;
    }

    private static void ReadConfigurationValues(
        object config,
        string? currentConfigType,
        ICollection<(string?, string, object)> res
    )
    {
        Type t = config.GetType();
        IEnumerable<PropertyInfo> properties = t.GetProperties().Where(prop => prop is { CanRead: true, CanWrite: true });
        foreach (PropertyInfo? prop in properties)
            if (prop.PropertyType.Assembly == t.Assembly)
            {
                object? sConfig = prop.GetValue(config);
                string configType = prop.Name;
                ReadConfigurationValues(sConfig!, configType, res);
            }
            else
            {
                object? value = prop.GetValue(config, null);
                if (value != null)
                    res.Add((currentConfigType, prop.Name,
                        prop.GetCustomAttribute<SecretAttribute>() is not null ? MaskText : value));
            }
    }
}
