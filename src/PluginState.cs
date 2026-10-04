namespace cs2_rockthevote
{
    public class PluginState : IPluginDependency<Plugin, Config>
    {
        public bool Unloaded { get; set; }
        public bool MapChangeScheduled { get; set; }
        public bool EofVoteHappening { get; set; }
        public bool ExtendTimeVoteHappening { get; set; }
        public bool RtvVoteHappening { get; set; }
        public bool RtvMapVotePending { get; set; }
        public int MapExtensionCount { get; set; } = 0;

        public bool DisableCommands => MapChangeScheduled || EofVoteHappening || ExtendTimeVoteHappening || RtvVoteHappening || RtvMapVotePending;

        public void OnMapStart(string map)
        {
            MapChangeScheduled = false;
            EofVoteHappening = false;
            ExtendTimeVoteHappening = false;
            RtvVoteHappening = false;
            RtvMapVotePending = false;
            MapExtensionCount = 0;
        }
    }
}
