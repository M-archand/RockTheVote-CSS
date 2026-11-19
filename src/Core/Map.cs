namespace cs2_rockthevote
{
    public class Map(string name, string? id, string? baseName = null)
    {
        public string? Id { get; set; } = id?.Trim();
        // Label shown to players, e.g. "surf_beginner (T1, Staged)"
        public string Name { get; set; } = name.Trim();
        // Map file name used for changelevel and current-map checks
        public string BaseName { get; set; } = baseName?.Trim() ?? MapNameHelper.StripDisplaySuffix(name.Trim());
    }
}