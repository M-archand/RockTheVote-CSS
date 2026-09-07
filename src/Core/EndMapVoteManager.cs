using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Menu;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Drawing;
using System.Data;

namespace cs2_rockthevote
{
    public class EndMapVoteManager : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<EndMapVoteManager> _logger;
        private ILogger _debugLogger = NullLogger<EndMapVoteManager>.Instance;
        private readonly MapLister _mapLister;
        private readonly ExtendRoundTimeManager _extendRoundTimeManager;
        private readonly TimeLimitManager _timeLimitManager;
        private readonly ChangeMapManager _changeMapManager;
        private readonly NominationCommand _nominationManager;
        private readonly StringLocalizer _localizer;
        private readonly PluginState _pluginState;
        private readonly MapCooldown _mapCooldown;
        private readonly GameRules _gameRules;
        private readonly CustomHud _customHud;
        private bool _activeVoteUsesPanorama = false;
        private Timer? Timer;
        private Timer? _nextVoteTimer;
        private Timer? _chatMapChoiceTimer;
        private Timer? _ignoreWinConditionsPollTimer;
        private readonly List<Timer> _hintOuterTimers = new();
        private readonly List<CEnvInstructorHint> _hintEntities = new();
        List<string> mapsElected = new();
        private readonly Dictionary<int, string> _playerVotes = new();
        private readonly HashSet<int> _revoteMenuOpen = new();
        private readonly List<string> _currentVoteOptions = new();
        private int _canVote = 0;
        private bool _activeVoteIsRtv = false;
        private Plugin? _plugin;

        public int TimeLeft { get; private set; } = -1;
        public int MaxOptionsHud { get; private set; } = 6;
        public ISet<int> VotedPlayers { get; private set; } = new HashSet<int>();
        public Dictionary<string,int> Votes { get; private set; } = new();
        public IReadOnlyDictionary<string, int> CurrentVotes => Votes;

        private List<KeyValuePair<string, int>> _sortedTopVotes = new();
        public IReadOnlyList<KeyValuePair<string, int>> SortedTopVotes => _sortedTopVotes;

        private void RebuildSortedTopVotes()
        {
            _sortedTopVotes = Votes
                .OrderByDescending(x => x.Value)
                .Take(MaxOptionsHud)
                .ToList();
        }

        private GeneralConfig _generalConfig = new();
        private EndOfMapConfig _endMapConfig = new();
        private PanoramaMenuConfig _panoramaConfig = new();
        private RtvConfig _rtvConfig = new();

        public EndMapVoteManager
        (
            MapLister mapLister,
            ChangeMapManager changeMapManager,
            NominationCommand nominationManager,
            StringLocalizer localizer,
            PluginState pluginState,
            MapCooldown mapCooldown,
            ExtendRoundTimeManager extendRoundTimeManager,
            TimeLimitManager timeLimitManager,
            GameRules gameRules,
            CustomHud customHud,
            ILogger<EndMapVoteManager> logger
        )
        {
            _mapLister = mapLister;
            _changeMapManager = changeMapManager;
            _nominationManager = nominationManager;
            _localizer = localizer;
            _pluginState = pluginState;
            _mapCooldown = mapCooldown;
            _extendRoundTimeManager = extendRoundTimeManager;
            _timeLimitManager = timeLimitManager;
            _gameRules = gameRules;
            _customHud = customHud;
            _logger = logger;
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            _plugin.AddCommand("revote", "Re-open the active map vote menu.", OnRevoteCommand);
            _plugin.AddCommandListener("say", OnSayVote, HookMode.Pre);
            _plugin.AddCommandListener("say_team", OnSayVote, HookMode.Pre);
            _plugin.RegisterListener<Listeners.OnCustomHudClicked>(OnPanoramaHudClicked);
        }

        // Click path for the panorama vote panel. CSS raises this from its
        // CS_UM_CustomHudClicked usermessage hook, so the actual work is deferred to the
        // next frame.
        private void OnPanoramaHudClicked(CCSPlayerController player, CCSCustomHudLayout customLayout, string buttonId)
        {
            if (player == null || !player.IsValid)
                return;

            int playerSlot = player.Slot;
            nint layoutPointer = customLayout?.Handle ?? 0;
            Server.NextFrame(() => HandlePanoramaClick(playerSlot, layoutPointer, buttonId));
        }

