using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace cs2_rockthevote.Core
{
    /// Renders the end-of-map vote on a custom_hud_layout Panorama panel shipped in a workshop addon.
    /// Players vote by clicking the panel buttons.
    public class CustomHud : IPluginDependency<Plugin, Config>
    {
        public const int MaxRows = 10;

        private const string DialogPanel = "rtv_vote_dialog";
        private const string HeaderPanel = "rtv_header";
        private const string ClosePanel = "rtv_vote_close";
        private const string TimerPanel = "rtv_timer";
        private const string RowPanelPrefix = "rtv_vote_";
        private const string RowLabelPrefix = "rtv_label_";
        private const string RowCountPrefix = "rtv_count_";
        private const string RowHiddenClass = "rtv-row-hidden";
        private const string RowVotedClass = "rtv-row-voted";
        private const string RowWinnerClass = "rtv-row-winner";
        private const string DialogHiddenClass = "rtv-dialog-hidden";

        internal static readonly string[] TextColors = ["white", "green", "cyan", "yellow", "orange", "red", "magenta", "blue"];

        // Row width steps baked into the addon stylesheet (.rtv-vote-row.rtv-w-*); the server
        // picks one per vote from the longest option label.
        private static readonly int[] WidthSteps = [240, 280, 320, 360, 400, 440, 480, 520, 560, 600, 640];

        // Position variants baked into the addon stylesheet (#rtv_vote_dialog.rtv-pos-*). Default (no class) = CenterRight.
        internal static readonly Dictionary<string, string> PositionClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CenterTop"] = "rtv-pos-center-top",
            ["CenterMiddle"] = "rtv-pos-center-middle",
            ["CenterBottom"] = "rtv-pos-center-bottom",
            ["TopLeftCorner"] = "rtv-pos-top-left",
            ["TopRightCorner"] = "rtv-pos-top-right",
            ["BottomLeftCorner"] = "rtv-pos-bottom-left",
            ["BottomRightCorner"] = "rtv-pos-bottom-right",
            ["CenterLeft"] = "rtv-pos-center-left",
            ["CenterRight"] = "rtv-pos-center-right",
        };

        private readonly ILogger<CustomHud> _logger;
        private ILogger _debugLogger = NullLogger.Instance;
        private PanoramaMenuConfig _panoramaConfig = new();
        private CCSCustomHudLayout? _hud;
        private readonly List<string> _options = new();
        private Func<string, int>? _getVotes;
        private bool _clickVoting;
        private string _timerText = "";
        private readonly Dictionary<int, int> _votedRows = new();

        public CustomHud(ILogger<CustomHud> logger)
        {
            _logger = logger;
        }

        public void OnLoad(Plugin plugin)
        {
            plugin.RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        }

        public void OnConfigParsed(Config config)
        {
            _panoramaConfig = config.PanoramaMenu;
            _warnedBadPosition = false;
            _debugLogger = DebugLog.For(_logger, config);
        }

        public void OnMapStart(string mapName)
        {
            Destroy();
        }

        public void Unload(Plugin plugin)
        {
            Destroy();
        }

        public bool IsActive => _hud is { IsValid: true };

        // True while the current panel captures the cursor for click voting
        public bool ClickVotingActive => _clickVoting && IsActive;

        // True when a clicked layout entity is the live panel
        public bool MatchesLayout(nint layoutPointer)
        {
            return layoutPointer != 0 && _hud is { IsValid: true } hud && hud.Handle == layoutPointer;
        }

        // Creates+spawns a custom_hud_layout entity using the addon layout.
        // Shared by the vote panel and the RTV toast.
        internal static CCSCustomHudLayout? SpawnLayout(string layoutPath, string targetname, ILogger logger)
        {
            var hud = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
            if (hud == null || !hud.IsValid)
            {
                logger.LogError("[RTV.CustomHud] Failed to create custom_hud_layout entity.");
                return null;
            }

            using var kv = new CEntityKeyValues();
            kv.SetString("layout", layoutPath);
            kv.SetString("targetname", targetname);
            hud.DispatchSpawn(kv);
            return hud;
        }

        // Spawns the vote panel and fills it for every connected player
        public bool Show(string title, IReadOnlyList<string> options, Func<string, int> getVotes, bool clickVoting = false)
        {
            Destroy();

            string layout = _panoramaConfig.AddonName?.Trim() ?? "";
            if (layout.Length == 0)
            {
                _logger.LogWarning("[RTV.CustomHud] PanoramaMenu.AddonName is empty; no panel will be shown.");
                return false;
            }

            _options.Clear();
            _options.AddRange(options.Take(MaxRows));
            _getVotes = getVotes;
            _clickVoting = clickVoting;
            _rowWidthClass = ComputeRowWidthClass();

            try
            {
                var hud = SpawnLayout(layout, "rtv_panorama_hud", _logger);
                if (hud == null)
                {
                    Destroy();
                    return false;
                }

                _hud = hud;

                ApplyGlobalContent(title);

                if (_clickVoting)
                {
                    foreach (var player in ServerManager.ValidPlayers())
                        hud.SetInputCaptureEnabled(player, true);
                }

                _debugLogger.LogInformation("[RTV.CustomHud] Spawned custom_hud_layout #{Index} with layout '{Layout}' and {Count} options.",
                    hud.Index, layout, _options.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.CustomHud] Failed to populate/spawn the vote panel.");
                Destroy();
                return false;
            }
        }

        // Highlights the row this player voted for (persistent "selected" style, distinct
        // from hover). Moving the vote moves the highlight.
        public void SetVotedRow(CCSPlayerController player, int row)
        {
            if (_hud is not { IsValid: true } hud || !player.IsValid || row < 1 || row > MaxRows)
                return;

            try
            {
                if (_votedRows.TryGetValue(player.Slot, out int previous) && previous != row)
                    hud.SetHasClassForPlayer(player, $"{RowPanelPrefix}{previous}", RowVotedClass, false);

                _votedRows[player.Slot] = row;
                hud.SetHasClassForPlayer(player, $"{RowPanelPrefix}{row}", RowVotedClass, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.CustomHud] Failed to highlight voted row {Row} for slot {Slot}.", row, player.Slot);
            }
        }

        // Removes a player's voted-row highlight (e.g. on disconnect)
        public void ClearVotedRow(CCSPlayerController player)
        {
            if (!_votedRows.Remove(player.Slot, out int row))
                return;

            if (_hud is not { IsValid: true } hud || !player.IsValid)
                return;

            try
            {
                hud.SetHasClassForPlayer(player, $"{RowPanelPrefix}{row}", RowVotedClass, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.CustomHud] Failed to clear voted row for slot {Slot}.", player.Slot);
            }
        }

        // Marks the winning row after the vote ends. The addon's rtv-row-winner class flashes
        // for ~5 seconds and then settles on a solid color. Re-shows the panel for everyone
        // (players who already voted may have it hidden), releases all cursors, and hides the
        // now-dead Close button. Caller destroys the panel after the display window.
        public void ShowWinner(int row)
        {
            if (_hud is not { IsValid: true } hud || row < 1 || row > MaxRows)
                return;

            _clickVoting = false; // the vote is over; nothing on the panel is clickable
            _timerText = "";      // countdown is done; clear it like the Close button

            try
            {
                hud.SetHasClass(ClosePanel, RowHiddenClass, true);
                hud.SetHasClass($"{RowPanelPrefix}{row}", RowWinnerClass, true);
                hud.SetDialogVariableString(TimerPanel, TimerPanel, "");

                foreach (var player in ServerManager.ValidPlayers())
                {
                    hud.SetHasClassForPlayer(player, DialogPanel, DialogHiddenClass, false);
                    hud.SetInputCaptureEnabled(player, false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.CustomHud] Failed to show the winning row {Row}.", row);
            }
        }

        // Updates the footer countdown (call once per second with seconds left)
        public void UpdateTimer(int secondsLeft)
        {
            if (_hud is not { IsValid: true } hud)
                return;

            // Empty at zero so the label disappears
            _timerText = secondsLeft > 0 ? secondsLeft.ToString() : "";

            try
            {
                hud.SetDialogVariableString(TimerPanel, TimerPanel, _timerText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.CustomHud] Failed to update the countdown timer.");
            }
        }

        // Rewrites the per-row vote counters (call on every vote change)
        public void UpdateCounts()
        {
            if (_hud is not { IsValid: true } hud || _getVotes == null)
                return;

            try
            {
                for (int i = 0; i < _options.Count; i++)
                {
                    string value = _getVotes(_options[i]).ToString();
                    hud.SetDialogVariableString($"{RowCountPrefix}{i + 1}", $"{RowCountPrefix}{i + 1}", value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.CustomHud] Failed to update vote counters.");
            }
        }

        public void SetVisibleForPlayer(CCSPlayerController player, bool visible)
        {
            if (_hud is not { IsValid: true } hud || !player.IsValid)
                return;

            try
            {
                hud.SetHasClassForPlayer(player, DialogPanel, DialogHiddenClass, !visible);

                // Hiding the panel alone would leave the player with a captured cursor
                if (_clickVoting)
                    hud.SetInputCaptureEnabled(player, visible);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.CustomHud] Failed to toggle panel visibility for slot {Slot}.", player.Slot);
            }
        }

        public void Destroy()
        {
            var hud = _hud;
            _hud = null;
            _options.Clear();
            _getVotes = null;
            _timerText = "";
            _votedRows.Clear();
            bool clickVoting = _clickVoting;
            _clickVoting = false;

            if (hud is { IsValid: true })
            {
                try
                {
                    // Release every cursor before the entity disappears
                    if (clickVoting)
                    {
                        foreach (var player in ServerManager.ValidPlayers())
                            hud.SetInputCaptureEnabled(player, false);
                    }

                    hud.AcceptInput("Kill");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RTV.CustomHud] Failed to kill the vote panel entity.");
                }
            }
        }

        private void ApplyGlobalContent(string title)
        {
            if (_hud is not { IsValid: true } hud)
                return;

            hud.SetDialogVariableString(HeaderPanel, HeaderPanel, title);
            hud.SetDialogVariableString(TimerPanel, TimerPanel, _timerText);
            ApplyMapVoteHeaderSize(hud);
            ApplyMapVoteMenuPosition(hud);

            // Without click support the Close button is dead UI. With it, it lets the
            // player dismiss the panel (and free their cursor) without voting.
            hud.SetHasClass(ClosePanel, RowHiddenClass, !_clickVoting);

            for (int i = 0; i < MaxRows; i++)
            {
                int row = i + 1;
                bool hasOption = i < _options.Count;
                string value = hasOption ? $"{row}. {_options[i]}" : "";
                string count = hasOption && _getVotes != null ? _getVotes(_options[i]).ToString() : "";

                // Written against the dialog, the row button, and the label. The client's
                // dialog-variable scope resolution is not pinned down.
                hud.SetDialogVariableString(DialogPanel, $"rtv_option_{row}", value);
                hud.SetDialogVariableString($"{RowPanelPrefix}{row}", $"rtv_option_{row}", value);
                hud.SetDialogVariableString($"{RowLabelPrefix}{row}", $"rtv_option_{row}", value);
                hud.SetDialogVariableString($"{RowCountPrefix}{row}", $"{RowCountPrefix}{row}", count);
                hud.SetHasClass($"{RowPanelPrefix}{row}", RowHiddenClass, !hasOption);
                ApplyRowStyle(hud, row);
            }
        }

        private string _rowWidthClass = "";

        // Estimates the pixel width needed for the longest "N. mapname" label
        // and adjusts it to the addon's width steps.
        private string ComputeRowWidthClass()
        {
            int maxLength = 0;
            for (int i = 0; i < _options.Count; i++)
                maxLength = Math.Max(maxLength, $"{i + 1}. {_options[i]}".Length);

            int fontSize = _panoramaConfig.MapVoteRowSize?.Trim().ToLowerInvariant() switch
            {
                "compact" => 15,
                "large" => 22,
                "extralarge" => 26,
                _ => 18,
            };

            // 0.46em average character advance measured against Stratum2 rendering
            // (0.55 overshot the width by ~15%)
            int estimated = (int)(maxLength * fontSize * 0.46) + 46 + 20;
            foreach (int step in WidthSteps)
            {
                if (estimated <= step)
                    return $"rtv-w-{step}";
            }

            return $"rtv-w-{WidthSteps[^1]}";
        }

        private void ApplyRowStyle(CCSCustomHudLayout hud, int row)
        {
            string size = _panoramaConfig.MapVoteRowSize?.Trim().ToLowerInvariant() ?? "normal";
            bool compact = size == "compact";
            bool large = size == "large";
            bool extraLarge = size == "extralarge";

            hud.SetHasClass($"{RowPanelPrefix}{row}", "rtv-row-compact", compact);
            hud.SetHasClass($"{RowLabelPrefix}{row}", "rtv-label-compact", compact);
            hud.SetHasClass($"{RowPanelPrefix}{row}", "rtv-row-large", large);
            hud.SetHasClass($"{RowLabelPrefix}{row}", "rtv-label-large", large);
            hud.SetHasClass($"{RowPanelPrefix}{row}", "rtv-row-xlarge", extraLarge);
            hud.SetHasClass($"{RowLabelPrefix}{row}", "rtv-label-xlarge", extraLarge);

            if (_rowWidthClass.Length > 0)
                hud.SetHasClass($"{RowPanelPrefix}{row}", _rowWidthClass, true);

            string color = _panoramaConfig.MapVoteRowColor?.Trim().ToLowerInvariant() ?? "green";
            if (TextColors.Contains(color))
                hud.SetHasClass($"{RowLabelPrefix}{row}", $"rtv-color-{color}", true);
        }

        private bool _warnedBadPosition;

        private void ApplyMapVoteMenuPosition(CCSCustomHudLayout hud)
        {
            string position = _panoramaConfig.MapVoteMenuPosition?.Trim() ?? "";
            if (position.Length == 0)
                return;

            if (PositionClasses.TryGetValue(position, out var positionClass))
            {
                hud.SetHasClass(DialogPanel, positionClass, true);
            }
            else if (!_warnedBadPosition)
            {
                _warnedBadPosition = true;
                _logger.LogWarning("[RTV.CustomHud] Unknown PanoramaMenu.MapVoteMenuPosition '{Position}'; using the layout default. Valid: {Valid}",
                    position, string.Join(", ", PositionClasses.Keys));
            }
        }

        private void ApplyMapVoteHeaderSize(CCSCustomHudLayout hud)
        {
            string size = _panoramaConfig.MapVoteHeaderSize?.Trim().ToLowerInvariant() ?? "normal";
            hud.SetHasClass(HeaderPanel, "rtv-header-compact", size == "compact");
            hud.SetHasClass(HeaderPanel, "rtv-header-large", size == "large");
            hud.SetHasClass(HeaderPanel, "rtv-header-xlarge", size == "extralarge");

            // "default" (or anything not in the palette) keeps the layout's gold header
            string color = _panoramaConfig.MapVoteHeaderColor?.Trim().ToLowerInvariant() ?? "default";
            if (TextColors.Contains(color))
                hud.SetHasClass(HeaderPanel, $"rtv-color-{color}", true);
        }

        private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player == null || !player.ReallyValid())
                return HookResult.Continue;

            if (_hud is { IsValid: true } hud)
            {
                try
                {
                    hud.SetHasClassForPlayer(player, DialogPanel, DialogHiddenClass, false);

                    if (_votedRows.Remove(player.Slot, out int staleRow))
                        hud.SetHasClassForPlayer(player, $"{RowPanelPrefix}{staleRow}", RowVotedClass, false);

                    if (_clickVoting)
                        hud.SetInputCaptureEnabled(player, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RTV.CustomHud] Failed to apply panel state for slot {Slot}.", player.Slot);
                }
            }

            return HookResult.Continue;
        }
    }
}
