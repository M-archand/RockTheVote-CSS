namespace cs2_rockthevote
{
    public static class MapNameHelper
    {
        private static Dictionary<string, string> _baseNames = new(StringComparer.OrdinalIgnoreCase);

        // Called by MapLister whenever the list changes so labels resolve to the map file name
        public static void SetBaseNames(IEnumerable<Map> maps)
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in maps)
                lookup.TryAdd(map.Name, map.BaseName);

            _baseNames = lookup;
        }

        // Resolves a map label to its file name, e.g. "surf_beginner (T1, Staged)" -> "surf_beginner".
        public static string GetBaseName(string displayName)
            => _baseNames.TryGetValue(displayName, out var baseName) ? baseName : StripDisplaySuffix(displayName);

        // Strips display suffixes from a map name, e.g. "surf_beginner (T1, Staged)" -> "surf_beginner".
        public static string StripDisplaySuffix(string displayName)
        {
            var idx = displayName.IndexOf(' ');
            return idx > 0 ? displayName[..idx] : displayName;
        }
    }
}
