using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Localization;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [CommandHelper(minArgs: 1, usage: "<number of minutes to extend the map time e.g. 15>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        public void OnExtendRoundTimeCommand(CCSPlayerController? player, CommandInfo commandInfo)
        {
            if (player != null && !PermissionUtility.HasAny(player, Config.General.ExtendPermissions))
            {
                commandInfo.ReplyToCommand($"[RTV] {ChatColors.Red}You do not have the correct permission to execute this command.");
                return;
            }

            var newRoundTime = commandInfo.GetArg(1);

            var intParseSuccess = int.TryParse(newRoundTime, out int newRoundTimeInt);

            if (intParseSuccess)
            {
                _extendRoundTime.CommandHandler(player!, commandInfo, newRoundTimeInt);
            }
            else
            {
                commandInfo.ReplyToCommand("You entered an incorrect integer for the roundtime. Try a number between 1 and 60.");
            }
        }
    }

    public class ExtendRoundTimeCommand(TimeLimitManager timeLimitManager, ExtendRoundTimeManager extendRoundTimeManager, GameRules gameRules, IStringLocalizer stringLocalizer) : IPluginDependency<Plugin, Config>
    {
        private TimeLimitManager _timeLimitManager = timeLimitManager;
        private readonly ExtendRoundTimeManager _extendRoundTimeManager = extendRoundTimeManager;
        private readonly GameRules _gameRules = gameRules;
        private StringLocalizer _localizer = new(stringLocalizer, "extendtime.prefix");

        public bool CommandHandler(CCSPlayerController player, CommandInfo commandInfo, int timeToExtend)
        {
            if (_gameRules.WarmupRunning)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                return false;
            }

            if (!_timeLimitManager.UnlimitedTime)
            {
                if (_timeLimitManager.TimeRemaining > 1)
                {
                    _extendRoundTimeManager.ExtendRoundTime(timeToExtend, _timeLimitManager, _gameRules);

                    commandInfo.ReplyToCommand($"Increased round time by {timeToExtend} minute(s)");

                    return true;
                }
                else
                {
                    player.PrintToChat(_localizer.LocalizeWithPrefix("extendtime.notapplicable"));
                    return false;
                }
            }
            else
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("extendtime.notapplicable"));
                return false;
            }
        }
    }
}
