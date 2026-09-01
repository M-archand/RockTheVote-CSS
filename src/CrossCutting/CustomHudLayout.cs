using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class CustomHudLayout
    {
        private static MemoryFunctionVoid<IntPtr, int, IntPtr, IntPtr, IntPtr>? _setDialogVarForPlayer;
        private static MemoryFunctionVoid<IntPtr, int, IntPtr, IntPtr, bool>? _setHasClassForPlayer;
        private static MemoryFunctionVoid<IntPtr, int, bool>? _setInputCapture;

        private static bool _resolved;
        private static bool _available;
        private static string _unavailableReason = "not initialized";

        private readonly CBaseEntity _entity;
        private readonly ILogger _logger;

        private CustomHudLayout(CBaseEntity entity, ILogger logger)
        {
            _entity = entity;
            _logger = logger;
        }

        public bool IsValid => _entity.IsValid;
        public uint EntityIndex => _entity.Index;
        public nint EntityPointer => _entity.Handle;

        public static bool IsSupported(ILogger logger)
        {
            Resolve(logger);
            return _available;
        }

        public static string UnavailableReason => _unavailableReason;

        // Creates an unspawned custom_hud_layout entity wrapped for state writes,
        // null when the engine setters are unavailable
        public static CustomHudLayout? Create(ILogger logger)
        {
            Resolve(logger);
            if (!_available)
                return null;

            var entity = Utilities.CreateEntityByName<CBaseEntity>("custom_hud_layout");
            if (entity == null || !entity.IsValid)
            {
                logger.LogWarning("[RTV.CustomHud] Failed to create custom_hud_layout entity.");
                return null;
            }

            return new CustomHudLayout(entity, logger);
        }

        public void Spawn(string layoutPath)
        {
            using var kv = new CEntityKeyValues();
            kv.SetString("layout", layoutPath);
            kv.SetString("targetname", "rtv_panorama_hud");
            _entity.DispatchSpawn(kv);
        }

        public void Kill()
        {
            if (_entity.IsValid)
                _entity.AcceptInput("Kill");
        }

        // Sets a {s:variable} dialog variable on a panel for one player
        public void SetDialogVariable(string panelId, string variableName, string value, int playerSlot)
        {
            nint entity = _entity.Handle;
            if (entity == 0 || _setDialogVarForPlayer == null)
                return;

            using var args = new UtlStringArgs(panelId, variableName, value);
            _setDialogVarForPlayer.Invoke(entity, playerSlot, args[0], args[1], args[2]);
        }

        // Forces a css class on/off a panel for one player
        public void SetHasClass(string panelId, string className, bool hasClass, int playerSlot)
        {
            nint entity = _entity.Handle;
            if (entity == 0 || _setHasClassForPlayer == null)
                return;

            using var args = new UtlStringArgs(panelId, className);
            _setHasClassForPlayer.Invoke(entity, playerSlot, args[0], args[1], hasClass);
        }

        // Captures (or releases) a player's cursor so Button panels are clickable
        public void SetInputCaptureEnabled(bool enabled, int playerSlot)
        {
            nint entity = _entity.Handle;
            if (entity == 0)
                return;

            _setInputCapture?.Invoke(entity, playerSlot, enabled);
        }

        // Binds the three CCSCustomHudLayout engine setters once per plugin load,
        // failure of any marks panorama support unavailable
        private static void Resolve(ILogger logger)
        {
            if (_resolved)
                return;
            _resolved = true;

            try
            {
                _setDialogVarForPlayer = Bind<MemoryFunctionVoid<IntPtr, int, IntPtr, IntPtr, IntPtr>>(
                    "CCSCustomHudLayout_SetDialogVariableStringForPlayer", logger);
                _setHasClassForPlayer = Bind<MemoryFunctionVoid<IntPtr, int, IntPtr, IntPtr, bool>>(
                    "CCSCustomHudLayout_SetHasClassForPlayer", logger);
                _setInputCapture = Bind<MemoryFunctionVoid<IntPtr, int, bool>>(
                    "CCSCustomHudLayout_SetInputCaptureEnabled", logger);

                if (_setDialogVarForPlayer == null || _setHasClassForPlayer == null || _setInputCapture == null)
                {
                    _unavailableReason = $"one or more CCSCustomHudLayout engine functions did not resolve (check {CustomHudGameData.Source})";
                    logger.LogWarning("[RTV.CustomHud] {Reason}", _unavailableReason);
                    return;
                }

                _available = true;
                _unavailableReason = "";
            }
            catch (Exception ex)
            {
                _unavailableReason = $"resolution failed: {ex.Message}";
                logger.LogWarning(ex, "[RTV.CustomHud] Failed to resolve custom_hud_layout support.");
            }
        }

        // Looks up a named signature in gamedata/customhud.json and wraps
        // the matched server function as a callable/hookable MemoryFunction
        internal static T? Bind<T>(string name, ILogger logger) where T : BaseMemoryFunction
        {
            if (!CustomHudGameData.TryGetSignature(name, out var signature))
            {
                logger.LogWarning("[RTV.CustomHud] No gamedata signature for {Name} (file: {Source}).", name, CustomHudGameData.Source);
                return null;
            }

            try
            {
                var fn = (T)Activator.CreateInstance(typeof(T), signature)!;
                if (fn.Handle != IntPtr.Zero)
                    return fn;

                logger.LogWarning("[RTV.CustomHud] Signature for {Name} did not resolve against the server binary.", name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[RTV.CustomHud] Failed to bind {Name}.", name);
            }

            return null;
        }

        // Marshals up to three strings as CUtlString* arguments (each an 8-byte slot holding a
        // char*, the engine copies what it needs and never takes ownership)
        private readonly struct UtlStringArgs : IDisposable
        {
            private readonly nint _block;
            private readonly nint _s0, _s1, _s2;

            public UtlStringArgs(string a, string b, string? c = null)
            {
                _block = Marshal.AllocHGlobal(24);
                _s0 = Marshal.StringToCoTaskMemUTF8(a);
                _s1 = Marshal.StringToCoTaskMemUTF8(b);
                _s2 = c != null ? Marshal.StringToCoTaskMemUTF8(c) : 0;
                Marshal.WriteIntPtr(_block, 0, _s0);
                Marshal.WriteIntPtr(_block, 8, _s1);
                Marshal.WriteIntPtr(_block, 16, _s2);
            }

            public nint this[int index] => _block + index * 8;

            public void Dispose()
            {
                Marshal.FreeCoTaskMem(_s0);
                Marshal.FreeCoTaskMem(_s1);
                if (_s2 != 0)
                    Marshal.FreeCoTaskMem(_s2);
                Marshal.FreeHGlobal(_block);
            }
        }
    }
}
