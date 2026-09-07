namespace cs2_rockthevote
{
    /// Comparison helpers for the enum-like string keys in the config (CountdownType,
    /// MenuType, HintType, ...). Config values are matched trimmed and case-insensitively
    /// so "Chat", "chat" and " CHAT " all resolve to the same behaviour.
    public static class ConfigValue
    {
        public static bool Is(string? value, string expected)
        {
            return string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAny(string? value, params string[] expected)
        {
            string? trimmed = value?.Trim();
            foreach (string candidate in expected)
            {
                if (string.Equals(trimmed, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// Looks a config key up in a registry dictionary, ignoring case even when the
        /// dictionary itself was built with a case-sensitive comparer.
        public static bool TryResolve<T>(IEnumerable<KeyValuePair<string, T>> registry, string? configured, out T resolved)
        {
            string key = configured?.Trim() ?? "";
            if (key.Length > 0)
            {
                foreach (var entry in registry)
                {
                    if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved = entry.Value;
                        return true;
                    }
                }
            }

            resolved = default!;
            return false;
        }

        public static T Resolve<T>(IEnumerable<KeyValuePair<string, T>> registry, string? configured, T fallback)
        {
            return TryResolve(registry, configured, out T resolved) ? resolved : fallback;
        }
    }
}