        private void HandlePanoramaClick(int playerSlot, nint layoutPointer, string buttonId)
        {
            try
            {
                //_logger.LogInformation(
                //    "[RTV.EndMapVote] CustomHudClicked: button='{Button}' layout=0x{Layout:X} slot={Slot} voteActive={VoteActive} clickActive={ClickActive}",
                //    buttonId, layoutPointer, playerSlot, _pluginState.EofVoteHappening, _customHud.ClickVotingActive);

                if (!_activeVoteUsesPanorama || !_pluginState.EofVoteHappening || Timer is null)
                    return;

                if (!_customHud.ClickVotingActive)
                    return;

                // Only react to clicks on our own layout entity
                if (!_customHud.MatchesLayout(layoutPointer))
                {
                    //_logger.LogInformation("[RTV.EndMapVote] CustomHudClicked layout mismatch (theirs 0x{Layout:X}), ignoring.", layoutPointer);
                    return;
                }

                var player = Utilities.GetPlayerFromSlot(playerSlot);
                if (player == null || !player.ReallyValid())
                {
                    //_logger.LogInformation("[RTV.EndMapVote] CustomHudClicked from slot {Slot} has no valid player, ignoring.", playerSlot);
                    return;
                }

                if (buttonId == "rtv_vote_close")
                {
                    _customHud.SetVisibleForPlayer(player, false);
                    return;
                }

                const string rowPrefix = "rtv_vote_";
                if (buttonId.StartsWith(rowPrefix, StringComparison.Ordinal)
                    && int.TryParse(buttonId.AsSpan(rowPrefix.Length), out int choice)
                    && choice >= 1 && choice <= _currentVoteOptions.Count)
                {
                    MapVoted(player, _currentVoteOptions[choice - 1], _activeVoteIsRtv, allowRevote: true);
                    return;
                }

                //_logger.LogInformation("[RTV.EndMapVote] CustomHudClicked button '{Button}' matched no vote row.", buttonId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.EndMapVote] Failed to handle CustomHudClicked.");
            }
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
            _endMapConfig = config.EndOfMapVote;
            _panoramaConfig = config.PanoramaMenu;
            _rtvConfig = config.Rtv;
            _debugLogger = _generalConfig.DebugLogging ? _logger : NullLogger<EndMapVoteManager>.Instance;

            if (!uint.TryParse(_endMapConfig.SoundPath, out _) && !SoundEventHelper.IsFullVolume(_endMapConfig.SoundVolume))
            {
                _logger.LogWarning("EndOfMapVote: To modify the sound volume (any value aside from 1) you need to use the soundevent_hash rather than the sound path");
            }

            // Check to make sure VoteDuration isn't >= TriggerSecondsBeforeEnd, if it is, use a fallback
            if (_endMapConfig.VoteDuration >= _endMapConfig.TriggerSecondsBeforeEnd)
            {
                var original = _endMapConfig.VoteDuration;
                var adjusted = Math.Max(1, _endMapConfig.TriggerSecondsBeforeEnd - 5);
                _endMapConfig.VoteDuration = adjusted;

                _logger.LogError(
                    "EndOfMapVote config invalid: VoteDuration ({VoteDuration}s) must be less than " +
                    "TriggerSecondsBeforeEnd ({TriggerSecondsBeforeEnd}s). Automatically adjusting VoteDuration to {AdjustedVoteDuration}s.",
                    original,
                    _endMapConfig.TriggerSecondsBeforeEnd,
                    adjusted
                );
            }
        }

        public void OnMapStart(string map)
        {
            Votes.Clear();
            _playerVotes.Clear();
            _revoteMenuOpen.Clear();
            _currentVoteOptions.Clear();
            _sortedTopVotes = new();
            TimeLeft = 0;
            mapsElected.Clear();
            _activeVoteUsesPanorama = false;
            KillTimer();
            KillNextVoteTimer();
            KillIgnoreWinConditionsPollTimer();
            KillHintEntities();
        }

        public void Unload(Plugin plugin)
        {
            CloseAllActiveMenus();
            plugin.RemoveCommandListener("say", OnSayVote, HookMode.Pre);
            plugin.RemoveCommandListener("say_team", OnSayVote, HookMode.Pre);
            plugin.RemoveListener<Listeners.OnCustomHudClicked>(OnPanoramaHudClicked);
            _activeVoteUsesPanorama = false;
            _revoteMenuOpen.Clear();
            KillTimer();
            KillNextVoteTimer();
            KillChatMapChoiceTimer();
            KillIgnoreWinConditionsPollTimer();
            KillHintEntities();
        }

        private void KillIgnoreWinConditionsPollTimer()
        {
            _ignoreWinConditionsPollTimer?.Kill();
            _ignoreWinConditionsPollTimer = null;
        }

        private void KillNextVoteTimer()
        {
            _nextVoteTimer?.Kill();
            _nextVoteTimer = null;
        }

        private void KillChatMapChoiceTimer()
        {
            _chatMapChoiceTimer?.Kill();
            _chatMapChoiceTimer = null;
        }

        private void KillHintEntities()
        {
            bool hadHints = _hintOuterTimers.Count > 0 || _hintEntities.Count > 0;

            foreach (var t in _hintOuterTimers)
            {
                try { t.Kill(); }
                catch (Exception ex) { _debugLogger.LogError(ex, "[Hint] Failed to kill outer hint timer"); }
            }
            _hintOuterTimers.Clear();

            foreach (var entity in _hintEntities)
            {
                try
                {
                    if (entity.IsValid)
                        entity.AcceptInput("Kill");
                }
                catch (Exception ex) { _debugLogger.LogError(ex, "[Hint] Failed to kill hint entity"); }
            }
            _hintEntities.Clear();

            // Restore instructor convar state directly since killing the timers above skips their cleanup
            if (hadHints)
            {
                Server.ExecuteCommand("sv_gameinstructor_disable true");
                foreach (var snapshot in ServerManager.ValidPlayers())
                {
                    int slot = snapshot.Slot;
                    try
                    {
                        var live = Utilities.GetPlayerFromSlot(slot);
                        if (live is not null && live.ReallyValid())
                            live.ReplicateConVar("sv_gameinstructor_enable", "false");
                    }
                    catch (Exception ex) { _debugLogger.LogError(ex, "[Hint] Failed to restore instructor convar. slot={Slot}", slot); }
                }
            }
        }

