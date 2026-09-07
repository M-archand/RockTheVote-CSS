using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote.Core
{
    /// Renders the end-of-map vote on a custom_hud_layout Panorama panel shipped in a workshop addon.
    /// Players vote by clicking the panel (CustomHudClickListener)
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
        private PanoramaMenuConfig _panoramaConfig = new();
        private CustomHudLayout? _hud;
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
            CustomHudGameData.Load(plugin.ModuleDirectory, _logger);
            plugin.RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        }

        public void OnConfigParsed(Config config)
        {
            _panoramaConfig = config.PanoramaMenu;
            _warnedBadPosition = false;
        }

        public void OnMapStart(string mapName)
        {
            Destroy();
        }

        public void Unload(Plugin plugin)
        {
            Destroy();
        }

        public bool Available => CustomHudLayout.IsSupported(_logger);

        public bool IsActive => _hud is { IsValid: true };

        // True while the current panel captures the cursor for click voting
        public bool ClickVotingActive => _clickVoting && IsActive;

        // True when a click receiver's layout pointer refers to the live panel
        public bool MatchesLayout(nint layoutPointer)
        {
            return layoutPointer != 0 && _hud is { IsValid: true } hud && hud.EntityPointer == layoutPointer;
        }

        // Spawns the vote panel and fills it for every connected player
        public bool Show(string title, IReadOnlyList<string> options, Func<string, int> getVotes, bool clickVoting = false)
        {
            Destroy();

            if (!Available)
            {
                _logger.LogWarning("[RTV.CustomHud] custom_hud_layout unsupported: {Reason}", CustomHudLayout.UnavailableReason);
                return false;
            }

            string layout = _panoramaConfig.AddonName?.Trim() ?? "";
            if (layout.Length == 0)
            {
                _logger.LogWarning("[RTV.CustomHud] PanoramaMenu.AddonName is empty; no panel will be shown.");
                return false;
            }

            var hud = CustomHudLayout.Create(_logger);
            if (hud == null)
                return false;

            _hud = hud;
            _options.Clear();
            _options.AddRange(options.Take(MaxRows));
            _getVotes = getVotes;
            _clickVoting = clickVoting;
            _rowWidthClass = ComputeRowWidthClass();

            try
            {
                // Write the state before DispatchSpawn so the spawn baseline carries it,
                // and once after in case the per-player rows only exist post-spawn.
                foreach (var player in ServerManager.ValidPlayers())
                    ApplyContent(title, player.Slot);

                hud.Spawn(layout);

                foreach (var player in ServerManager.ValidPlayers())
                    ApplyContent(title, player.Slot);

                _logger.LogInformation("[RTV.CustomHud] Spawned custom_hud_layout #{Index} with layout '{Layout}' and {Count} options.",
                    hud.EntityIndex, layout, _options.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.CustomHud] Failed to populate/spawn the vote panel.");
                Destroy();
                return false;
            }
        }

        /// Re-sends the panel content to one player (used for late joiners and revotes)
        public void ApplyForPlayer(int playerSlot)
        {
            if (_hud is not { IsValid: true })
                return;

            try
            {
                ApplyContent(_lastTitle, playerSlot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.CustomHud] Failed to apply panel content for slot {Slot}.", playerSlot);
            }
        }

        // Highlights the row this player voted for (persistent "selected" style, distinct
        // from hover). Moving the vote moves the highlight.
        public void SetVotedRow(int playerSlot, int row)
        {
            if (_hud is not { IsValid: true } hud || row < 1 || row > MaxRows)
                return;

            try
            {
                if (_votedRows.TryGetValue(playerSlot, out int previous) && previous != row)
                    hud.SetHasClass($"{RowPanelPrefix}{previous}", RowVotedClass, false, playerSlot);

                _votedRows[playerSlot] = row;
                hud.SetHasClass($"{RowPanelPrefix}{row}", RowVotedClass, true, playerSlot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.CustomHud] Failed to highlight voted row {Row} for slot {Slot}.", row, playerSlot);
            }
        }

        // Removes a player's voted-row highlight (e.g. on disconnect)
        public void ClearVotedRow(int playerSlot)
        {
            if (!_votedRows.Remove(playerSlot, out int row))
                return;

            if (_hud is not { IsValid: true } hud)
                return;

            try
            {
                hud.SetHasClass($"{RowPanelPrefix}{row}", RowVotedClass, false, playerSlot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.CustomHud] Failed to clear voted row for slot {Slot}.", playerSlot);
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
                foreach (var player in ServerManager.ValidPlayers())
                {
                    hud.SetHasClass(DialogPanel, DialogHiddenClass, false, player.Slot);
                    hud.SetHasClass(ClosePanel, RowHiddenClass, true, player.Slot);
                    hud.SetHasClass($"{RowPanelPrefix}{row}", RowWinnerClass, true, player.Slot);
                    hud.SetDialogVariable(TimerPanel, TimerPanel, "", player.Slot);
                    hud.SetInputCaptureEnabled(false, player.Slot);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.CustomHud] Failed to show the winning row {Row}.", row);
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
                foreach (var player in ServerManager.ValidPlayers())
                    hud.SetDialogVariable(TimerPanel, TimerPanel, _timerText, player.Slot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.CustomHud] Failed to update the countdown timer.");
            }
        }

        // Rewrites the per-row vote counters (call on every vote change)
        public void UpdateCounts()
        {
            if (_hud is not { IsValid: true } hud || _getVotes == null)
                return;

            try
            {
                var players = ServerManager.ValidPlayers();
                for (int i = 0; i < _options.Count; i++)
                {
                    string value = _getVotes(_options[i]).ToString();
                    foreach (var player in players)
                        hud.SetDialogVariable($"{RowCountPrefix}{i + 1}", $"{RowCountPrefix}{i + 1}", value, player.Slot);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.CustomHud] Failed to update vote counters.");
            }
        }

        public void SetVisibleForPlayer(int playerSlot, bool visible)
        {
            if (_hud is not { IsValid: true } hud)
                return;

            try
            {
                hud.SetHasClass(DialogPanel, DialogHiddenClass, !visible, playerSlot);

                // Hiding the panel alone would leave the player with a captured cursor
                if (_clickVoting)
                    hud.SetInputCaptureEnabled(visible, playerSlot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.CustomHud] Failed to toggle panel visibility for slot {Slot}.", playerSlot);
            }
        }

        public void Destroy()
        {
            var hud = _hud;
            _hud = null;
            _options.Clear();
            _getVotes = null;
            _lastTitle = "";
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
                            hud.SetInputCaptureEnabled(false, player.Slot);
                    }

                    hud.Kill();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RTV.CustomHud] Failed to kill the vote panel entity.");
                }
            }
        }

        private string _lastTitle = "";

        private void ApplyContent(string title, int playerSlot)
        {
            if (_hud is not { IsValid: true } hud)
                return;

            _lastTitle = title;

            hud.SetDialogVariable(HeaderPanel, HeaderPanel, title, playerSlot);
            hud.SetDialogVariable(TimerPanel, TimerPanel, _timerText, playerSlot);
            ApplyMapVoteHeaderSize(hud, playerSlot);
            ApplyMapVoteMenuPosition(hud, playerSlot);

            // Without click support the Close button is dead UI. With it, it lets the
            // player dismiss the panel (and free their cursor) without voting.
            hud.SetHasClass(ClosePanel, RowHiddenClass, !_clickVoting, playerSlot);
            hud.SetInputCaptureEnabled(_clickVoting, playerSlot);

            for (int i = 0; i < MaxRows; i++)
            {
                int row = i + 1;
                bool hasOption = i < _options.Count;
                string value = hasOption ? $"{row}. {_options[i]}" : "";
                string count = hasOption && _getVotes != null ? _getVotes(_options[i]).ToString() : "";

                // Written against the dialog, the row button, and the label. The client's
                // dialog-variable scope resolution is not pinned down.
                hud.SetDialogVariable(DialogPanel, $"rtv_option_{row}", value, playerSlot);
                hud.SetDialogVariable($"{RowPanelPrefix}{row}", $"rtv_option_{row}", value, playerSlot);
                hud.SetDialogVariable($"{RowLabelPrefix}{row}", $"rtv_option_{row}", value, playerSlot);
                hud.SetDialogVariable($"{RowCountPrefix}{row}", $"{RowCountPrefix}{row}", count, playerSlot);
                hud.SetHasClass($"{RowPanelPrefix}{row}", RowHiddenClass, !hasOption, playerSlot);
                ApplyRowStyle(hud, row, playerSlot);
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

        private void ApplyRowStyle(CustomHudLayout hud, int row, int playerSlot)
        {
            string size = _panoramaConfig.MapVoteRowSize?.Trim().ToLowerInvariant() ?? "normal";
            bool compact = size == "compact";
            bool large = size == "large";
            bool extraLarge = size == "extralarge";

            hud.SetHasClass($"{RowPanelPrefix}{row}", "rtv-row-compact", compact, playerSlot);
            hud.SetHasClass($"{RowLabelPrefix}{row}", "rtv-label-compact", compact, playerSlot);
            hud.SetHasClass($"{RowPanelPrefix}{row}", "rtv-row-large", large, playerSlot);
            hud.SetHasClass($"{RowLabelPrefix}{row}", "rtv-label-large", large, playerSlot);
            hud.SetHasClass($"{RowPanelPrefix}{row}", "rtv-row-xlarge", extraLarge, playerSlot);
            hud.SetHasClass($"{RowLabelPrefix}{row}", "rtv-label-xlarge", extraLarge, playerSlot);

            if (_rowWidthClass.Length > 0)
                hud.SetHasClass($"{RowPanelPrefix}{row}", _rowWidthClass, true, playerSlot);

            string color = _panoramaConfig.MapVoteRowColor?.Trim().ToLowerInvariant() ?? "green";
            if (TextColors.Contains(color))
                hud.SetHasClass($"{RowLabelPrefix}{row}", $"rtv-color-{color}", true, playerSlot);
        }

        private bool _warnedBadPosition;

        private void ApplyMapVoteMenuPosition(CustomHudLayout hud, int playerSlot)
        {
            string position = _panoramaConfig.MapVoteMenuPosition?.Trim() ?? "";
            if (position.Length == 0)
                return;

            if (PositionClasses.TryGetValue(position, out var positionClass))
            {
                hud.SetHasClass(DialogPanel, positionClass, true, playerSlot);
            }
            else if (!_warnedBadPosition)
            {
                _warnedBadPosition = true;
                _logger.LogWarning("[RTV.CustomHud] Unknown PanoramaMenu.MapVoteMenuPosition '{Position}'; using the layout default. Valid: {Valid}",
                    position, string.Join(", ", PositionClasses.Keys));
            }
        }

        private void ApplyMapVoteHeaderSize(CustomHudLayout hud, int playerSlot)
        {
            string size = _panoramaConfig.MapVoteHeaderSize?.Trim().ToLowerInvariant() ?? "normal";
            hud.SetHasClass(HeaderPanel, "rtv-header-compact", size == "compact", playerSlot);
            hud.SetHasClass(HeaderPanel, "rtv-header-large", size == "large", playerSlot);
            hud.SetHasClass(HeaderPanel, "rtv-header-xlarge", size == "extralarge", playerSlot);

            // "default" (or anything not in the palette) keeps the layout's gold header
            string color = _panoramaConfig.MapVoteHeaderColor?.Trim().ToLowerInvariant() ?? "default";
            if (TextColors.Contains(color))
                hud.SetHasClass(HeaderPanel, $"rtv-color-{color}", true, playerSlot);
        }

        private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player == null || !player.ReallyValid())
                return HookResult.Continue;

            if (IsActive)
                ApplyForPlayer(player.Slot);

            return HookResult.Continue;
        }
    }
}
