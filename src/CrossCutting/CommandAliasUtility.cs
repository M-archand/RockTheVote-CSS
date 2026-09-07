namespace cs2_rockthevote
{
    // Turns configured command aliases into the names to register with CounterStrikeSharp.
    // The "css_" prefix is optional in the config.
    public static class CommandAliasUtility
    {
        public const string ChatPrefix = "css_";

        // Strips a leading "css_" and trims. Returns "" for unusable entries.
        public static string Normalize(string? alias)
        {
            string trimmed = alias?.Trim() ?? "";
            if (trimmed.StartsWith(ChatPrefix, StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[ChatPrefix.Length..].Trim();

            return trimmed;
        }

        // Expands configured aliases into the deduplicated list of command names to register.
        public static List<string> Expand(IEnumerable<string>? configured)
        {
            var names = new List<string>();
            if (configured is null)
                return names;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in configured)
            {
                string name = Normalize(entry);
                if (name.Length == 0)
                    continue;

                if (seen.Add(name))
                    names.Add(name);

                string prefixed = ChatPrefix + name;
                if (seen.Add(prefixed))
                    names.Add(prefixed);
            }

            return names;
        }
    }
}
