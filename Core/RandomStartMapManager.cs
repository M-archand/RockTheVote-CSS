using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    public class RandomStartMapManager(MapLister mapLister, ChangeMapManager changeMapManager, ILogger<RandomStartMapManager> logger) : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<RandomStartMapManager> _logger = logger;
        private readonly MapLister _mapLister = mapLister;
        private readonly ChangeMapManager _changeMapManager = changeMapManager;
        private bool _firstMapStart = true;
        private Timer? _timerChangeMap;
        private GeneralConfig _generalConfig = new();
        private Plugin? _plugin;

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void Unload(Plugin plugin)
        {
            _timerChangeMap?.Kill();
            _timerChangeMap = null;
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
        }

        public void OnMapStart(string currentMap)
        {
            // Only act while the initial random change is still pending
            if (!_generalConfig.RandomStartMap || !_firstMapStart)
                return;

            // Build a list of maps
            var candidates = _mapLister.Maps?
                .Where(m => !string.Equals(m.Name, currentMap, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates == null || candidates.Count == 0)
            {
                _firstMapStart = false;
                return;
            }

            // Pick a random map
            var pick = candidates[new Random().Next(candidates.Count)];

            _timerChangeMap?.Kill();
            _timerChangeMap = _plugin?.AddTimer(3.0f, () =>
            {
                _timerChangeMap = null;
                _firstMapStart = false;

                _changeMapManager.ScheduleMapChange(pick.Name);
                _changeMapManager.ChangeNextMap(0f);
            });
        }
    }
}