        private bool IsPanoramaMode()
        {
            return ConfigValue.Is(_endMapConfig.MenuType, "panorama");
        }

        // Chat-number voting applies only when a panorama vote runs without click
        // voting (PanoramaMenu.EnableClickVoting off), the panel is display only so 
        // chat is the input path.
        private bool PanoramaChatFallbackActive()
        {
            return _activeVoteUsesPanorama && !_customHud.ClickVotingActive;
        }

        private bool ShouldPrintChatMapChoices()
        {
            bool chatDriven = ConfigValue.Is(_endMapConfig.MenuType, "ChatMenu")
                || PanoramaChatFallbackActive();
            return chatDriven
                && _endMapConfig.ChatMapChoiceReminder
                && _endMapConfig.ChatMapChoiceInterval > 0;
        }

        private void PrintChatMapChoices(CCSPlayerController? target = null)
        {
            if (_currentVoteOptions.Count == 0)
                return;

            void Print(string text)
            {
                if (target != null)
                    target.PrintToChat(text);
                else
                    Server.PrintToChatAll(text);
            }

            Print(_localizer.Localize("emv.hud.menu-title"));

            for (int i = 0; i < _currentVoteOptions.Count; i++)
            {
                string option = _currentVoteOptions[i];
                int voteCount = Votes.TryGetValue(option, out int currentVotes) ? currentVotes : 0;
                Print($" {ChatColors.Lime}!{i + 1} {ChatColors.Default}- {ChatColors.Yellow}({ChatColors.Orange}{voteCount}{ChatColors.Yellow}) {ChatColors.Default}- {option}");
            }

            if (_endMapConfig.EnableRevote)
                Print(_localizer.Localize("emv.revote"));
        }

        // Numeric chat voting (!1..!N), fallback input for a display-only panorama panel.
        // When click voting is active, votes come only from panel clicks.
        private HookResult OnSayVote(CCSPlayerController? player, CommandInfo info)
        {
            if (!PanoramaChatFallbackActive() || !_pluginState.EofVoteHappening || Timer is null)
                return HookResult.Continue;

            if (player == null || !player.ReallyValid())
                return HookResult.Continue;

            string text = info.GetArg(1).Trim();
            if (text.StartsWith('!') || text.StartsWith('/'))
                text = text[1..];

            if (!int.TryParse(text, out int choice))
                return HookResult.Continue;

            if (choice < 1 || choice > _currentVoteOptions.Count)
                return HookResult.Continue;

            MapVoted(player, _currentVoteOptions[choice - 1], _activeVoteIsRtv, allowRevote: true);
            return HookResult.Handled;
        }

