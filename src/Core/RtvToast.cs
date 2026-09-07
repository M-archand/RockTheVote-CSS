using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Extensions;
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
        private CCSCustomHudLayout? _hud;
        private int _remainingVotes;
        private int _barDuration;
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

            string layout = _panoramaConfig.AddonName?.Trim() ?? "";
            if (layout.Length == 0)
            {
                _logger.LogWarning("[RTV.Toast] PanoramaMenu.AddonName is empty; no toast will be shown.");
                return;
            }

            _remainingVotes = remainingVotes;
            _barDuration = _rtvConfig.AlwaysActive ? 0 : NearestBarDuration(_rtvConfig.RtvVoteDuration);

            try
            {
                var hud = CustomHud.SpawnLayout(layout, "rtv_panorama_toast", _logger);
                if (hud == null)
                    return;

                _hud = hud;
                ApplyContent();

                // The drain is a width transition (100% -> 0%)
                if (_barDuration > 0)
                    _plugin?.AddTimer(0.1f, StartBar, TimerFlags.STOP_ON_MAPCHANGE);

                if (_remainingProvider != null)
                    _refreshTimer = _plugin?.AddTimer(RefreshInterval, RefreshFromProvider,
                        TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REPEAT);

                _logger.LogInformation("[RTV.Toast] Spawned toast custom_hud_layout #{Index} (barDuration={Bar}s).",
                    hud.Index, _barDuration);
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
                SetToastVariable(hud, DescPanel, "rtv_toast_desc", desc);
                SetToastVariable(hud, DescNumPanel, "rtv_toast_desc_num", number);
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
            _remainingProvider = null;
            _refreshTimer?.Kill();
            _refreshTimer = null;

            if (hud is { IsValid: true })
            {
                try
                {
                    hud.AcceptInput("Kill");
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

            try
            {
                hud.SetHasClass(BarPanel, BarRunClass, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.Toast] Failed to start the drain bar.");
            }
        }

        private void ApplyContent()
        {
            if (_hud is not { IsValid: true } hud)
                return;

            hud.SetHasClass(DialogPanel, DialogHiddenClass, true);
            hud.SetHasClass(ToastPanel, ToastOnClass, true);

            var (desc, number) = DescParts(_remainingVotes);
            var (instrPre, instrCmd, instrPost) = InstrParts();
            SetToastVariable(hud, HeaderPanel, "rtv_toast_header", StripChatTags(Localize("rtv.panorama-header")));
            SetToastVariable(hud, DescPanel, "rtv_toast_desc", desc);
            SetToastVariable(hud, DescNumPanel, "rtv_toast_desc_num", number);
            SetToastVariable(hud, InstrPanel, "rtv_toast_instr", instrPre);
            SetToastVariable(hud, InstrCmdPanel, "rtv_toast_instr_cmd", instrCmd);
            SetToastVariable(hud, InstrPostPanel, "rtv_toast_instr_post", instrPost);

            hud.SetHasClass(InstrCmdPanel, GapClass, instrCmd.Length > 0 && instrPre.Length > 0);
            hud.SetHasClass(InstrPostPanel, GapClass, instrPost.Length > 0 && !char.IsPunctuation(instrPost[0]));

            ApplyPosition(hud);
            ApplyHeaderStyle(hud);
            ApplyTextStyle(hud);

            if (_barDuration > 0)
            {
                hud.SetHasClass(BarTrackPanel, BarOnClass, true);
                hud.SetHasClass(BarPanel, $"rtv-dur-{_barDuration}", true);
            }
        }

        private static void SetToastVariable(CCSCustomHudLayout hud, string panelId, string variableName, string value)
        {
            hud.SetDialogVariableString(ToastPanel, variableName, value);
            hud.SetDialogVariableString(panelId, variableName, value);
        }

        private void ApplyPosition(CCSCustomHudLayout hud)
        {
            string position = _panoramaConfig.RtvPosition?.Trim() ?? "";
            if (position.Length == 0)
                return;

            if (CustomHud.PositionClasses.TryGetValue(position, out var positionClass))
            {
                hud.SetHasClass(ToastPanel, positionClass, true);
            }
            else if (!_warnedBadPosition)
            {
                _warnedBadPosition = true;
                _logger.LogWarning("[RTV.Toast] Unknown PanoramaMenu.RtvPosition '{Position}'; using the layout default. Valid: {Valid}",
                    position, string.Join(", ", CustomHud.PositionClasses.Keys));
            }
        }

        private void ApplyHeaderStyle(CCSCustomHudLayout hud)
        {
            string size = _panoramaConfig.RtvMapVoteHeaderSize?.Trim().ToLowerInvariant() ?? "normal";
            hud.SetHasClass(HeaderPanel, "rtv-header-compact", size == "compact");
            hud.SetHasClass(HeaderPanel, "rtv-header-large", size == "large");
            hud.SetHasClass(HeaderPanel, "rtv-header-xlarge", size == "extralarge");

            string color = _panoramaConfig.RtvMapVoteHeaderColor?.Trim().ToLowerInvariant() ?? "default";
            if (CustomHud.TextColors.Contains(color))
                hud.SetHasClass(HeaderPanel, $"rtv-color-{color}", true);
        }

        private void ApplyTextStyle(CCSCustomHudLayout hud)
        {
            string size = _panoramaConfig.RtvSize?.Trim().ToLowerInvariant() ?? "normal";
            bool compact = size == "compact";
            bool large = size == "large";
            bool extraLarge = size == "extralarge";

            string color = _panoramaConfig.RtvColor?.Trim().ToLowerInvariant() ?? "default";
            bool hasColor = CustomHud.TextColors.Contains(color);

            foreach (string panel in (string[])[DescPanel, DescNumPanel, InstrPanel, InstrCmdPanel, InstrPostPanel])
            {
                hud.SetHasClass(panel, "rtv-text-compact", compact);
                hud.SetHasClass(panel, "rtv-text-large", large);
                hud.SetHasClass(panel, "rtv-text-xlarge", extraLarge);

                if (hasColor && panel != DescNumPanel && panel != InstrCmdPanel)
                    hud.SetHasClass(panel, $"rtv-color-{color}", true);
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
