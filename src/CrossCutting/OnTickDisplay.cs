using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using static CounterStrikeSharp.API.Core.Listeners;

namespace cs2_rockthevote.CrossCutting
{
    public class OnTickDisplay : IPluginDependency<Plugin, Config>
    {
        private readonly PluginState _pluginState;
        private readonly EndMapVoteManager _endMap;
        private readonly RockTheVoteCommand _rtv;
        private readonly ExtendRoundTimeManager _voteExtend;
        private readonly StringLocalizer _localizer;
        private readonly StringBuilder _hudBuilder = new();
        private readonly bool[] _activeSlots = new bool[VoteConstants.MAXPLAYERS];
        private Plugin? _plugin;
        private bool _hooked;
        private GeneralConfig _generalConfig = new();
        private EndOfMapConfig _endMapConfig = new();
        private VoteExtendConfig _voteExtendConfig = new();
        private NominateConfig _nomConfig = new();
        private RtvConfig _rtvConfig = new();

        public OnTickDisplay(PluginState pluginState, StringLocalizer localizer, EndMapVoteManager endMap, RockTheVoteCommand rtv, ExtendRoundTimeManager voteExtend)
        {
            _pluginState = pluginState;
            _localizer = localizer;
            _endMap = endMap;
            _rtv = rtv;
            _voteExtend = voteExtend;
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
            _endMapConfig = config.EndOfMapVote;
            _voteExtendConfig = config.VoteExtend;
            _nomConfig = config.Nominate;
            _rtvConfig = config.Rtv;

            if (_plugin != null)
                ApplyHookState(_plugin);
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            ApplyHookState(plugin);
        }

        private bool ShouldHook() =>
            ConfigValue.Is(_endMapConfig.CountdownType, "hud")
            || ConfigValue.Is(_rtvConfig.CountdownType, "hud")
            || ConfigValue.Is(_voteExtendConfig.CountdownType, "hud")
            || ConfigValue.Is(_endMapConfig.MenuType, "HudMenu")
            || ConfigValue.Is(_nomConfig.MenuType, "HudMenu");

        private void ApplyHookState(Plugin plugin)
        {
            bool shouldHook = ShouldHook();

            if (shouldHook && !_hooked)
            {
                plugin.RegisterListener<OnTick>(PlayerOnTick);
                plugin.RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
                plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnCache, HookMode.Pre);
                plugin.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnectCache, HookMode.Pre);
                _hooked = true;

                Server.NextFrame(() =>
                {
                    foreach (var player in ServerManager.ValidPlayers())
                        CachePlayer(player);
                });
            }
            else if (!shouldHook && _hooked)
            {
                plugin.RemoveListener<OnTick>(PlayerOnTick);
                plugin.DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
                plugin.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnCache, HookMode.Pre);
                plugin.DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnectCache, HookMode.Pre);
                Array.Clear(_activeSlots, 0, _activeSlots.Length);
                _hooked = false;
            }
        }

        public void Unload(Plugin plugin)
        {
            plugin.RemoveListener<OnTick>(PlayerOnTick);
            if (_hooked)
            {
                plugin.DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
                plugin.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnCache, HookMode.Pre);
                plugin.DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnectCache, HookMode.Pre);
                _hooked = false;
            }
        }

        public void OnMapStart(string map)
        {
            Array.Clear(_activeSlots, 0, _activeSlots.Length);

            if (_hooked)
            {
                Server.NextFrame(() =>
                {
                    foreach (var player in ServerManager.ValidPlayers())
                        CachePlayer(player);
                });
            }
        }

        private void CachePlayer(CCSPlayerController? player)
        {
            if (player.ReallyValid() && player!.Slot >= 0 && player.Slot < _activeSlots.Length)
                _activeSlots[player.Slot] = true;
        }

        private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            CachePlayer(@event.Userid);
            return HookResult.Continue;
        }

        private HookResult OnPlayerSpawnCache(EventPlayerSpawn @event, GameEventInfo info)
        {
            CachePlayer(@event.Userid);
            return HookResult.Continue;
        }

        private HookResult OnPlayerDisconnectCache(EventPlayerDisconnect @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player != null && player.Slot >= 0 && player.Slot < _activeSlots.Length)
                _activeSlots[player.Slot] = false;
            return HookResult.Continue;
        }

        private void PrintCenterToAll(string text)
        {
            for (int slot = 0; slot < _activeSlots.Length; slot++)
            {
                if (!_activeSlots[slot])
                    continue;

                var player = Utilities.GetPlayerFromSlot(slot);
                if (player != null && player.IsValid)
                    player.PrintToCenter(text);
            }
        }

        public void PlayerOnTick()
        {
            // Only shown while a vote is running
            if (!_pluginState.EofVoteHappening && !_pluginState.ExtendTimeVoteHappening && !_pluginState.RtvVoteHappening)
                return;

            // EndMapVote HUD Countdown. Don't show if EnabledHudMenu true, otherwise this would be covered by the map list
            if (_endMapConfig.EnableCountdown && ConfigValue.Is(_endMapConfig.CountdownType, "hud") && _pluginState.EofVoteHappening && !ConfigValue.Is(_endMapConfig.MenuType, "HudMenu"))
            {
                PrintCenterToAll(_localizer.Localize("emv.hud.timer", _endMap.TimeLeft));
            }

            // RTV HUD Countdown
            if (_rtvConfig.EnableCountdown && ConfigValue.Is(_rtvConfig.CountdownType, "hud") && _pluginState.RtvVoteHappening)
            {
                PrintCenterToAll(_localizer.Localize("general.hud-countdown", _rtv.TimeLeft));
            }

            // VoteExtend HUD Countdown
            if (_voteExtendConfig.EnableCountdown && ConfigValue.Is(_voteExtendConfig.CountdownType, "hud") && _pluginState.ExtendTimeVoteHappening)
            {
                PrintCenterToAll(_localizer.Localize("general.hud-countdown", _voteExtend.TimeLeft));
            }

            // HUD map vote list
            if (ConfigValue.Is(_endMapConfig.MenuType, "HudMenu") && _pluginState.EofVoteHappening)
            {
                _hudBuilder.Clear();
                _hudBuilder.Append($"<b><font color='yellow'>{_localizer.Localize("emv.hud.timer", _endMap.TimeLeft)}</font></b>");

                int idx = 1;
                const string header = "<br><font color='yellow'>!{0}</font> {1} <font color='lime'>({2})</font>";
                foreach (var kv in _endMap.SortedTopVotes)
                {
                    _hudBuilder.AppendFormat(header, idx++, kv.Key, kv.Value);
                }

                var hud = _hudBuilder.ToString();
                for (int slot = 0; slot < _activeSlots.Length; slot++)
                {
                    if (!_activeSlots[slot])
                        continue;

                    if (_generalConfig.HideHudAfterVote && _endMap.VotedPlayers.Contains(slot))
                        continue;

                    var player = Utilities.GetPlayerFromSlot(slot);
                    if (player == null || !player.IsValid)
                        continue;

                    player.PrintToCenterHtml(hud);
                }
            }
        }
        
    }
}