        private void StartChatMapChoiceReminder()
        {
            KillChatMapChoiceTimer();

            if (_plugin == null || !ShouldPrintChatMapChoices())
                return;

            _chatMapChoiceTimer = _plugin.AddTimer(_endMapConfig.ChatMapChoiceInterval, () =>
            {
                if (!_pluginState.EofVoteHappening || TimeLeft <= 0 || !ShouldPrintChatMapChoices())
                {
                    KillChatMapChoiceTimer();
                    return;
                }

                PrintChatMapChoices();
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        public void ScheduleNextVote()
        {
            KillNextVoteTimer();

            _nextVoteTimer = _plugin?.AddTimer(1.0F, () =>
            {
                if (_timeLimitManager.UnlimitedTime)
                    return;

                float timeRemainingSeconds = (float)Math.Max(0, (double)_timeLimitManager.TimeRemaining);
                if (timeRemainingSeconds <= _endMapConfig.TriggerSecondsBeforeEnd)
                {
                    KillNextVoteTimer();
                    _pluginState.EofVoteHappening = false;
                    _changeMapManager.OnMapStart(Server.MapName);
                    StartVote(isRtv: false);
                }
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        // Fallback poll for triggers without a guaranteed native event (e.g. IgnoredWinConditions)
        private void StartIgnoreWinConditionsPoll(string winnerMapName, MapChangeTrigger trigger = MapChangeTrigger.IgnoredWinConditions)
        {
            KillIgnoreWinConditionsPollTimer();

            _debugLogger.LogInformation(
                "[RTV.MapChange] Arming plugin-owned end-of-map poll (deferring creation to next frame). map={Map} trigger={Trigger} currentMap={CurrentMap}",
                winnerMapName,
                trigger,
                Server.MapName
            );

            Server.NextFrame(() =>
            {
                _debugLogger.LogInformation("[RTV.MapChange] NextFrame: creating poll timer now. map={Map} trigger={Trigger}", winnerMapName, trigger);
                CreateIgnoreWinConditionsPollTimer(winnerMapName, trigger);
            });
        }

        private void CreateIgnoreWinConditionsPollTimer(string winnerMapName, MapChangeTrigger trigger = MapChangeTrigger.IgnoredWinConditions)
        {
            _ignoreWinConditionsPollTimer = _plugin?.AddTimer(1.0F, () =>
            {
                try { _debugLogger.LogInformation("[RTV.MapChange] Poll tick entry. map={Map} trigger={Trigger}", winnerMapName, trigger); }
                catch { }

                try
                {
                    if (!_pluginState.MapChangeScheduled)
                    {
                        _debugLogger.LogInformation("[RTV.MapChange] Poll tick: change no longer scheduled, stopping. map={Map} trigger={Trigger}", winnerMapName, trigger);
                        KillIgnoreWinConditionsPollTimer();
                        return;
                    }

                    _debugLogger.LogInformation("[RTV.MapChange] Poll tick: computing remaining seconds. map={Map} trigger={Trigger}", winnerMapName, trigger);
                    int remainingSeconds = ComputeRemainingSecondsForIgnoreWinConditions();
                    _debugLogger.LogInformation(
                        "[RTV.MapChange] Poll tick: computed. remainingSeconds={Remaining} map={Map} trigger={Trigger}",
                        remainingSeconds,
                        winnerMapName,
                        trigger
                    );

                    if (remainingSeconds <= 3)
                    {
                        _debugLogger.LogInformation(
                            "[RTV.MapChange] Plugin-owned trigger fired. remainingSeconds={Remaining} map={Map} trigger={Trigger}",
                            remainingSeconds,
                            winnerMapName,
                            trigger
                        );
                        KillIgnoreWinConditionsPollTimer();
                        _changeMapManager.ChangeNextMap(trigger);
                    }
                }
                catch (Exception ex)
                {
                    _debugLogger.LogError(ex, "[RTV.MapChange] Plugin-owned poll callback failed. map={Map} trigger={Trigger}", winnerMapName, trigger);
                }
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        private int ComputeRemainingSecondsForIgnoreWinConditions()
        {
            if (!_timeLimitManager.UnlimitedTime)
            {
                float remaining = (float)Math.Max(0, (double)_timeLimitManager.TimeRemaining);
                return (int)Math.Floor(remaining);
            }

            float roundTime = _gameRules.RoundTime;
            float gameStartTime = _gameRules.GameStartTime;
            float elapsed = Server.CurrentTime - gameStartTime;
            return (int)Math.Floor(roundTime - elapsed);
        }

        private void OnRevoteCommand(CCSPlayerController? player, CommandInfo _)
        {
            if (player == null || !player.IsValid)
                return;

            if (!_pluginState.EofVoteHappening || Timer is null || _currentVoteOptions.Count == 0)
                return;

            if (!_endMapConfig.EnableRevote)
                return;

            // Re-show Panorama panel, the player clicks another row (chat list only
            // when the vote is running in the chat-number fallback)
            if (_activeVoteUsesPanorama)
            {
                _customHud.SetVisibleForPlayer(player, true);
                if (PanoramaChatFallbackActive())
                    PrintChatMapChoices(player);
                return;
            }

            // Already showing a revote menu for this player - their vote is active, ignore repeat requests
            if (!_revoteMenuOpen.Add(player.Slot))
                return;

            int menuTimeLeft = Math.Max(TimeLeft, 1);
            DisplayRevoteMenu(player, _currentVoteOptions, menuTimeLeft, _activeVoteIsRtv);
        }

        // Revote always uses a frozen WASD menu with no exit button, forcing the player to make a selection
        private void DisplayRevoteMenu(CCSPlayerController player, IEnumerable<string> voteOptions, int durationSeconds, bool isRtv)
        {
            if (_plugin == null || !player.IsValid)
                return;

            var title = _localizer.Localize("emv.hud.menu-title");
            var menu = new WasdMenu(title, _plugin)
            {
                ExitButton = false,
                WasdMenu_FreezePlayer = true
            };

            foreach (var option in voteOptions)
            {
                var chosen = option;
                menu.AddItem(chosen, (p, _) =>
                {
                    _revoteMenuOpen.Remove(p.Slot);

                    MapVoted(p, chosen, isRtv, allowRevote: true);
                });
            }

            menu.Display(player, durationSeconds);
        }

        private void DisplayVoteMenu(CCSPlayerController player, IEnumerable<string> voteOptions, int durationSeconds, bool isRtv, bool allowRevote)
        {
            if (_plugin == null || !player.IsValid)
                return;

            var title = _localizer.Localize("emv.hud.menu-title");
            var menuType = ConfigValue.Resolve(MenuManager.MenuTypesList, _endMapConfig.MenuType, MenuTypeManager.GetDefaultMenu());

            var menu = MenuManager.MenuByType(menuType, title, _plugin);
            if (menu is ChatMenu)
                menu.ExitButton = false;

            foreach (var option in voteOptions)
            {
                var chosen = option;
                menu.AddItem(chosen, (p, _) =>
                {
                    MapVoted(p, chosen, isRtv, allowRevote);
                });
            }

            menu.Display(player, durationSeconds);
        }

        public void MapVoted(CCSPlayerController player, string mapName, bool isRtv, bool allowRevote = false)
        {
            if (!player.IsValid)
                return;

            if (!_pluginState.EofVoteHappening || Timer is null)
                return;

            if (!Votes.ContainsKey(mapName))
                return;

            var slot = player.Slot;
            bool canRevote = _endMapConfig.EnableRevote && allowRevote;

            if (_playerVotes.TryGetValue(slot, out var previousMap))
            {
                if (!canRevote)
                    return;

                if (string.Equals(previousMap, mapName, StringComparison.Ordinal))
                    return;

                if (Votes.TryGetValue(previousMap, out int previousVotes) && previousVotes > 0)
                {
                    Votes[previousMap] = previousVotes - 1;
                }
            }
            else
            {
                VotedPlayers.Add(slot);
            }

            _playerVotes[slot] = mapName;
            Votes[mapName] += 1;
            RebuildSortedTopVotes();

            // The click-voting panorama panel gives its own feedback (voted-row highlight,
            // live counters), so skip the chat confirmation there. The chat-number fallback
            // still gets it.
            if (!(_activeVoteUsesPanorama && _customHud.ClickVotingActive))
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("emv.you-voted", mapName));
                if (_endMapConfig.EnableRevote)
                    player.PrintToChat(_localizer.LocalizeWithPrefix("emv.revote"));
            }

            if (_activeVoteUsesPanorama)
            {
                _customHud.UpdateCounts();

                // Persistent highlight on the chosen row while the panel stays open
                int votedRow = _currentVoteOptions.IndexOf(mapName) + 1;
                if (votedRow > 0)
                    _customHud.SetVotedRow(player, votedRow);

                if (_generalConfig.HideHudAfterVote)
                    _customHud.SetVisibleForPlayer(player, false);
            }

            // Keep the vote open for the full timer when revotes are enabled.
            if (!_endMapConfig.EnableRevote && Votes.Values.Sum() >= _canVote)
                EndVote(isRtv);
        }

        public void PlayerDisconnected(CCSPlayerController? player)
        {
            if (player == null)
                return;

            int slot = player.Slot;
            _revoteMenuOpen.Remove(slot);

            if (!_playerVotes.TryGetValue(slot, out var votedMap))
                return;

            _playerVotes.Remove(slot);
            VotedPlayers.Remove(slot);

            if (Votes.TryGetValue(votedMap, out int count) && count > 0)
                Votes[votedMap] = count - 1;

            RebuildSortedTopVotes();

            if (_activeVoteUsesPanorama)
            {
                _customHud.UpdateCounts();
                _customHud.ClearVotedRow(player);
            }
        }

        public void KillTimer()
        {
            TimeLeft = -1;
            KillChatMapChoiceTimer();
            KillHintEntities();
            if (Timer is not null)
            {
                Timer!.Kill();
                Timer = null;
            }
        }

        public static IList<T> Shuffle<T>(Random rng, IList<T> array)
        {
            int n = array.Count;
            while (n > 1)
            {
                int k = rng.Next(n--);
                (array[k], array[n]) = (array[n], array[k]);
            }
            return array;
        }

        public void PrintCenterTextAll(string text)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.IsValid)
                {
                    player.PrintToCenter(text);
                }
            }
        }

        public void ChatCountdown(int secondsLeft)
        {
            if (!_pluginState.EofVoteHappening || !_endMapConfig.EnableCountdown || !ConfigValue.Is(_endMapConfig.CountdownType, "chat"))
                return;

            string text = _localizer.LocalizeWithPrefix("general.chat-countdown", secondsLeft);
            foreach (var player in ServerManager.ValidPlayers())
                player.PrintToChat(text);

            int next = secondsLeft - _endMapConfig.ChatCountdownInterval;
            if (next > 0)
            {
                _plugin?.AddTimer(
                    _endMapConfig.ChatCountdownInterval, () =>
                    {
                        try
                        {
                            ChatCountdown(next);
                        }
                        catch (Exception ex)
                        {
                            _plugin.Logger.LogError($"ChatCountdown timer callback failed: {ex.Message}");
                        }
                    }, TimerFlags.STOP_ON_MAPCHANGE
                );
            }
        }

        private void DisplayGameHintForAll(IEnumerable<CCSPlayerController> targets, float seconds = 5f)
        {
            Server.ExecuteCommand("sv_gameinstructor_disable false");

            string text = _localizer.Localize("emv.vote-started");

            foreach (var player in targets)
            {
                if (player == null || !player.IsValid) continue;

                int slot = player.Slot;
                player.ReplicateConVar("sv_gameinstructor_enable", "true");

                var outerTimer = new Timer(0.25f, () =>
                {
                    try
                    {
                        var live = Utilities.GetPlayerFromSlot(slot);
                        if (live is null || !live.ReallyValid())
                            return;

                        ShowHudInstructorHint(
                            controller: live,
                            text: text,
                            seconds: seconds,
                            iconOnScreen: "",
                            iconOffScreen: "",
                            bindingCmd: "use_binding",
                            color: Color.FromArgb(255, 255, 0, 0)
                        );
                    }
                    catch (Exception ex)
                    {
                        _debugLogger.LogError(ex, "[Hint] Outer timer ShowHudInstructorHint failed. slot={Slot}", slot);
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
                _hintOuterTimers.Add(outerTimer);
            }

            // Capture slots, not raw controllers, so the late ReplicateConVar cleanup re-validates each handle.
            var targetSlots = targets.Where(p => p != null && p.IsValid).Select(p => p.Slot).ToList();
            var cleanupTimer = new Timer(seconds, () =>
            {
                Server.ExecuteCommand("sv_gameinstructor_disable true");
                foreach (var s in targetSlots)
                {
                    try
                    {
                        var live = Utilities.GetPlayerFromSlot(s);
                        if (live is null || !live.ReallyValid())
                            continue;
                        live.ReplicateConVar("sv_gameinstructor_enable", "false");
                    }
                    catch (Exception ex)
                    {
                        _debugLogger.LogError(ex, "[RTV.Hint] Cleanup ReplicateConVar failed. slot={Slot}", s);
                    }
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
            _hintOuterTimers.Add(cleanupTimer);
        }

        private void ShowHudInstructorHint(CCSPlayerController controller, string text, float seconds, string iconOnScreen, string iconOffScreen, string bindingCmd, Color color, float iconHeightOffset = 0f)
        {
            if (!controller.IsValid)
                return;

            var pawn = controller.PlayerPawn?.Value;
            if (pawn is null || !pawn.IsValid)
                return;

            var hint = Utilities.CreateEntityByName<CEnvInstructorHint>("env_instructor_hint");
            if (hint is null) return;

            hint.Static = true;

            hint.Caption = text;
            hint.Timeout = (int)MathF.Max(0, seconds);
            hint.Icon_Onscreen = iconOnScreen;
            hint.Icon_Offscreen = iconOffScreen;
            hint.Binding = bindingCmd;
            hint.Color = color;

            hint.IconOffset = iconHeightOffset;
            hint.Range = 0f;
            hint.NoOffscreen = false;
            hint.ForceCaption = false;

            hint.DispatchSpawn();
            _hintEntities.Add(hint);

            try
            {
                hint.AcceptInput("ShowHint", pawn, pawn);
            }
            catch (Exception ex)
            {
                _debugLogger.LogError(ex, "[RTV.Hint] AcceptInput ShowHint failed");
                return;
            }

            if (seconds > 0)
                RemoveEntity(hint, seconds + 0.25f);

            int controllerSlot = controller.Slot;
            var convarResetTimer = _plugin?.AddTimer(5f, () =>
            {
                try
                {
                    Server.ExecuteCommand("sv_gameinstructor_disable true");
                    var live = Utilities.GetPlayerFromSlot(controllerSlot);
                    if (live is not null && live.ReallyValid())
                        live.ReplicateConVar("sv_gameinstructor_enable", "false");
                }
                catch (Exception ex)
                {
                    _debugLogger.LogError(ex, "[RTV.Hint] Cleanup timer failed. slot={Slot}", controllerSlot);
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
            if (convarResetTimer != null)
                _hintOuterTimers.Add(convarResetTimer);
        }

        private void RemoveEntity(CEnvInstructorHint entity, float time = 0.0f)
        {
            if (time == 0.0f)
            {
                if (entity.IsValid)
                {
                    entity.AcceptInput("Kill");
                }
            }
            else if (time > 0.0f)
            {
                var removeTimer = new Timer(time, () =>
                {
                    try
                    {
                        if (entity.IsValid)
                        {
                            entity.AcceptInput("Kill");
                            _hintEntities.Remove(entity);
                        }
                    }
                    catch (Exception ex)
                    {
                        _debugLogger.LogError(ex, "[RTV.Hint] RemoveEntity timer failed");
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
                _hintOuterTimers.Add(removeTimer);
            }
        }

        public void StartVote(bool isRtv)
        {
            KillNextVoteTimer();
            if (_pluginState.EofVoteHappening)
                return;

            VotedPlayers.Clear();
            _playerVotes.Clear();
            _revoteMenuOpen.Clear();
            _currentVoteOptions.Clear();
            _activeVoteIsRtv = isRtv;

            if (_rtvConfig.EnablePanoramaVote)
            {
                Server.ExecuteCommand("sv_allow_votes 0");
                Server.ExecuteCommand("sv_vote_allow_in_warmup 0");
                Server.ExecuteCommand("sv_vote_allow_spectators 0");
                Server.ExecuteCommand("sv_vote_count_spectator_votes 0");
            }

            Votes.Clear();
            _pluginState.EofVoteHappening = true;

            int maxExt = _generalConfig.MaxMapExtensions;
            bool unlimited = maxExt <= 0;  // treat 0 or negative as unlimited

            bool extensionsDisabledForMap = _generalConfig.DisabledExtensionMaps
                .Contains(Server.MapName, StringComparer.OrdinalIgnoreCase);

            bool canShowExtendOption = !isRtv
                && _endMapConfig.IncludeExtendCurrentMap
                && !extensionsDisabledForMap
                && (unlimited || _pluginState.MapExtensionCount < maxExt);

            int mapsToShow = !isRtv
                ? (_endMapConfig.MapsToShow == 0 ? MaxOptionsHud : _endMapConfig.MapsToShow)
                : (_rtvConfig.MapsToShow == 0 ? MaxOptionsHud : _rtvConfig.MapsToShow);

            // Cap for CenterHtmlMenu (HUD) pages
            if (ConfigValue.Is(_endMapConfig.MenuType, "CenterHtmlMenu")
                && mapsToShow > MaxOptionsHud)
            {
                mapsToShow = MaxOptionsHud;
            }

            // The panorama layout has a fixed number of rows
            if (IsPanoramaMode() && mapsToShow > CustomHud.MaxRows)
                mapsToShow = CustomHud.MaxRows;

            int mapOptionsCount = canShowExtendOption ? mapsToShow - 1 : mapsToShow;

            // Get map list
            var mapsScrambled = Shuffle(new Random(), _mapLister.Maps!.Select(x => x.Name)
                .Where(x => !_mapCooldown.IsMapInCooldown(x)).ToList());

            var nominationWinners = _nominationManager.NominationWinners()
                .Where(x => !_mapCooldown.IsMapInCooldown(x));

            mapsElected = [.. nominationWinners.Concat(mapsScrambled).Distinct()];

            // Create vote list
            List<string> voteOptions = new();
            foreach (var map in mapsElected.Take(mapOptionsCount))
            {
                Votes[map] = 0;
                voteOptions.Add(map);
            }

            if (canShowExtendOption)
            {
                string extendOption = _localizer.Localize("extendtime.list-name");
                Votes[extendOption] = 0;
                voteOptions.Add(extendOption);
            }

            _currentVoteOptions.AddRange(voteOptions);
            RebuildSortedTopVotes();
            _canVote = ServerManager.ValidPlayerCount();
            int voteDuration = isRtv ? _rtvConfig.MapVoteDuration : _endMapConfig.VoteDuration;
            TimeLeft = voteDuration;

            _activeVoteUsesPanorama = false;
            if (IsPanoramaMode())
            {
                _activeVoteUsesPanorama = _customHud.Show(
                    _localizer.Localize("emv.hud.menu-title"),
                    _currentVoteOptions,
                    option => Votes.TryGetValue(option, out int count) ? count : 0,
                    clickVoting: _panoramaConfig.EnableClickVoting);

                if (_activeVoteUsesPanorama)
                    _customHud.UpdateTimer(voteDuration);
                else
                    _logger.LogWarning("[RTV.EndMapVote] MenuType 'panorama' unavailable; falling back to the default menu display.");
            }

            var players = ServerManager.ValidPlayers()
                .Where(p => p != null && p.IsValid)
                .ToList();

            foreach (var snapshot in players)
            {
                int slot = snapshot.Slot;
                try
                {
                    var live = Utilities.GetPlayerFromSlot(slot);
                    if (live is null || !live.ReallyValid())
                        continue;

                    if (!_activeVoteUsesPanorama)
                        DisplayVoteMenu(live, _currentVoteOptions, voteDuration, isRtv, allowRevote: false);

                    if (_endMapConfig.SoundEnabled)
                    {
                        SoundEventHelper.PlaySound(live, _endMapConfig.SoundPath, _endMapConfig.SoundVolume);
                    }
                }
                catch (Exception ex)
                {
                    _debugLogger.LogError(ex, "[RTV.EndMapVote] StartVote per-player setup failed. slot={Slot}", slot);
                }
            }

            if (!ConfigValue.Is(_endMapConfig.MenuType, "ChatMenu"))
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-started"));

            // Chat instructions only when the panorama vote fell back to chat-number input
            if (PanoramaChatFallbackActive())
                PrintChatMapChoices();

            if (_endMapConfig.EnableHint)
            {
                var hintType = string.IsNullOrWhiteSpace(_endMapConfig.HintType)
                    ? "GameHint"
                    : _endMapConfig.HintType.Trim();
                if (ConfigValue.Is(hintType, "csay"))
                {
                    var message = _localizer.Localize("emv.vote-started").Replace("\"", "'");
                    Server.ExecuteCommand($"css_csay {message}");
                }
                else
                {
                    DisplayGameHintForAll(players, seconds: 5f);
                }
            }

            StartChatMapChoiceReminder();
            ChatCountdown(voteDuration);

            float voteDeadline = Server.CurrentTime + voteDuration;
            Timer = _plugin?.AddTimer(1.0F, () =>
            {
                TimeLeft = (int)Math.Ceiling(Math.Max(0.0, voteDeadline - Server.CurrentTime));

                if (_activeVoteUsesPanorama)
                    _customHud.UpdateTimer(TimeLeft);

                if (TimeLeft <= 0)
                {
                    _debugLogger.LogInformation("[RTV.EndMapVote] Vote-tick timer reached deadline. Deferring EndVote to next frame. isRtv={IsRtv}", isRtv);
                    
                    Server.NextFrame(() => EndVote(isRtv));
                }
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void CloseAllActiveMenus()
        {
            foreach (var snapshot in ServerManager.ValidPlayers())
            {
                int slot = snapshot.Slot;
                try
                {
                    var live = Utilities.GetPlayerFromSlot(slot);
                    if (live is null || !live.ReallyValid())
                        continue;

                    MenuManager.CloseActiveMenu(live);
                }
                catch (Exception ex)
                {
                    _debugLogger.LogError(ex, "[RTV.EndMapVote] CloseActiveMenu failed. slot={Slot}", slot);
                }
            }
        }

        public void EndVote(bool isRtv)
        {
            CloseAllActiveMenus();

            // Panorama: keep the panel up briefly to flash + highlight the winning row.
            // The snapshot maps the winner back to its row after the list is cleared.
            bool showPanoramaWinner = _activeVoteUsesPanorama && _customHud.IsActive;
            var panoramaOptions = showPanoramaWinner ? _currentVoteOptions.ToList() : null;
            if (!showPanoramaWinner)
                _customHud.Destroy();
            _activeVoteUsesPanorama = false;

            KillTimer();
            _currentVoteOptions.Clear();
            _revoteMenuOpen.Clear();

            bool ignoreRoundWinConditions =
                ConVar.Find("mp_ignore_round_win_conditions")?.GetPrimitiveValue<bool>() == true;

            MapChangeTrigger trigger;
            if (isRtv)
                trigger = MapChangeTrigger.RoundStart;
            else if (_endMapConfig.ChangeMapImmediately)
                trigger = MapChangeTrigger.IgnoredWinConditions;
            else if (ignoreRoundWinConditions)
                trigger = MapChangeTrigger.IgnoredWinConditions;
            else
                trigger = MapChangeTrigger.MatchEnd;
            string extendOption = _localizer.Localize("extendtime.list-name");

            decimal totalVotes = Votes.Select(x => x.Value).Sum();
            KeyValuePair<string, int> winner;
            Random rnd = new();

            if (totalVotes == 0)
            {
                // No votes cast, pick a random map(not the extend).
                var candidateMaps = Votes.Keys.Where(x => x != extendOption).ToList();
                if (candidateMaps.Count == 0)
                    candidateMaps = Votes.Keys.ToList();
                string chosen = candidateMaps[rnd.Next(candidateMaps.Count)];
                winner = new KeyValuePair<string, int>(chosen, 0);
            }
            else
            {
                int maxVotes = Votes.Values.Max();
                var tiedMaps = Votes.Where(kv => kv.Value == maxVotes).Select(kv => kv.Key).ToList();
                string chosenKey = tiedMaps[rnd.Next(tiedMaps.Count)];
                winner = new KeyValuePair<string,int>(chosenKey, maxVotes);
            }

            decimal percent = totalVotes > 0 ? winner.Value / totalVotes * 100M : 0;

            _debugLogger.LogInformation(
                "[RTV.EndMapVote] Vote ended. isRtv={IsRtv} winner={Winner} winnerVotes={WinnerVotes} totalVotes={TotalVotes} percent={Percent} trigger={Trigger} changeImmediately={ChangeImmediately} currentMap={CurrentMap}",
                isRtv,
                winner.Key,
                winner.Value,
                totalVotes,
                percent,
                trigger,
                !isRtv ? _endMapConfig.ChangeMapImmediately : !_rtvConfig.ChangeAtRoundEnd,
                Server.MapName
            );

            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ended", winner.Key, percent, totalVotes));

            if (showPanoramaWinner)
            {
                int winnerRow = panoramaOptions!.IndexOf(winner.Key) + 1;
                _customHud.ShowWinner(winnerRow);
                _plugin?.AddTimer(6.0f, _customHud.Destroy, TimerFlags.STOP_ON_MAPCHANGE);
            }

            if (winner.Key == extendOption)
            {
                _nominationManager.MarkMapExtended();

                int maxExt = _generalConfig.MaxMapExtensions;
                bool unlimited = maxExt <= 0;

                if (unlimited || _pluginState.MapExtensionCount < maxExt)
                {
                    bool success = _extendRoundTimeManager.ExtendRoundTime(_generalConfig.RoundTimeExtension);
                    if (success)
                    {
                        _debugLogger.LogInformation(
                            "[RTV.EndMapVote] Extend won and applied. minutes={Minutes} mapExtensionsUsed={ExtensionsUsed}",
                            _generalConfig.RoundTimeExtension,
                            _pluginState.MapExtensionCount + 1
                        );
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.passed", _generalConfig.RoundTimeExtension, percent, totalVotes));
                        _pluginState.MapExtensionCount++;
                    }
                    else
                    {
                        _debugLogger.LogWarning("[RTV.EndMapVote] Extend won but apply failed. minutes={Minutes}", _generalConfig.RoundTimeExtension);
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.failed", percent, totalVotes));
                    }
                }

                ScheduleNextVote();
            }
            else
            {
                _changeMapManager.ScheduleMapChange(winner.Key, trigger: trigger);

                if (!isRtv)
                {
                    if (_endMapConfig.ChangeMapImmediately)
                    {
                        _debugLogger.LogInformation("[RTV.EndMapVote] Changing map immediately from end-of-map vote. winner={Winner}", winner.Key);
                        _changeMapManager.ChangeNextMap();
                    }
                    else if (ignoreRoundWinConditions)
                    {
                        StartIgnoreWinConditionsPoll(winner.Key, MapChangeTrigger.IgnoredWinConditions);
                    }
                    else
                    {
                        // MatchEnd normally fires via EventCsWinPanelMatch, but that event is not
                        // guaranteed on an empty server, so the fallback poll as a safety net.
                        _debugLogger.LogInformation(
                            "[RTV.EndMapVote] Arming MatchEnd fallback poll in case EventCsWinPanelMatch never fires. winner={Winner}",
                            winner.Key
                        );
                        StartIgnoreWinConditionsPoll(winner.Key, MapChangeTrigger.MatchEnd);
                    }
                }
                else
                {
                    if (!_rtvConfig.ChangeAtRoundEnd)
                    {
                        var delay = _rtvConfig.MapChangeDelay;
                        if (delay <= 0) // Immediate
                        {
                            _debugLogger.LogInformation("[RTV.EndMapVote] RTV map vote changing immediately. winner={Winner}", winner.Key);
                            _changeMapManager.ChangeNextMap();
                        }
                        else // Timer for MapChangeDelay seconds
                        {
                            _debugLogger.LogInformation(
                                "[RTV.EndMapVote] RTV map vote armed delayed map change. winner={Winner} delaySeconds={DelaySeconds}",
                                winner.Key,
                                delay
                            );
                            _plugin?.AddTimer(delay, () =>
                            {
                                _changeMapManager.ChangeNextMap();
                            }, TimerFlags.STOP_ON_MAPCHANGE);
                        }
                    }
                    else
                    {
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("general.changing-map-next-round", winner.Key));
                    }
                }
            }

            _pluginState.EofVoteHappening = false;
        }
    }
}
