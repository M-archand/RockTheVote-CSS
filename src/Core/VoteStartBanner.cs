using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace cs2_rockthevote.Core
{
    public class VoteStartBanner : IPluginDependency<Plugin, Config>
    {
        private const string BannerPanel = "rtv_delay";
        private const string HeaderPanel = "rtv_delay_header";
        private const string TextPanel = "rtv_delay_text";
        private const string BubblePanel = "rtv_delay_bubble";
        private const string TimerPanel = "rtv_delay_timer";
        private const string BarPanel = "rtv_delay_bar";
        private const string DialogPanel = "rtv_vote_dialog";
        private const string DialogHiddenClass = "rtv-dialog-hidden";
        private const string BannerOnClass = "rtv-delay-on";

        // Steps baked into the addon stylesheet: banner widths 240..640 in 20px
        // (.rtv-delay.rtv-w-*) and bar widths (.rtv-delay-bar.rtv-pct-*)
        private static readonly int[] WidthSteps = [.. Enumerable.Range(0, 21).Select(i => 240 + i * 20)];
        private const int BarStepPercent = 2;

        private readonly ILogger<VoteStartBanner> _logger;
        private ILogger _debugLogger = NullLogger.Instance;
        private readonly StringLocalizer _localizer;
        private PanoramaMenuConfig _panoramaConfig = new();
        private Plugin? _plugin;
        private CCSCustomHudLayout? _hud;
        private int _totalSeconds = 1;
        private int _remainingSeconds;
        private string _barClass = "";
        private bool _warnedBadPosition;
        private CounterStrikeSharp.API.Modules.Timers.Timer? _timer;

        public VoteStartBanner(StringLocalizer localizer, ILogger<VoteStartBanner> logger)
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
            _panoramaConfig = config.PanoramaMenu;
            _warnedBadPosition = false;
            _debugLogger = DebugLog.For(_logger, config);
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

        public bool Show(int seconds)
        {
            Hide();

            string layout = _panoramaConfig.AddonName?.Trim() ?? "";
            if (layout.Length == 0)
            {
                _logger.LogWarning("[RTV.VoteStartBanner] PanoramaMenu.AddonName is empty; no banner will be shown.");
                return false;
            }

            _totalSeconds = Math.Max(seconds, 1);
            _remainingSeconds = _totalSeconds;

            try
            {
                var hud = CustomHud.SpawnLayout(layout, "rtv_panorama_delay", _logger);
                if (hud == null)
                    return false;

                _hud = hud;
                ApplyContent(hud);

                // The drain is a width transition, so the first step goes out a frame after spawn
                _plugin?.AddTimer(0.1f, () => SetBar(_remainingSeconds - 1), TimerFlags.STOP_ON_MAPCHANGE);
                _timer = _plugin?.AddTimer(1.0f, Tick, TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REPEAT);

                _debugLogger.LogInformation("[RTV.VoteStartBanner] Spawned custom_hud_layout #{Index} ({Seconds}s).",
                    hud.Index, _totalSeconds);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.VoteStartBanner] Failed to populate/spawn the banner.");
                Hide();
                return false;
            }
        }

        public void Hide()
        {
            var hud = _hud;
            _hud = null;
            _barClass = "";
            StopTimer();

            if (hud is { IsValid: true })
            {
                try
                {
                    hud.AcceptInput("Kill");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RTV.VoteStartBanner] Failed to kill the banner entity.");
                }
            }
        }

        private void StopTimer()
        {
            _timer?.Kill();
            _timer = null;
        }

        private void Tick()
        {
            if (_hud is not { IsValid: true } hud || --_remainingSeconds <= 0)
            {
                StopTimer();
                return;
            }

            try
            {
                SetVariable(hud, TimerPanel, "rtv_delay_timer", TimerText(_remainingSeconds));
                // The bar runs one second ahead so it reaches zero as the banner goes away
                SetBar(_remainingSeconds - 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.VoteStartBanner] Failed to update the countdown.");
            }
        }

        // Bar width = secondsLeft / total, adjusted to the stylesheet's percent steps
        private void SetBar(int secondsLeft)
        {
            if (_hud is not { IsValid: true } hud)
                return;

            int percent = (int)Math.Round(100.0 * Math.Max(secondsLeft, 0) / _totalSeconds / BarStepPercent) * BarStepPercent;
            string barClass = $"rtv-pct-{percent}";
            if (barClass == _barClass)
                return;

            try
            {
                if (_barClass.Length > 0)
                    hud.SetHasClass(BarPanel, _barClass, false);
                hud.SetHasClass(BarPanel, barClass, true);
                _barClass = barClass;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.VoteStartBanner] Failed to update the drain bar.");
            }
        }

        private static string TimerText(int seconds) => $"{seconds}s";

        private void ApplyContent(CCSCustomHudLayout hud)
        {
            hud.SetHasClass(DialogPanel, DialogHiddenClass, true);
            hud.SetHasClass(BannerPanel, BannerOnClass, true);

            string header = RtvToast.StripChatTags(_localizer.Localize("rtv.panorama-delay-header"));
            string text = RtvToast.StripChatTags(_localizer.Localize("rtv.panorama-delay-text"));
            SetVariable(hud, HeaderPanel, "rtv_delay_header", header);
            SetVariable(hud, TextPanel, "rtv_delay_text", text);
            SetVariable(hud, TimerPanel, "rtv_delay_timer", TimerText(_remainingSeconds));

            hud.SetHasClass(BannerPanel, ComputeWidthClass(header, text), true);
            ApplyPosition(hud);
            ApplyHeaderStyle(hud);
            ApplyTextStyle(hud);
        }

        private static void SetVariable(CCSCustomHudLayout hud, string panelId, string variableName, string value)
        {
            hud.SetDialogVariableString(BannerPanel, variableName, value);
            hud.SetDialogVariableString(panelId, variableName, value);
        }

        private string HeaderSize() => _panoramaConfig.RtvMapVoteHeaderSize?.Trim().ToLowerInvariant() ?? "normal";

        private string TextSize() => _panoramaConfig.RtvSize?.Trim().ToLowerInvariant() ?? "normal";

        // Estimates the pixel width of the longer text line plus the bubble and padding,
        // adjusted to the addon's width steps.
        private string ComputeWidthClass(string header, string text)
        {
            int headerFont = HeaderSize() switch
            {
                "compact" => 16,
                "large" => 20,
                "extralarge" => 22,
                _ => 18,
            };

            (int textFont, int bubbleWidth) = TextSize() switch
            {
                "compact" => (18, 60),
                "large" => (28, 84),
                "extralarge" => (32, 96),
                _ => (24, 72),
            };

            double headerWidth = TextWidth(header, headerFont) + header.Length; // 1px letter-spacing
            double textWidth = TextWidth(text, textFont);

            // longest line + bubble gap + bubble + row padding + border + slack
            int estimated = (int)Math.Ceiling(Math.Max(headerWidth, textWidth)) + 18 + bubbleWidth + 24 + 2 + 8;
            foreach (int step in WidthSteps)
            {
                if (estimated <= step)
                    return $"rtv-w-{step}";
            }

            return $"rtv-w-{WidthSteps[^1]}";
        }

        // Bold Stratum2 caps measure ~0.4em per character
        private static double TextWidth(string text, int fontSize)
        {
            double em = 0;
            foreach (char c in text)
                em += c >= '\u2E80' ? 1.0 : 0.42;
            return em * fontSize;
        }

        private void ApplyPosition(CCSCustomHudLayout hud)
        {
            string position = _panoramaConfig.RtvPosition?.Trim() ?? "";
            if (position.Length == 0)
                return;

            if (CustomHud.PositionClasses.TryGetValue(position, out var positionClass))
            {
                hud.SetHasClass(BannerPanel, positionClass, true);
            }
            else if (!_warnedBadPosition)
            {
                _warnedBadPosition = true;
                _logger.LogWarning("[RTV.VoteStartBanner] Unknown PanoramaMenu.RtvPosition '{Position}'; using the layout default. Valid: {Valid}",
                    position, string.Join(", ", CustomHud.PositionClasses.Keys));
            }
        }

        // Header size follows the toast header setting, the header color tints the bubble,
        // timer, and bar while the header text itself stays the layout's green
        private void ApplyHeaderStyle(CCSCustomHudLayout hud)
        {
            string size = HeaderSize();
            hud.SetHasClass(HeaderPanel, "rtv-header-compact", size == "compact");
            hud.SetHasClass(HeaderPanel, "rtv-header-large", size == "large");
            hud.SetHasClass(HeaderPanel, "rtv-header-xlarge", size == "extralarge");

            string color = _panoramaConfig.RtvMapVoteHeaderColor?.Trim().ToLowerInvariant() ?? "default";
            if (!CustomHud.TextColors.Contains(color))
                return;

            foreach (string panel in (string[])[BubblePanel, TimerPanel, BarPanel])
                hud.SetHasClass(panel, $"rtv-color-{color}", true);
        }

        // Body size/color follow the toast body settings
        private void ApplyTextStyle(CCSCustomHudLayout hud)
        {
            string size = TextSize();
            foreach (string panel in (string[])[TextPanel, TimerPanel, BubblePanel])
            {
                hud.SetHasClass(panel, "rtv-text-compact", size == "compact");
                hud.SetHasClass(panel, "rtv-text-large", size == "large");
                hud.SetHasClass(panel, "rtv-text-xlarge", size == "extralarge");
            }

            string color = _panoramaConfig.RtvColor?.Trim().ToLowerInvariant() ?? "default";
            if (CustomHud.TextColors.Contains(color))
                hud.SetHasClass(TextPanel, $"rtv-color-{color}", true);
        }
    }
}
