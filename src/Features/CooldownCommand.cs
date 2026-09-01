using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class CooldownCommand : IPluginDependency<Plugin, Config>
    {
        private const int CommandCooldownSeconds = 10;

        private readonly ILogger<CooldownCommand> _logger;
        private readonly StringLocalizer _localizer;
        private readonly MapCooldown _mapCooldown;
        private Plugin? _plugin;

        private List<string> _commands = new();
        private readonly HashSet<string> _registeredAliases = new(StringComparer.OrdinalIgnoreCase);

        // Rebuilt on map start (EventCooldownRefreshed), newest map first
        private string[] _cachedCooldownMaps = Array.Empty<string>();

        // Per-player usage timestamps for the command cooldown
        private readonly Dictionary<ulong, DateTime> _lastUse = new();

        public CooldownCommand(MapCooldown mapCooldown, StringLocalizer localizer, ILogger<CooldownCommand> logger)
        {
            _mapCooldown = mapCooldown;
            _localizer = localizer;
            _logger = logger;

            _mapCooldown.EventCooldownRefreshed += (_, _) => RefreshCache();
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            RefreshCache();
        }

        public void OnConfigParsed(Config config)
        {
            _commands = config.General.CooldownCommands;

            if (_commands.Count == 0)
                return;

            Server.NextFrame(() =>
            {
                foreach (var alias in _commands.Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)))
                {
                    // Skip aliases already registered
                    if (_plugin == null || _registeredAliases.Contains(alias))
                        continue;

                    _plugin.AddCommand(alias, "Prints the maps currently on cooldown to your console", ExecuteCommand);
                    _registeredAliases.Add(alias);
                }
            });
        }

        private void RefreshCache()
        {
            // Snapshot in descending order, most recently played at the top
            _cachedCooldownMaps = _mapCooldown.MapsOnCooldown.Reverse().ToArray();
            _lastUse.Clear();
        }

        private void ExecuteCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (player == null || !player.IsValid)
                return;

            try
            {
                var now = DateTime.UtcNow;
                if (_lastUse.TryGetValue(player.SteamID, out var last))
                {
                    double elapsed = (now - last).TotalSeconds;
                    if (elapsed < CommandCooldownSeconds)
                    {
                        int remaining = (int)Math.Ceiling(CommandCooldownSeconds - elapsed);
                        player.PrintToChat(_localizer.LocalizeWithPrefix("rtv.cooldown", remaining));
                        return;
                    }
                }
                _lastUse[player.SteamID] = now;

                var maps = _cachedCooldownMaps;
                if (maps.Length == 0)
                {
                    player.PrintToChat(_localizer.LocalizeWithPrefix("cooldown.none"));
                    return;
                }

                // Tell them to check console
                player.PrintToChat(_localizer.LocalizeWithPrefix("cooldown.check-console"));

                player.PrintToConsole("====================================");
                player.PrintToConsole("          Maps on Cooldown");
                player.PrintToConsole($"          Total Maps: {maps.Length}");
                player.PrintToConsole("====================================");

                for (int i = 0; i < maps.Length; i++)
                {
                    if (i == 0)
                        player.PrintToConsole($"{maps[i]} {_localizer.Localize("cooldown.most-recent")}");
                    else
                        player.PrintToConsole(maps[i]);
                }

                player.PrintToConsole("====================================");
            }
            catch (Exception ex)
            {
                _logger.LogError("[RTV.Cooldown] An error occurred while printing the cooldown list: {Message}", ex.Message);
            }
        }
    }
}
