using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace cs2_rockthevote.Core
{
    // Small "RTV in-progress" pop-up (no cursor input capture).
    public partial class RtvToast : IPluginDependency<Plugin, Config>
    {
        private const string ToastPanel = "rtv_toast";
        private const string HeaderPanel = "rtv_toast_header";
        private const string DescPanel = "rtv_toast_desc";
        private const string DescNumPanel = "rtv_toast_desc_num";
        private const string InstrPanel = "rtv_toast_instr";
        private const string InstrCmdPanel = "rtv_toast_instr_cmd";
        private const string InstrPostPanel = "rtv_toast_instr_post";
        private const string GapClass = "rtv-gap";
        private const string RtvCommand = "!rtv";
        private const string BarTrackPanel = "rtv_toast_bar_track";
        private const string BarPanel = "rtv_toast_bar";
        private const string DialogPanel = "rtv_vote_dialog";
        private const string DialogHiddenClass = "rtv-dialog-hidden";
        private const string ToastOnClass = "rtv-toast-on";
        private const string BarOnClass = "rtv-bar-on";
        private const string BarRunClass = "rtv-run";

        // Match the .rtv-toast-bar.rtv-dur-* steps baked into the addon stylesheet
        private static readonly int[] BarDurationSteps = [10, 15, 20, 30, 45, 60, 90, 120, 180, 240, 300];

        // How often the "votes required" line polls the eligible player pool
        private const float RefreshInterval = 5.0f;

        private readonly ILogger<RtvToast> _logger;
        private readonly StringLocalizer _localizer;
        private RtvConfig _rtvConfig = new();
        private PanoramaMenuConfig _panoramaConfig = new();
        private Plugin? _plugin;
        private CustomHudLayout? _hud;
        private int _remainingVotes;
        private int _barDuration;
        private bool _barStarted;
        private bool _warnedBadPosition;
        private Func<int>? _remainingProvider;
        private CounterStrikeSharp.API.Modules.Timers.Timer? _refreshTimer;

        public RtvToast(StringLocalizer localizer, ILogger<RtvToast> logger)
        {
            _localizer = localizer;
            _logger = logger;
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            plugin.RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        }

        public void OnConfigParsed(Config config)
        {
            _rtvConfig = config.Rtv;
            _panoramaConfig = config.PanoramaMenu;
            _warnedBadPosition = false;
        }

        public void OnMapStart(string mapName)
        {
            Hide();
        }

        public void Unload(Plugin plugin)
        {
            Hide();
        }

        public bool IsActive => _hud is { IsValid: true };

        public void ShowOrUpdate(int remainingVotes, Func<int>? remainingProvider = null)
        {
            if (!_rtvConfig.EnablePanoramaToast)
                return;

            _remainingProvider = remainingProvider ?? _remainingProvider;

            if (IsActive)
            {
                UpdateRemaining(remainingVotes);
                return;
            }

            if (!CustomHudLayout.IsSupported(_logger))
            {
                _logger.LogWarning("[RTV.Toast] custom_hud_layout unsupported: {Reason}", CustomHudLayout.UnavailableReason);
                return;
            }

            string layout = _panoramaConfig.AddonName?.Trim() ?? "";
            if (layout.Length == 0)
            {
                _logger.LogWarning("[RTV.Toast] PanoramaMenu.AddonName is empty; no toast will be shown.");
                return;
            }

            var hud = CustomHudLayout.Create(_logger);
            if (hud == null)
                return;

            _hud = hud;
            _remainingVotes = remainingVotes;
            _barStarted = false;
            _barDuration = _rtvConfig.AlwaysActive ? 0 : NearestBarDuration(_rtvConfig.RtvVoteDuration);

            try
            {
                foreach (var player in ServerManager.ValidPlayers())
                    ApplyContent(player.Slot);

                hud.Spawn(layout);

                foreach (var player in ServerManager.ValidPlayers())
                    ApplyContent(player.Slot);

                // The drain is a width transition (100% -> 0%)
                if (_barDuration > 0)
                    _plugin?.AddTimer(0.1f, StartBar, TimerFlags.STOP_ON_MAPCHANGE);

                if (_remainingProvider != null)
                    _refreshTimer = _plugin?.AddTimer(RefreshInterval, RefreshFromProvider,
                        TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REPEAT);

                _logger.LogInformation("[RTV.Toast] Spawned toast custom_hud_layout #{Index} (barDuration={Bar}s).",
                    hud.EntityIndex, _barDuration);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.Toast] Failed to populate/spawn the toast panel.");
                Hide();
            }
        }

        // Rewrites the "votes still needed" line for everyone
        public void UpdateRemaining(int remainingVotes)
        {
            if (_hud is not { IsValid: true } hud)
                return;

            _remainingVotes = remainingVotes;
            var (desc, number) = DescParts(remainingVotes);

            try
            {
                foreach (var player in ServerManager.ValidPlayers())
                {
                    SetToastVariable(hud, DescPanel, "rtv_toast_desc", desc, player.Slot);
                    SetToastVariable(hud, DescNumPanel, "rtv_toast_desc_num", number, player.Slot);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.Toast] Failed to update the vote counter.");
            }
        }

        private const string SplitSentinel = "\uE000";

        private (string Text, string Number) DescParts(int remainingVotes)
        {
            var (prefix, suffix, hasArg) = SplitAtArg("rtv.panorama-descripition");
            if (!hasArg)
                return (prefix, "");

            string number = suffix.Length > 0 ? $"{remainingVotes} {suffix}" : remainingVotes.ToString();
            return (prefix, number);
        }

        private (string Pre, string Cmd, string Post) InstrParts()
        {
            var (pre, post, hasArg) = SplitAtArg("rtv.instructions");
            return hasArg ? (pre, RtvCommand, post) : (pre, "", "");
        }

        private (string Before, string After, bool HasArg) SplitAtArg(string key)
        {
            string formatted = StripChatTags(Localize(key, SplitSentinel));
            int idx = formatted.IndexOf(SplitSentinel, StringComparison.Ordinal);
            if (idx < 0)
                return (formatted, "", false);

            return (formatted[..idx].TrimEnd(), formatted[(idx + SplitSentinel.Length)..].TrimStart(), true);
        }

        public void Hide()
        {
            var hud = _hud;
            _hud = null;
            _barStarted = false;
            _remainingProvider = null;
            _refreshTimer?.Kill();
            _refreshTimer = null;

            if (hud is { IsValid: true })
            {
                try
                {
                    hud.Kill();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RTV.Toast] Failed to kill the toast panel entity.");
                }
            }
        }

        private void RefreshFromProvider()
        {
            if (_remainingProvider == null || _hud is not { IsValid: true })
                return;

            try
            {
                int remaining = _remainingProvider();
                if (remaining != _remainingVotes)
                    UpdateRemaining(remaining);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.Toast] Required-votes refresh failed.");
            }
        }

        private void StartBar()
        {
            if (_hud is not { IsValid: true } hud || _barDuration <= 0)
                return;

            _barStarted = true;

            try
            {
                foreach (var player in ServerManager.ValidPlayers())
                    hud.SetHasClass(BarPanel, BarRunClass, true, player.Slot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.Toast] Failed to start the drain bar.");
            }
        }

        private void ApplyContent(int playerSlot)
        {
            if (_hud is not { IsValid: true } hud)
                return;

            hud.SetHasClass(DialogPanel, DialogHiddenClass, true, playerSlot);
            hud.SetHasClass(ToastPanel, ToastOnClass, true, playerSlot);

            var (desc, number) = DescParts(_remainingVotes);
            var (instrPre, instrCmd, instrPost) = InstrParts();
            SetToastVariable(hud, HeaderPanel, "rtv_toast_header", StripChatTags(Localize("rtv.panorama-header")), playerSlot);
            SetToastVariable(hud, DescPanel, "rtv_toast_desc", desc, playerSlot);
            SetToastVariable(hud, DescNumPanel, "rtv_toast_desc_num", number, playerSlot);
            SetToastVariable(hud, InstrPanel, "rtv_toast_instr", instrPre, playerSlot);
            SetToastVariable(hud, InstrCmdPanel, "rtv_toast_instr_cmd", instrCmd, playerSlot);
            SetToastVariable(hud, InstrPostPanel, "rtv_toast_instr_post", instrPost, playerSlot);

            hud.SetHasClass(InstrCmdPanel, GapClass, instrCmd.Length > 0 && instrPre.Length > 0, playerSlot);
            hud.SetHasClass(InstrPostPanel, GapClass, instrPost.Length > 0 && !char.IsPunctuation(instrPost[0]), playerSlot);

            ApplyPosition(hud, playerSlot);
            ApplyHeaderStyle(hud, playerSlot);
            ApplyTextStyle(hud, playerSlot);

            if (_barDuration > 0)
            {
                hud.SetHasClass(BarTrackPanel, BarOnClass, true, playerSlot);
                hud.SetHasClass(BarPanel, $"rtv-dur-{_barDuration}", true, playerSlot);

                if (_barStarted)
                    hud.SetHasClass(BarPanel, BarRunClass, true, playerSlot);
            }
        }

        private static void SetToastVariable(CustomHudLayout hud, string panelId, string variableName, string value, int playerSlot)
        {
            hud.SetDialogVariable(ToastPanel, variableName, value, playerSlot);
            hud.SetDialogVariable(panelId, variableName, value, playerSlot);
        }

        private void ApplyPosition(CustomHudLayout hud, int playerSlot)
        {
            string position = _panoramaConfig.RtvPosition?.Trim() ?? "";
            if (position.Length == 0)
                return;

            if (CustomHud.PositionClasses.TryGetValue(position, out var positionClass))
            {
                hud.SetHasClass(ToastPanel, positionClass, true, playerSlot);
            }
            else if (!_warnedBadPosition)
            {
                _warnedBadPosition = true;
                _logger.LogWarning("[RTV.Toast] Unknown PanoramaMenu.RtvPosition '{Position}'; using the layout default. Valid: {Valid}",
                    position, string.Join(", ", CustomHud.PositionClasses.Keys));
            }
        }

        private void ApplyHeaderStyle(CustomHudLayout hud, int playerSlot)
        {
            string size = _panoramaConfig.RtvMapVoteHeaderSize?.Trim().ToLowerInvariant() ?? "normal";
            hud.SetHasClass(HeaderPanel, "rtv-header-compact", size == "compact", playerSlot);
            hud.SetHasClass(HeaderPanel, "rtv-header-large", size == "large", playerSlot);
            hud.SetHasClass(HeaderPanel, "rtv-header-xlarge", size == "extralarge", playerSlot);

            string color = _panoramaConfig.RtvMapVoteHeaderColor?.Trim().ToLowerInvariant() ?? "default";
            if (CustomHud.TextColors.Contains(color))
                hud.SetHasClass(HeaderPanel, $"rtv-color-{color}", true, playerSlot);
        }

        private void ApplyTextStyle(CustomHudLayout hud, int playerSlot)
        {
            string size = _panoramaConfig.RtvSize?.Trim().ToLowerInvariant() ?? "normal";
            bool compact = size == "compact";
            bool large = size == "large";
            bool extraLarge = size == "extralarge";

            string color = _panoramaConfig.RtvColor?.Trim().ToLowerInvariant() ?? "default";
            bool hasColor = CustomHud.TextColors.Contains(color);

            foreach (string panel in (string[])[DescPanel, DescNumPanel, InstrPanel, InstrCmdPanel, InstrPostPanel])
            {
                hud.SetHasClass(panel, "rtv-text-compact", compact, playerSlot);
                hud.SetHasClass(panel, "rtv-text-large", large, playerSlot);
                hud.SetHasClass(panel, "rtv-text-xlarge", extraLarge, playerSlot);

                if (hasColor && panel != DescNumPanel && panel != InstrCmdPanel)
                    hud.SetHasClass(panel, $"rtv-color-{color}", true, playerSlot);
            }
        }

        private string Localize(string key, params object[] args)
        {
            return _localizer.Localize(key, args);
        }

        private static string StripChatTags(string text)
        {
            text = ChatTagRegex().Replace(text, "");
            text = new string(text.Where(c => c >= ' ').ToArray());
            return Regex.Replace(text, @"\s{2,}", " ").Trim();
        }

        [GeneratedRegex(@"\{[a-zA-Z][a-zA-Z\-]*\}")]
        private static partial Regex ChatTagRegex();

        private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player == null || !player.ReallyValid())
                return HookResult.Continue;

            if (IsActive)
            {
                try
                {
                    ApplyContent(player.Slot);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RTV.Toast] Failed to apply toast content for slot {Slot}.", player.Slot);
                }
            }

            return HookResult.Continue;
        }

        private static int NearestBarDuration(int seconds)
        {
            int best = BarDurationSteps[0];
            foreach (int step in BarDurationSteps)
            {
                if (Math.Abs(step - seconds) < Math.Abs(best - seconds))
                    best = step;
            }
            return best;
        }
    }
}
