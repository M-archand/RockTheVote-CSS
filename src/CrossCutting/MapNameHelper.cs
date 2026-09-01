namespace cs2_rockthevote
{
    public static class MapNameHelper
    {

        // Strips display suffixes from a map name, e.g. "surf_beginner (T1, Staged)" -> "surf_beginner".
        public static string GetBaseName(string displayName)
        {
            var idx = displayName.IndexOf(' ');
            return idx > 0 ? displayName[..idx] : displayName;
        }
    }
}
