using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using System.Data.Common;
using System.Globalization;

namespace cs2_rockthevote
{
    public class MapListDatabase(ILogger<MapListDatabase> logger) : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<MapListDatabase> _logger = logger;
        private ILogger _debugLogger = NullLogger.Instance;
        private DatabaseConfig _config = new();

        public void OnConfigParsed(Config config)
        {
            _config = config.Database;
            _debugLogger = DebugLog.For(_logger, config);
        }

        // MySqlConnector types stay inside this method so the plugin still loads without MySqlConnector.dll when the database is off
        private DbConnection CreateConnection()
        {
            if (!Enum.TryParse(_config.SslMode?.Trim(), ignoreCase: true, out MySqlSslMode sslMode) || !Enum.IsDefined(sslMode))
            {
                _logger.LogWarning("[RTV.MapDatabase] Invalid Database.SslMode \"{SslMode}\", using Preferred.", _config.SslMode);
                sslMode = MySqlSslMode.Preferred;
            }

            return new MySqlConnection(new MySqlConnectionStringBuilder
            {
                Server = _config.Host,
                Port = (uint)Math.Clamp(_config.Port, 1, ushort.MaxValue),
                UserID = _config.User,
                Password = _config.Password,
                Database = _config.Database,
                SslMode = sslMode,
                ConnectionTimeout = 10,
                // One query per map start; no pool to keep sockets or background tasks alive across hot reloads
                Pooling = false,
            }.ConnectionString);
        }

        // Runs off the main thread. Returns null when the table name is invalid or the query fails.
        public async Task<Map[]?> LoadActiveMapsAsync()
        {
            string table = _config.TableName.Trim();
            if (table.Length == 0 || !table.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                _logger.LogError("[RTV.MapDatabase] Invalid Database.TableName \"{Table}\". Only letters, digits and underscores are allowed.", _config.TableName);
                return null;
            }

            if (!TryParseTiers(_config.EnabledTiers, out byte[] tiers))
            {
                _logger.LogError("[RTV.MapDatabase] Invalid Database.EnabledTiers \"{EnabledTiers}\". Use tiers from 0 to 255 as a list (\"1,2,3\"), a range (\"1-3\") or both (\"1-3,5\").", _config.EnabledTiers);
                return null;
            }

            string tierFilter = tiers.Length == 0 ? "" : $" AND `Tier` IN ({string.Join(",", tiers)})";

            try
            {
                await using DbConnection connection = CreateConnection();
                await connection.OpenAsync().ConfigureAwait(false);
                await EnsureTableAsync(connection, table).ConfigureAwait(false);

                await using DbCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT `MapName`, `DisplayName`, `WorkshopID` FROM `{table}` WHERE `Active` = 1{tierFilter} ORDER BY `MapName`";

                var maps = new List<Map>();
                // Labels must be unique: votes, nominations and the map change resolve maps by label
                var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using DbDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    string mapName = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                    if (!IsValidMapName(mapName))
                    {
                        _logger.LogWarning("[RTV.MapDatabase] Skipping row with invalid MapName \"{MapName}\". Only letters, digits, '_', '-', '.' and '/' are allowed.", mapName);
                        continue;
                    }

                    string? displayName = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
                    string? workshopId = ReadWorkshopId(reader, mapName);

                    string label = string.IsNullOrWhiteSpace(displayName) ? mapName : displayName;
                    if (!labels.Add(label))
                    {
                        if (!labels.Add(mapName))
                        {
                            _logger.LogWarning("[RTV.MapDatabase] Skipping {MapName}: DisplayName \"{DisplayName}\" is already used by another map and MapName is taken as a label too.", mapName, label);
                            continue;
                        }

                        _logger.LogWarning("[RTV.MapDatabase] DisplayName \"{DisplayName}\" is used by more than one map; showing {MapName} by its file name instead.", label, mapName);
                        label = mapName;
                    }

                    maps.Add(new Map(label, workshopId, mapName));
                }

                if (maps.Count == 0 && tiers.Length > 0)
                    _logger.LogWarning("[RTV.MapDatabase] No active maps in {Table} match Database.EnabledTiers \"{EnabledTiers}\". Maps without a Tier are left out while it is set.", table, _config.EnabledTiers);
                else if (maps.Count == 0)
                    _logger.LogWarning("[RTV.MapDatabase] No active maps in {Table}. Set Active = 1 on the maps you want in the rotation.", table);

                _debugLogger.LogInformation("[RTV.MapDatabase] Query returned {MapCount} active map(s) from {Host}:{Port}/{Database}.{Table}.",
                    maps.Count, _config.Host, _config.Port, _config.Database, table);
                return [.. maps];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RTV.MapDatabase] Failed to load the map list from {Host}:{Port}/{Database}.{Table}. message={Message}",
                    _config.Host, _config.Port, _config.Database, table, ex.Message);
                return null;
            }
        }

        // Blank = no filter. Anything else has to parse completely, so a typo never widens the map pool
        private static bool TryParseTiers(string? value, out byte[] tiers)
        {
            tiers = [];
            if (string.IsNullOrWhiteSpace(value))
                return true;

            var parsed = new SortedSet<byte>();
            foreach (string token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string[] bounds = token.Split('-', StringSplitOptions.TrimEntries);
                if (bounds.Length > 2
                    || !byte.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte first)
                    || !byte.TryParse(bounds[^1], NumberStyles.None, CultureInfo.InvariantCulture, out byte last))
                    return false;

                for (int tier = Math.Min(first, last); tier <= Math.Max(first, last); tier++)
                    parsed.Add((byte)tier);
            }

            tiers = [.. parsed];
            return tiers.Length > 0;
        }

        // MapName goes into changelevel console commands, so nothing that could start a second command
        private static bool IsValidMapName(string mapName)
            => mapName.Length > 0 && mapName.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/');

        private string? ReadWorkshopId(DbDataReader reader, string mapName)
        {
            if (reader.IsDBNull(2))
                return null;

            string raw = Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture)?.Trim() ?? "";
            if (ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id))
                return id == 0 ? null : id.ToString(CultureInfo.InvariantCulture);

            if (raw.Length > 0)
                _logger.LogWarning("[RTV.MapDatabase] Ignoring invalid WorkshopID \"{WorkshopId}\" for {MapName}.", raw, mapName);

            return null;
        }

        // Best effort: a read-only database user can still load maps from an existing table
        private async Task EnsureTableAsync(DbConnection connection, string table)
        {
            try
            {
                await using DbCommand command = connection.CreateCommand();
                command.CommandText = $@"CREATE TABLE IF NOT EXISTS `{table}` (
    `MapName` VARCHAR(128) NOT NULL,
    `DisplayName` VARCHAR(255) NULL,
    `WorkshopID` BIGINT(20) UNSIGNED NULL,
    `Active` TINYINT NOT NULL DEFAULT 1,
    `Tier` TINYINT UNSIGNED NULL DEFAULT NULL,
    PRIMARY KEY (`MapName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _debugLogger.LogInformation("[RTV.MapDatabase] Could not ensure table {Table} exists (continuing with the query). message={Message}", table, ex.Message);
            }
        }
    }
}
