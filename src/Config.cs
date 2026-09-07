using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace cs2_rockthevote
{
    public class RtvConfig
    {
        public bool Enabled { get; set; } = true;
        public bool EnabledInWarmup { get; set; } = false;
        public bool EnablePanoramaVote { get; set; } = false;
        public bool EnablePanoramaToast { get; set; } = false;
        public int MinPlayers { get; set; } = 0;
        public int MinRounds { get; set; } = 0;
        public bool ChangeAtRoundEnd { get; set; } = false;
        public int MapChangeDelay { get; set; } = 5;
        public bool SoundEnabled { get; set; } = false;
        public float SoundVolume { get; set; } = 0.5F;
        public string SoundPath { get; set; } = "sounds/vo/announcer/cs2_classic/felix_broken_fang_pick_1_map_tk01.vsnd_c";
        public int MapsToShow { get; set; } = 6;
        public bool AlwaysActive { get; set; } = true;
        public bool AlwaysActiveReminder { get; set; } = true;
        public int ReminderInterval { get; set; } = 180;
        public int RtvVoteDuration { get; set; } = 60;
        public int MapVoteDuration { get; set; } = 60;
        public int CooldownDuration { get; set; } = 180;
        public int MapStartDelay { get; set; } = 180;
        public int VotePercentage { get; set; } = 51;
        public bool EnableCountdown { get; set; } = true;
        public string CountdownType { get; set; } = "chat";
        public int ChatCountdownInterval { get; set; } = 15;
    }

    public class EndOfMapConfig
    {
        public bool Enabled { get; set; } = true;
        public bool EnableRevote {get; set; } = false;
        public int MapsToShow { get; set; } = 6;
        public string MenuType { get; set; } = "WasdMenu";
        public bool ChangeMapImmediately { get; set; } = false;
        public int VoteDuration { get; set; } = 150;
        public bool SoundEnabled { get; set; } = true;
        public float SoundVolume { get; set; } = 0.5F;
        public string SoundPath { get; set; } = "1974266470";
        public int TriggerSecondsBeforeEnd { get; set; } = 180;
        public int TriggerRoundsBeforeEnd { get; set; } = 0;
        public float DelayToChangeInTheEnd { get; set; } = 0F;
        public bool IncludeExtendCurrentMap { get; set; } = true;
        public bool EnableCountdown { get; set; } = false;
        public string CountdownType { get; set; } = "chat";
        public int ChatCountdownInterval { get; set; } = 30;
        public bool ChatMapChoiceReminder { get; set; } = true;
        public int ChatMapChoiceInterval { get; set; } = 30;
        public bool EnableHint { get; set; } = false;
        public string HintType { get; set; } = "GameHint";
    }

    public class VotemapConfig
    {
        public bool Enabled { get; set; } = false;
        public string MenuType { get; set; } = "WasdMenu";
        public int VotePercentage { get; set; } = 50;
        public bool ChangeMapImmediately { get; set; } = true;
        public bool EnabledInWarmup { get; set; } = false;
        public int MinPlayers { get; set; } = 0;
        public int MinRounds { get; set; } = 0;
        public string Permission { get; set; } = "@css/vip";

        [JsonIgnore]
        public string[] Permissions => PermissionUtility.Parse(Permission);
    }

    public class VoteExtendConfig
    {
        public bool Enabled { get; set; } = false;
        public bool EnablePanorama { get; set; } = true;
        public int VoteDuration { get; set; } = 60;
        public int VotePercentage { get; set; } = 50;
        public int CooldownDuration { get; set; } = 180;
        public bool EnableCountdown { get; set; } = true;
        public string CountdownType { get; set; } = "chat";
        public int ChatCountdownInterval { get; set; } = 15;
        public string Permission { get; set; } = "@css/vip";

        [JsonIgnore]
        public string[] Permissions => PermissionUtility.Parse(Permission);
    }

    public class NominateConfig
    {
        public bool Enabled { get; set; } = true;
        public bool EnabledInWarmup { get; set; } = true;
        public string MenuType { get; set; } = "WasdMenu";
        public int NominateLimit { get; set; } = 1;
        public string Permission { get; set; } = "";

        [JsonIgnore]
        public string[] Permissions => PermissionUtility.Parse(Permission);
    }

    public class MapChooserConfig
    {
        public string Command { get; set; } = "mapmenu,mm";
        public string MenuType { get; set; } = "WasdMenu";
        public string Permission { get; set; } = "@css/changemap";

        [JsonIgnore]
        public string[] Permissions => PermissionUtility.Parse(Permission);
    }

    // Shared settings for every menu using the panorama custom hud
    public class PanoramaMenuConfig
    {
        public string AddonName { get; set; } = "panorama/layout/custom_game/rockthevote.xml";
        public bool   EnableClickVoting { get; set; } = true;
        public string MapVoteMenuPosition { get; set; } = "CenterLeft";
        public string MapVoteHeaderSize { get; set; } = "extralarge";
        public string MapVoteHeaderColor { get; set; } = "orange";
        public string MapVoteRowSize { get; set; } = "large";
        public string MapVoteRowColor { get; set; } = "green";
        public string RtvPosition { get; set; } = "CenterRight";
        public string RtvMapVoteHeaderSize { get; set; } = "large";
        public string RtvMapVoteHeaderColor { get; set; } = "orange";
        public string RtvColor { get; set; } = "white";
        public string RtvSize { get; set; } = "normal";
    }

    public class GeneralConfig
    {
        public string AdminPermission { get; set; } = "@css/root";

        [JsonIgnore]
        public string[] AdminPermissions => PermissionUtility.Parse(AdminPermission);

        public bool DebugLogging { get; set; } = false;
        public int MaxMapExtensions { get; set; } = 2;
        public string DisableMapExtensions { get; set; } = "";

        [JsonIgnore]
        public string[] DisabledExtensionMaps => PermissionUtility.Parse(DisableMapExtensions);
        public int RoundTimeExtension { get; set; } = 15;
        public int MapsInCoolDown { get; set; } = 3;
        public List<string> CooldownCommands { get; set; } = new() { "css_cooldown" };
        public bool HideHudAfterVote { get; set; } = true;
        public bool RandomStartMap { get; set; } = false;
        public bool IncludeSpectator { get; set; } = true;
        public bool IncludeAFK { get; set; } = true;
        public int AFKCheckInterval { get; set; } = 30;
        public bool EnableMapValidation { get; set; } = false;
        public string SteamApiKey { get; set; } = "";
        public string DiscordWebhook { get; set; } = "";
    }

    public class Config : BasePluginConfig, IBasePluginConfig
    {
        public const int CurrentVersion = 26;

        [JsonPropertyName("ConfigVersion")]
        public override int Version { get; set; } = CurrentVersion;
        public RtvConfig Rtv { get; set; } = new();
        public EndOfMapConfig EndOfMapVote { get; set; } = new();
        public NominateConfig Nominate { get; set; } = new();
        public VotemapConfig Votemap { get; set; } = new();
        public VoteExtendConfig VoteExtend { get; set; } = new();
        public MapChooserConfig MapChooser { get; set; } = new();
        public PanoramaMenuConfig PanoramaMenu { get; set; } = new();
        public GeneralConfig General { get; set; } = new();
    }
}
