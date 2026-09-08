using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Modules.Extensions;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public static class ConfigValidator
    {
        public static void WarnMissingEntries(Config config, ILogger logger)
        {
            try
            {
                var path = config.GetConfigPath();

                if (!File.Exists(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    return;

                using var doc = JsonDocument.Parse(File.ReadAllText(path),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return;

                var rootDefaults = new Config();
                foreach (var property in typeof(Config).GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (IsIgnored(property))
                        continue;

                    var jsonName = JsonNameOf(property);
                    var isSection = property.PropertyType.IsClass && property.PropertyType.Namespace == typeof(Config).Namespace;

                    if (!TryGetPropertyIgnoreCase(root, jsonName, out var element))
                    {
                        if (isSection)
                            logger.LogWarning("[RTV.Config] {Section} section is missing from your config, falling back to default values for all of its settings",
                                jsonName);
                        else
                            logger.LogWarning("[RTV.Config] Config.{Key} is missing from your config, falling back to default value ({Default})",
                                jsonName, FormatValue(property.GetValue(rootDefaults)));
                        continue;
                    }

                    if (!isSection)
                        continue;

                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        logger.LogWarning("[RTV.Config] {Section} section is not an object in your config, falling back to default values for all of its settings",
                            jsonName);
                        continue;
                    }

                    var sectionDefaults = Activator.CreateInstance(property.PropertyType)!;
                    foreach (var key in property.PropertyType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    {
                        if (IsIgnored(key) || !key.CanWrite)
                            continue;

                        if (!TryGetPropertyIgnoreCase(element, JsonNameOf(key), out _))
                            logger.LogWarning("[RTV.Config] {Section}.{Key} is missing from your config, falling back to default value ({Default})",
                                jsonName, JsonNameOf(key), FormatValue(key.GetValue(sectionDefaults)));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[RTV.Config] Failed to check the config file for missing entries");
            }
        }

        private static bool IsIgnored(PropertyInfo property) =>
            property.GetCustomAttribute<JsonIgnoreAttribute>() != null;

        private static string JsonNameOf(PropertyInfo property) =>
            property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;

        private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var member in obj.EnumerateObject())
            {
                if (string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = member.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string FormatValue(object? value) => value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            string s => $"\"{s}\"",
            IEnumerable list => "[" + string.Join(", ", list.Cast<object?>().Select(FormatValue)) + "]",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };
    }
}
