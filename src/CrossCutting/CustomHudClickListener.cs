using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    // Receives panorama panel button clicks by detouring the server's CustomHudClicked
    // receiver, the function the inbound CS_UM_CustomHudClicked dispatch always calls, with
    // the clicking controller resolved and the button id already decoded.
    //
    // Hook point arguments:
    //   arg 0 - singleton, ignored
    //   arg 1 - CBasePlayerController* of the clicker (may be null)
    //   arg 2 - CCSCustomHudLayout* that was clicked
    //   arg 3 - std::string* button id
    public class CustomHudClickListener : IPluginDependency<Plugin, Config>
    {
        private const string ReceiverKey = "CCSCustomHudLayout_CustomHudClickedReceiver";

        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private readonly ILogger<CustomHudClickListener> _logger;
        private MemoryFunctionVoid<IntPtr, IntPtr, IntPtr, IntPtr>? _receiver;
        private Func<DynamicHook, HookResult>? _handler;

        // (playerSlot, layout entity pointer, buttonId)
        public event Action<int, nint, string>? OnHudClicked;

        public bool IsHooked { get; private set; }

        public CustomHudClickListener(ILogger<CustomHudClickListener> logger)
        {
            _logger = logger;
        }

        // Loads the gamedata signatures and installs the click-receiver hook.
        // On any failure, click voting stays off and chat voting takes over.
        public void OnLoad(Plugin plugin)
        {
            CustomHudGameData.Load(plugin.ModuleDirectory, _logger);

            try
            {
                var receiver = CustomHudLayout.Bind<MemoryFunctionVoid<IntPtr, IntPtr, IntPtr, IntPtr>>(ReceiverKey, _logger);
                if (receiver == null)
                {
                    _logger.LogWarning("[RTV.CustomHud] CustomHudClicked receiver unavailable. Click voting off; chat voting stays.");
                    return;
                }

                _handler = OnClicked;
                receiver.Hook(_handler, HookMode.Pre);
                _receiver = receiver;
                IsHooked = true;
                _logger.LogDebug("[RTV.CustomHud] Hooked the CustomHudClicked receiver; panorama click voting available.");
            }
            catch (Exception ex)
            {
                IsHooked = false;
                _logger.LogWarning(ex, "[RTV.CustomHud] Failed to hook the CustomHudClicked receiver. Chat voting stays.");
            }
        }

        public void Unload(Plugin plugin)
        {
            try
            {
                if (IsHooked && _receiver != null && _handler != null)
                    _receiver.Unhook(_handler, HookMode.Pre);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.CustomHud] Failed to unhook the CustomHudClicked receiver.");
            }

            IsHooked = false;
        }

        // Pre-hook on the engine's click receiver. Extracts the clicker,
        // layout entity, and button id, then raises OnHudClicked.
        private HookResult OnClicked(DynamicHook hook)
        {
            try
            {
                nint controller = hook.GetParam<IntPtr>(1);
                nint layout = hook.GetParam<IntPtr>(2);
                string? buttonId = ReadStdString(hook.GetParam<IntPtr>(3));

                if (string.IsNullOrEmpty(buttonId) || controller == 0 || OnHudClicked == null)
                    return HookResult.Continue;

                var player = new CCSPlayerController(controller);
                if (!player.IsValid)
                    return HookResult.Continue;

                _logger.LogDebug("[RTV.CustomHud] Click received: slot={Slot} layout=0x{Layout:X} button='{Button}'",
                    player.Slot, layout, buttonId);

                OnHudClicked.Invoke(player.Slot, layout, buttonId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV.CustomHud] Click handler failed.");
            }

            return HookResult.Continue;
        }

        // Reads a native C++ std::string, handling the differing libstdc++ (heap pointer first)
        // and MSVC (inline/heap union) layouts.
        private static string? ReadStdString(nint stdString)
        {
            if (stdString == 0)
                return null;

            if (!IsWindows)
            {
                nint data = Marshal.ReadIntPtr(stdString);
                return data == 0 ? null : Marshal.PtrToStringUTF8(data);
            }

            long capacity = Marshal.ReadInt64(stdString + 0x18);
            if (capacity < 16)
                return Marshal.PtrToStringUTF8(stdString);

            nint heap = Marshal.ReadIntPtr(stdString);
            return heap == 0 ? null : Marshal.PtrToStringUTF8(heap);
        }
    }
}
