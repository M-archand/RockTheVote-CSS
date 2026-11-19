using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        public void OnReloadMapsCommand(CCSPlayerController? player, CommandInfo command)
        {
            _reloadMapsCommand.CommandHandler(player, command);
        }
    }

    public class ReloadMapsCommand : IPluginDependency<Plugin, Config>
    {
        private readonly MapLister _mapLister;
        private readonly ILogger<ReloadMapsCommand> _logger;
        private GeneralConfig _generalConfig = new();

        public ReloadMapsCommand(MapLister mapLister, ILogger<ReloadMapsCommand> logger)
        {
            _mapLister = mapLister;
            _logger = logger;
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
        }

        private string ResultMessage() => _mapLister.LastLoadFailed
            ? $"[RTV] {ChatColors.Red}Map list reload failed. Check the server console/logs for details."
            : $"[RTV] {ChatColors.Lime}Map list reloaded successfully.";

        public void CommandHandler(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null && !PermissionUtility.HasAny(player, _generalConfig.AdminPermissions))
            {
                command.ReplyToCommand($"[RTV] {ChatColors.Red}You do not have the correct permission to execute this command.");
                return;
            }

            try
            {
                _mapLister.LoadMaps();

                if (!_mapLister.IsLoading)
                {
                    command.ReplyToCommand(ResultMessage());
                    return;
                }

                // Database loads finish on a later frame, so CommandInfo can't be used for the reply
                command.ReplyToCommand($"[RTV] {ChatColors.Lime}Reloading the map list from the database...");
                int? slot = player?.Slot;
                ulong steamId = player?.SteamID ?? 0;
                _mapLister.WhenLoaded(() =>
                {
                    string message = ResultMessage();
                    if (slot is null)
                    {
                        Server.PrintToConsole(message);
                        return;
                    }

                    // The slot may belong to someone else by now
                    var live = Utilities.GetPlayerFromSlot(slot.Value);
                    if (live != null && live.IsValid && live.SteamID == steamId)
                        live.PrintToChat(message);
                });
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "[RTV.ReloadMaps] maplist.txt not found while running css_reloadmaps.");
                command.ReplyToCommand($"[RTV] {ChatColors.Red}maplist.txt not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.ReloadMaps] Failed to reload map list via css_reloadmaps.");
                command.ReplyToCommand($"[RTV] {ChatColors.Red}Failed to reload map list. Check server logs for details.");
            }
        }
    }
}
