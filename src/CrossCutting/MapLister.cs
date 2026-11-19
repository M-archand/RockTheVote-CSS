using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class MapLister : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<MapLister> _logger;
        private readonly MapListDatabase _database;
        public Map[] Maps { get; private set; } = Array.Empty<Map>();
        public bool MapsLoaded { get; private set; } = false;
        // true when the last load could not read its configured source, even if an older list or maplist.txt is still in use
        public bool LastLoadFailed { get; private set; } = false;
        // true while a database load is in flight; the file path is synchronous
        public bool IsLoading { get; private set; } = false;
        public event EventHandler<Map[]>? EventMapsLoaded;
        private Plugin? _plugin;
        private ILogger _debugLogger = NullLogger.Instance;
        private GeneralConfig _generalConfig = new();
        private int _loadGeneration = 0;
        private bool _unloaded = false;

        public MapLister(MapListDatabase database, ILogger<MapLister> logger)
        {
            _database = database;
            _logger = logger;
        }

        public void Clear()
        {
            SetMaps(Array.Empty<Map>(), loaded: false);
        }

        private void SetMaps(Map[] maps, bool loaded)
        {
            Maps = maps;
            MapsLoaded = loaded;
            MapNameHelper.SetBaseNames(maps);
        }

        public void LoadMaps()
        {
            if (_plugin is null)
            {
                Clear();
                _logger.LogWarning("[RTV.MapLister] LoadMaps called before plugin was assigned.");
                return;
            }

            // Any load still in flight is superseded by this one
            int generation = ++_loadGeneration;

            if (_generalConfig.EnabledDatabase)
            {
                LoadMapsFromDatabase(generation);
                return;
            }

            IsLoading = false;
            Clear();
            LoadMapsFromFile();
            LastLoadFailed = !MapsLoaded;
            EventMapsLoaded?.Invoke(this, Maps);
        }

        // Runs the callback right away unless a database load is in flight, then once on the main thread when it finishes
        public void WhenLoaded(Action callback)
        {
            if (!IsLoading)
            {
                callback();
                return;
            }

            bool done = false;
            EventHandler<Map[]>? handler = null;
            handler = (_, _) =>
            {
                // PruneMaps also raises the event; wait for the load itself
                if (IsLoading || done)
                    return;

                done = true;
                EventMapsLoaded -= handler;
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RTV.MapLister] WhenLoaded callback failed. message={Message}", ex.Message);
                }
            };
            EventMapsLoaded += handler;
        }

        // Keeps the current list until the query finishes, and when it fails, so a reload never leaves the plugin without maps
        private void LoadMapsFromDatabase(int generation)
        {
            IsLoading = true;

            _ = Task.Run(async () =>
            {
                Map[]? maps = null;
                try
                {
                    maps = await _database.LoadActiveMapsAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RTV.MapLister] Unexpected error while loading the map list from the database. message={Message}", ex.Message);
                }

                Server.NextWorldUpdate(() =>
                {
                    if (_unloaded || generation != _loadGeneration)
                        return;

                    IsLoading = false;

                    try
                    {
                        if (maps is not null)
                        {
                            LastLoadFailed = false;
                            SetMaps(maps, loaded: true);
                            if (maps.Length == 0)
                                _logger.LogWarning("[RTV.MapLister] The database returned no active maps. Set Active = 1 on the maps you want in the rotation.");
                            else
                                _debugLogger.LogInformation("[RTV.MapLister] Loaded {MapCount} maps from the database.", maps.Length);
                        }
                        else if (MapsLoaded && Maps.Length > 0)
                        {
                            LastLoadFailed = true;
                            _logger.LogError("[RTV.MapLister] Database load failed, keeping the {MapCount} previously loaded maps.", Maps.Length);
                        }
                        else
                        {
                            LastLoadFailed = true;
                            Clear();
                            string mapsFile = MapsFilePath();
                            if (File.Exists(mapsFile))
                            {
                                _logger.LogWarning("[RTV.MapLister] Database load failed, falling back to {MapListPath}.", mapsFile);
                                LoadMapsFromFile();
                            }
                            else
                            {
                                _logger.LogError("[RTV.MapLister] Database load failed and no {MapListPath} exists to fall back to. The map list is empty.", mapsFile);
                                Server.PrintToConsole("[RTV.MapLister] Database load failed and no maplist.txt exists to fall back to. The map list is empty.");
                            }
                        }

                        EventMapsLoaded?.Invoke(this, Maps);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[RTV.MapLister] Applying the database map list failed. message={Message}", ex.Message);
                    }
                });
            });
        }

        private string MapsFilePath()
            => Path.GetFullPath(Path.Combine(_plugin!.ModulePath, "../maplist.txt"));

        private void LoadMapsFromFile()
        {
            string mapsFile = MapsFilePath();
            string exampleFile = Path.GetFullPath(Path.Combine(_plugin!.ModulePath, "../maplist.example.txt"));

            if (!File.Exists(mapsFile))
            {
                _logger.LogError("[RTV.MapLister] Missing required map list file at {MapListPath}.", mapsFile);
                if (File.Exists(exampleFile))
                {
                    _debugLogger.LogInformation(
                        "[RTV.MapLister] Example map list found at {ExamplePath}. Copy or rename it to {MapListPath}.",
                        exampleFile,
                        mapsFile
                    );
                }

                Server.PrintToConsole($"[RTV.MapLister] Missing required map list file at {mapsFile}.");
                return;
            }

            try
            {
                Map[] maps = [.. File.ReadAllText(mapsFile)
                    .Replace("\r\n", "\n")
                    .Split("\n")
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("//"))
                    .Select(mapLine =>
                    {
                        string[] args = mapLine.Split(":");
                        string mapName = args[0];
                        string? mapValue = args.Length == 2 ? args[1] : null;
                        return new Map(mapName, mapValue);
                    })];

                SetMaps(maps, loaded: true);
                _debugLogger.LogInformation("[RTV.MapLister] Loaded {MapCount} maps from {MapListPath}.", Maps.Length, mapsFile);
            }
            catch (Exception ex)
            {
                Clear();
                _logger.LogError(ex, "[RTV.MapLister] Failed to load map list from {MapListPath}. message={Message}", mapsFile, ex.Message);
                Server.PrintToConsole($"[RTV.MapLister] Failed to load map list from {mapsFile}. message={ex.Message}");
            }
        }

        public void OnMapStart(string _map)
        {
            if (_plugin is not null)
                LoadMaps();
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
            _debugLogger = DebugLog.For(_logger, config);
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            LoadMaps();
        }

        public void Unload(Plugin plugin)
        {
            _unloaded = true;
        }

        // Returns the exact map name, or null if none found
        public string? GetExactMapName(string name)
        {
            return Maps
                .Select(m => m.Name)
                .FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // Returns all map names that contain the given argument
        public List<string> GetMatchingMapNames(string partial)
        {
            return [.. Maps
                .Select(m => m.Name)
                .Where(n => n.Contains(partial, StringComparison.OrdinalIgnoreCase))];
        }

        // Disable maps no longer available on the workshop from showing (the source list is left untouched)
        public void PruneMaps(IEnumerable<Map> toRemove)
        {
            var removeList = toRemove.ToList();
            if (removeList.Count == 0)
                return;

            int before = Maps.Length;
            Map[] remaining = [.. Maps.Where(m => !removeList.Any(r =>
                string.Equals(r.Name, m.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Id, m.Id, StringComparison.Ordinal)))];

            int removed = before - remaining.Length;
            if (removed == 0)
                return;

            SetMaps(remaining, MapsLoaded);
            _debugLogger.LogInformation("[RTV.MapLister] Pruned {Removed} unavailable map(s) from the in-memory list; {Remaining} remain.", removed, Maps.Length);
            EventMapsLoaded?.Invoke(this, Maps);
        }
    }
}
