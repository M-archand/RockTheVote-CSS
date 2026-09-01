using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public static class CustomHudGameData
    {
        private const string FileName = "customhud.json";

        private static readonly Dictionary<string, string> _signatures = new();
        private static bool _loaded;

        public static bool Loaded => _loaded;
        public static string Source { get; private set; } = "";

        // Reads customhud.json on load from addons/counterstrikesharp/gamedata
        public static void Load(string moduleDirectory, ILogger logger)
        {
            if (_loaded)
                return;
            _loaded = true;

            string path = Path.GetFullPath(Path.Combine(moduleDirectory, "..", "..", "gamedata", FileName));
            Source = path;

            if (!File.Exists(path))
            {
                logger.LogWarning("[RTV.CustomHud] Gamedata file not found at {Path}; panorama menu unavailable, falling back to chat voting.", path);
                return;
            }

            try
            {
                bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                using var doc = JsonDocument.Parse(File.ReadAllText(path), options);

                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    if (!entry.Value.TryGetProperty("signatures", out var signatures))
                        continue;

                    if (signatures.TryGetProperty(windows ? "windows" : "linux", out var pattern)
                        && pattern.GetString() is { Length: > 0 } value)
                    {
                        _signatures[entry.Name] = value;
                    }
                }

                logger.LogDebug("[RTV.CustomHud] Loaded {Count} signatures from {Path}.", _signatures.Count, path);
            }
            catch (Exception ex)
            {
                _signatures.Clear();
                logger.LogWarning(ex, "[RTV.CustomHud] Failed to parse {Path}; panorama menu unavailable, chat voting stays.", path);
            }
        }

        // Fetches a loaded signature pattern by gamedata key
        public static bool TryGetSignature(string name, out string signature)
        {
            if (_signatures.TryGetValue(name, out var value))
            {
                signature = value;
                return true;
            }

            signature = "";
            return false;
        }
    }
}
