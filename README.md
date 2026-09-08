<div align="center">

# CS2 RockTheVote (RTV)

![GitHub Downloads](https://img.shields.io/github/downloads/M-archand/cs2-rockthevote/total?style=for-the-badge)
![GitHub Release](https://img.shields.io/github/v/release/M-archand/cs2-rockthevote?style=for-the-badge)

General purpose map voting plugin for CS2, built on CounterStrikeSharp.

[Features](#features) • [Requirements](#requirements) • [Installation](#installation) • [Commands](#commands) • [Configuration](#configuration) • [Map List](#map-list) • [Translations](#translations)

</div>

## Features

### Voting
- **Rock the Vote.** `!rtv` in chat, or the built-in CS2 Panorama vote (F1 = Yes, F2 = No). Configurable pass percentage, cooldowns, min players/rounds, and an always-active mode with periodic chat reminders.
- **End of Map Vote.** Triggers a set number of seconds or rounds before the map ends. Supports map cooldown, an "extend current map" option, and several menu types.
- **Revote.** `!revote` during an active End of Map Vote reopens a frozen WASD menu (no exit) so the player can change their choice.
- **Nominate.** `!nominate <map>` adds a map to the next vote. Partial name matching, conflict resolution for similar names (`surf_beginner`, `surf_beginner2`), configurable per-player limit.
- **Votemap.** `!votemap <map>` starts a vote to change to a specific map.
- **Vote Extend.** `!ve` / `!voteextend` starts a vote to extend the current map, optionally via the Panorama F1/F2 vote.
- **Extend.** `!extend 10` extends the current map by 10 minutes (admin).

### Menus & Display
- **Menu types.** `ChatMenu`, `CenterHtmlMenu`, `WasdMenu`, `ConsoleMenu` (via CS2MenuManager), plus the plugin's own `HudMenu` and clickable `panorama` menu for the End of Map Vote.
- **Panorama vote menu.** A clickable custom HUD panel shipped in a workshop addon. Click to vote, live vote counters, voted-row highlight, winner flash, countdown timer, configurable position/sizes/colors. See [Panorama HUD addon](#panorama-hud-addon).
- **Panorama RTV toast.** A small HUD pop-up while an `!rtv` is collecting votes, showing votes required and instructions.
- **Countdowns.** Chat (interval based) or HUD (per second) countdown for every vote type.
- **Hints & sounds.** Optional center-screen hint and configurable sound when a vote starts.

### Map Management
- **Custom map list.** Plain-text `maplist.txt` with support for workshop maps and custom display names like `surf_beginner (T1, Staged)`.
- **Map cooldown persistence.** Recently played maps stay on cooldown across restarts (stored in `mapcooldown.txt`).
- **Map list validator.** Detects workshop maps that are no longer available, removes them from the in-memory list (so they can't be nominated, voted for, or switched to), and reports to the error log or a Discord webhook. Optional Steam Web API key enables batch validation (100 IDs per request).
- **Map Chooser.** `!mapmenu` opens a menu of all maps and changes to the selection immediately (flag restricted).
- **Hot reload.** `!reloadmaps` rebuilds the map list and `!reloadrtv` reloads the config mid-game.

### Other
- **Translations.** 12 languages, see [Translations](#translations).
- **AFK & spectator handling.** Choose whether spectators and AFK players count toward `!rtv`.
- **Config validation.** Warns on startup about keys or sections missing from your config.

<details>
<summary><b>Screenshots</b></summary>

**Nominate**

![nominate](https://github.com/user-attachments/assets/6ac056bc-9842-4422-ac0d-c7cd814b3ba6)

**HUD alert**

![hudalert](https://github.com/user-attachments/assets/23c35f20-b4f0-4122-b241-287b44efdb27)

**HUD / chat countdown**

![hudcountdown](https://github.com/user-attachments/assets/e1034f3c-340a-4d88-8d8a-96526f333fad)
![chatcountdown](https://github.com/user-attachments/assets/803826a1-665b-4ab7-9e38-fbb0e8d702be)

**Panorama vote (RTV / Vote Extend)**

![RTV.PanoramaVote](https://github.com/user-attachments/assets/31ebe223-225f-4cef-812e-3bf6c56e590d)
![voteextend](https://github.com/user-attachments/assets/5cfd9a5f-36a5-4a11-ae26-3e74d5387251)

**Panorama End of Map Vote menu**

![panoramamenu](https://github.com/user-attachments/assets/c207fbd4-a068-4071-982f-bd837edfe53d)

**Panorama RTV toast**

![rtvtoast](https://github.com/user-attachments/assets/7cec25c9-9c92-47af-887e-d08606b97e23)

**Menu types**

![wasdmenu](https://github.com/user-attachments/assets/1df185bf-4313-4010-81de-98111ae383dc)
![chatmenu](https://github.com/user-attachments/assets/8d7e9ee8-b26e-47b1-89d8-ced96b13a392)
![hudmenu](https://github.com/user-attachments/assets/0fd37e45-bf7f-4f97-9b7b-7fab92352392)

**Map list validator**

![MapDiscordWebhook](https://github.com/user-attachments/assets/eaf8d706-abd1-4258-a7a3-b9cb44500802)
![WorkshopMapLog](https://github.com/user-attachments/assets/2f65dd9d-1ee9-4217-a753-81358973df2e)

**!maps**

![mapscommand](https://github.com/user-attachments/assets/d4ab1377-0b29-45b6-bdaa-06b6a7664751)

</details>

## Requirements

| Dependency | Version |
| --- | --- |
| [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) | v374 or newer (the Panorama menu uses the `CustomHudLayout` API added in 1.0.374) |
| [CS2MenuManager](https://github.com/schwarper/CS2MenuManager) | v42 or newer |

## Installation

1. Download the latest release from the [releases page](https://github.com/M-archand/cs2-rockthevote/releases).
2. Extract the zip into `addons/counterstrikesharp/plugins`.
3. Edit `maplist.example.txt` with your maps and rename it to `maplist.txt`. See [Map List](#map-list).
4. Load the plugin once. The config is created at `addons/counterstrikesharp/configs/plugins/RockTheVote/RockTheVote.json`.
5. Edit the config, then run `!reloadrtv`, reload the plugin, or restart the server. Changing the map is not enough.

### Panorama HUD addon

Only needed if you set `EndOfMapVote.MenuType` to `panorama` or enable `Rtv.EnablePanoramaToast`.

The clickable panel is a `custom_hud_layout` Panorama panel. Its sources live in this repo under [panorama/](panorama/) (`layout/custom_game/rockthevote.xml` and `styles/custom_game/rockthevote.css`). The compiled `.vxml_c` / `.vcss_c` files must be shipped to clients in a workshop addon, and `PanoramaMenu.AddonName` must point at the layout path inside that addon.

- Clicks arrive through CounterStrikeSharp's built-in `CustomHudLayout` API, no gamedata file needed.
- You can also set `PanoramaMenu.EnableClickVoting` to `false`. The panel then becomes display-only and players vote by typing `!1` .. `!N` in chat.
- Position, size, and color variants are baked into the addon stylesheet, so keep the addon in sync with the plugin version.

## Commands

The `css_` prefix is optional in chat, so `!rtv`, `/rtv`, and `css_rtv` in console all work.

| Command | Aliases | Description | Restricted by |
| --- | --- | --- | --- |
| `!rtv` | | Vote to rock the vote | |
| `!nominate <map>` | `!nom` | Nominate a map for the next vote | `Nominate.Permission` |
| `!votemap <map>` | | Start a vote to change to a specific map | `Votemap.Permission` |
| `!voteextend` | `!ve` | Start a vote to extend the current map | `VoteExtend.Permission` |
| `!extend <minutes>` | | Extend the current map immediately | `General.ExtendPermission` |
| `!revote` | | Reopen the End of Map Vote menu to change your vote | `EndOfMapVote.EnableRevote` |
| `!mapmenu` | `!mm` (configurable) | Open a menu of all maps and change to the selection | `MapChooser.Permission` |
| `!maps` | `!maplist` | Print all available maps to console | |
| `!cooldown` | `!cd` (configurable) | Print the maps currently on cooldown to console, most recent first (10s per-player cooldown) | |
| `!nextmap` | | Print the next map to chat | |
| `!timeleft` | | Print the time left on the current map | |
| `!reloadmaps` | | Rebuild the map list from `maplist.txt` | `General.AdminPermission` |
| `!reloadrtv` | | Reload the plugin config | `General.AdminPermission` |

**Permission format.** Every `Permission` key accepts a comma separated list of CSS flags, e.g. `"@css/vip,@css/nominate"`. Any listed flag grants access. An empty string means anyone can use the command.

## Configuration

Config file: `addons/counterstrikesharp/configs/plugins/RockTheVote/RockTheVote.json` (`ConfigVersion` 26).

Things to know:
- String values such as `MenuType`, `CountdownType`, `HintType`, positions, and colors are matched case-insensitively.
- Command lists (`MapChooser.Command`, `General.CooldownCommands`) accept a JSON array (`["mapmenu", "mm"]`) or a CSV string (`"mapmenu,mm"`). The `css_` prefix is optional.
- `SoundVolume` other than `1.0` requires a soundevent hash in `SoundPath` rather than a file path.
- Missing keys or sections are logged as warnings on load and fall back to their defaults.

### Rtv

| Key | Default | Description |
| --- | --- | --- |
| `Enabled` | `true` | Enable the `!rtv` command |
| `EnabledInWarmup` | `false` | Allow `!rtv` during warmup |
| `EnablePanoramaVote` | `false` | `true` = built-in CS2 Panorama vote (F1 = Yes, F2 = No). `false` = players type `!rtv` in chat |
| `EnablePanoramaToast` | `false` | Show a small Panorama pop-up while an `!rtv` is collecting votes (header, live "votes required" line, instructions, and a draining time-left bar when `AlwaysActive` is `false`). Needs the [Panorama HUD addon](#panorama-hud-addon) and the `Rtv*` keys in `PanoramaMenu` |
| `MinPlayers` | `0` | Minimum players before `!rtv` can be used |
| `MinRounds` | `0` | Minimum rounds played before `!rtv` can be used |
| `ChangeAtRoundEnd` | `false` | `true` = wait until round end to change the map. `false` = use `MapChangeDelay` |
| `MapChangeDelay` | `5` | Seconds after the map vote passes before the map changes. `0` = immediate |
| `SoundEnabled` | `false` | Play a sound when the rtv map vote starts |
| `SoundVolume` | `0.5` | Volume of the alert sound |
| `SoundPath` | `"sounds/vo/announcer/cs2_classic/felix_broken_fang_pick_1_map_tk01.vsnd_c"` | File path or soundevent hash |
| `MapsToShow` | `6` | Maps shown in the map vote when the rtv passes |
| `AlwaysActive` | `true` | `true` = the rtv stays open once started until it passes. `false` = timed vote lasting `RtvVoteDuration` |
| `AlwaysActiveReminder` | `true` | Periodically remind chat that an rtv is active and how many votes are still needed |
| `ReminderInterval` | `180` | Seconds between reminders |
| `RtvVoteDuration` | `60` | Length of the rtv vote in seconds (when `AlwaysActive` is `false`) |
| `MapVoteDuration` | `60` | Length of the resulting map vote in seconds |
| `CooldownDuration` | `180` | Seconds before another `!rtv` can be started after one fails |
| `MapStartDelay` | `180` | Seconds after map start before `!rtv` can be used |
| `VotePercentage` | `51` | Percentage of players required to pass |
| `EnableCountdown` | `true` | Show a countdown during the vote |
| `CountdownType` | `"chat"` | `chat` = print time left on an interval. `hud` = persistent HUD text updated every second |
| `ChatCountdownInterval` | `15` | Seconds between chat countdown messages |

### EndOfMapVote

| Key | Default | Description |
| --- | --- | --- |
| `Enabled` | `true` | Enable the End of Map Vote |
| `EnableRevote` | `false` | Allow `!revote` to reopen the vote (forced WASD menu, player frozen, no exit until they pick) |
| `MapsToShow` | `6` | Maps shown in the vote. The extend option takes one slot when `IncludeExtendCurrentMap` is `true` |
| `MenuType` | `"WasdMenu"` | `ChatMenu`, `CenterHtmlMenu`, `WasdMenu`, `ConsoleMenu`, `HudMenu`, or `panorama` |
| `ChangeMapImmediately` | `false` | `true` = change as soon as the vote ends. `false` = change when the map ends |
| `VoteDuration` | `150` | Length of the vote in seconds. Must be smaller than `TriggerSecondsBeforeEnd` when `General.ForceMapChange` is `true` |
| `SoundEnabled` | `true` | Play a sound when the vote starts |
| `SoundVolume` | `0.5` | Volume of the alert sound |
| `SoundPath` | `"1974266470"` | File path or soundevent hash |
| `TriggerSecondsBeforeEnd` | `180` | Seconds before the map ends that the vote starts |
| `TriggerRoundsBeforeEnd` | `0` | Rounds before the end to trigger the vote. Use `0` for modes like surf/bhop or it will never appear |
| `DelayToChangeInTheEnd` | `0` | Seconds the MVP screen shows at the end when `ChangeMapImmediately` is `false` |
| `IncludeExtendCurrentMap` | `true` | Include an "extend current map" option |
| `EnableCountdown` | `false` | Show a countdown during the vote |
| `CountdownType` | `"chat"` | `chat` or `hud`, see Rtv |
| `ChatCountdownInterval` | `30` | Seconds between chat countdown messages |
| `ChatMapChoiceReminder` | `true` | Periodically reprint the map choices and current tally to chat. Only applies to `ChatMenu`, or a `panorama` vote that fell back to chat-number voting |
| `ChatMapChoiceInterval` | `30` | Seconds between map choice reprints |
| `EnableHint` | `false` | Show a center-screen message when the vote starts |
| `HintType` | `"GameHint"` | `csay` = HUD message lower middle. `GameHint` = game instructor text center middle |

### Nominate

| Key | Default | Description |
| --- | --- | --- |
| `Enabled` | `true` | Enable `!nominate` |
| `EnabledInWarmup` | `true` | Allow nominations during warmup |
| `MenuType` | `"WasdMenu"` | `ChatMenu`, `CenterHtmlMenu`, `WasdMenu`, `ConsoleMenu`, or `HudMenu` |
| `NominateLimit` | `1` | Nominations per player per map |
| `Permission` | `"@css/vip,@css/nominate"` | Flags allowed to nominate. Empty = anyone |

### Votemap

| Key | Default | Description |
| --- | --- | --- |
| `Enabled` | `false` | Enable `!votemap` |
| `MenuType` | `"WasdMenu"` | `ChatMenu`, `CenterHtmlMenu`, `WasdMenu`, or `ConsoleMenu` |
| `VotePercentage` | `50` | Percentage of players required to pass |
| `ChangeMapImmediately` | `true` | Change as soon as the vote passes |
| `EnabledInWarmup` | `false` | Allow `!votemap` during warmup |
| `MinPlayers` | `0` | Minimum players before `!votemap` can be used |
| `MinRounds` | `0` | Minimum rounds before `!votemap` can be used |
| `Permission` | `"@css/vip, @css/votemap"` | Flags allowed to use it. Empty = anyone |

### VoteExtend

| Key | Default | Description |
| --- | --- | --- |
| `Enabled` | `false` | Enable `!ve` / `!voteextend` |
| `EnablePanorama` | `true` | `true` = built-in CS2 Panorama vote (F1 = Yes, F2 = No). `false` = players type `!ve` in chat |
| `VoteDuration` | `60` | Length of the vote in seconds |
| `VotePercentage` | `50` | Percentage of players required to pass |
| `CooldownDuration` | `180` | Seconds before another `!ve` can be started |
| `EnableCountdown` | `true` | Show a countdown during the vote |
| `CountdownType` | `"chat"` | `chat` or `hud`, see Rtv |
| `ChatCountdownInterval` | `15` | Seconds between chat countdown messages |
| `Permission` | `"@css/vip,@css/votextend"` | Flags allowed to use it. Empty = anyone |

### MapChooser

| Key | Default | Description |
| --- | --- | --- |
| `Command` | `["mapmenu", "mm"]` | Commands that open the menu. Array or CSV, `css_` optional |
| `MenuType` | `"WasdMenu"` | `ChatMenu`, `CenterHtmlMenu`, `WasdMenu`, or `ConsoleMenu` |
| `Permission` | `"@css/changemap"` | Flags allowed to use it. Empty = anyone |

### PanoramaMenu

Shared settings for everything drawn on the Panorama custom HUD: the End of Map Vote when `MenuType` is `panorama`, and the RTV toast. Menus are click-driven: players click a row to vote (the row stays highlighted), can change their vote by clicking another row, and the winning row flashes for about 5 seconds when the vote ends. A countdown timer sits next to the Close button.

| Key | Default | Description |
| --- | --- | --- |
| `AddonName` | `"panorama/layout/custom_game/rockthevote.xml"` | Path of the compiled layout inside the workshop addon |
| `EnableClickVoting` | `true` | Click-to-vote on the panel. `false` = display-only panel with chat-number voting (`!1` .. `!N`) |
| `MapVoteMenuPosition` | `"CenterLeft"` | `CenterTop`, `CenterMiddle`, `CenterBottom`, `TopLeftCorner`, `TopRightCorner`, `BottomLeftCorner`, `BottomRightCorner`, `CenterLeft`, `CenterRight` |
| `MapVoteHeaderSize` | `"extralarge"` | `compact`, `normal`, `large`, `extralarge` |
| `MapVoteHeaderColor` | `"orange"` | `default` (gold), `white`, `green`, `cyan`, `yellow`, `orange`, `red`, `magenta`, `blue` |
| `MapVoteRowSize` | `"large"` | `compact`, `normal`, `large`, `extralarge` |
| `MapVoteRowColor` | `"green"` | `white`, `green`, `cyan`, `yellow`, `orange`, `red`, `magenta`, `blue` |
| `RtvPosition` | `"CenterRight"` | Where the RTV toast appears. Same options as `MapVoteMenuPosition` |
| `RtvMapVoteHeaderSize` | `"large"` | Toast header size, same options as `MapVoteHeaderSize` |
| `RtvMapVoteHeaderColor` | `"orange"` | Toast header color, same options as `MapVoteHeaderColor` |
| `RtvColor` | `"white"` | Toast body text color, same options as `MapVoteHeaderColor` |
| `RtvSize` | `"normal"` | Toast body text size, same options as `MapVoteHeaderSize` |

### General

| Key | Default | Description |
| --- | --- | --- |
| `AdminPermission` | `"@css/root,@css/admin"` | Flags allowed to use `!reloadrtv` and `!reloadmaps` |
| `ExtendPermission` | `"@css/admin,@css/changemap"` | Flags allowed to use `!extend`. Empty = anyone |
| `DebugLogging` | `false` | Verbose diagnostic logging (vote flow, map change, hint and validation details) |
| `ForceMapChange` | `true` | Only matters while `mp_ignore_round_win_conditions` is `1`. `true` = the vote runs before the timer ends and the plugin changes map just before 00:00. `false` = the timer runs down to 00:00, the End of Map Vote starts at 00:00 without interrupting anyone's run, and the map changes when the vote ends |
| `MaxMapExtensions` | `2` | How many times a map can be extended |
| `DisableMapExtensions` | `""` | CSV list of maps that can't be extended, e.g. `"surf_utopia_njv, surf_mesa_revo"`. These won't show the extend option |
| `RoundTimeExtension` | `15` | Minutes added by a Vote Extend or End of Map Vote extension |
| `MapsInCoolDown` | `3` | Recent maps excluded from the vote and nominations. `0` = no cooldown (the current map is always excluded) |
| `CooldownCommands` | `["cooldown", "cd"]` | Commands that print the cooldown list. Array or CSV, `css_` optional |
| `HideHudAfterVote` | `true` | Close the `HudMenu` / `panorama` panel after the player votes (`!revote` brings the panel back) |
| `RandomStartMap` | `false` | `true` = pick a random map when the server starts. `false` = use the map from your startup command |
| `IncludeSpectator` | `true` | Spectators count toward `!rtv` |
| `IncludeAFK` | `true` | AFK players count toward `!rtv` |
| `AFKCheckInterval` | `30` | Seconds between AFK checks (compares player positions, also runs when a vote starts) |
| `EnableMapValidation` | `false` | Check `maplist.txt` for workshop maps that are no longer on the workshop |
| `SteamApiKey` | `""` | Blank = 1 request/second HTML checks. Set a [Steam Web API key](https://steamcommunity.com/dev/apikey) to batch validation (100 IDs per call) |
| `DiscordWebhook` | `""` | Blank = no alert. Full `https://discord.com/api/webhooks/...` URL to be notified of missing workshop maps |

<details>
<summary><b>Full default config</b></summary>

```json
{
  "ConfigVersion": 26,
  "Rtv": {
    "Enabled": true,
    "EnabledInWarmup": false,
    "EnablePanoramaVote": false,
    "EnablePanoramaToast": false,
    "MinPlayers": 0,
    "MinRounds": 0,
    "ChangeAtRoundEnd": false,
    "MapChangeDelay": 5,
    "SoundEnabled": false,
    "SoundVolume": 0.5,
    "SoundPath": "sounds/vo/announcer/cs2_classic/felix_broken_fang_pick_1_map_tk01.vsnd_c",
    "MapsToShow": 6,
    "AlwaysActive": true,
    "AlwaysActiveReminder": true,
    "ReminderInterval": 180,
    "RtvVoteDuration": 60,
    "MapVoteDuration": 60,
    "CooldownDuration": 180,
    "MapStartDelay": 180,
    "VotePercentage": 51,
    "EnableCountdown": true,
    "CountdownType": "chat",
    "ChatCountdownInterval": 15
  },
  "EndOfMapVote": {
    "Enabled": true,
    "EnableRevote": false,
    "MapsToShow": 6,
    "MenuType": "WasdMenu",
    "ChangeMapImmediately": false,
    "VoteDuration": 150,
    "SoundEnabled": true,
    "SoundVolume": 0.5,
    "SoundPath": "1974266470",
    "TriggerSecondsBeforeEnd": 180,
    "TriggerRoundsBeforeEnd": 0,
    "DelayToChangeInTheEnd": 0,
    "IncludeExtendCurrentMap": true,
    "EnableCountdown": false,
    "CountdownType": "chat",
    "ChatCountdownInterval": 30,
    "ChatMapChoiceReminder": true,
    "ChatMapChoiceInterval": 30,
    "EnableHint": false,
    "HintType": "GameHint"
  },
  "Nominate": {
    "Enabled": true,
    "EnabledInWarmup": true,
    "MenuType": "WasdMenu",
    "NominateLimit": 1,
    "Permission": "@css/vip,@css/nominate"
  },
  "Votemap": {
    "Enabled": false,
    "MenuType": "WasdMenu",
    "VotePercentage": 50,
    "ChangeMapImmediately": true,
    "EnabledInWarmup": false,
    "MinPlayers": 0,
    "MinRounds": 0,
    "Permission": "@css/vip, @css/votemap"
  },
  "VoteExtend": {
    "Enabled": false,
    "EnablePanorama": true,
    "VoteDuration": 60,
    "VotePercentage": 50,
    "CooldownDuration": 180,
    "EnableCountdown": true,
    "CountdownType": "chat",
    "ChatCountdownInterval": 15,
    "Permission": "@css/vip,@css/votextend"
  },
  "MapChooser": {
    "Command": ["mapmenu", "mm"],
    "MenuType": "WasdMenu",
    "Permission": "@css/changemap"
  },
  "PanoramaMenu": {
    "AddonName": "panorama/layout/custom_game/rockthevote.xml",
    "EnableClickVoting": true,
    "MapVoteMenuPosition": "CenterLeft",
    "MapVoteHeaderSize": "extralarge",
    "MapVoteHeaderColor": "orange",
    "MapVoteRowSize": "large",
    "MapVoteRowColor": "green",
    "RtvPosition": "CenterRight",
    "RtvMapVoteHeaderSize": "large",
    "RtvMapVoteHeaderColor": "orange",
    "RtvColor": "white",
    "RtvSize": "normal"
  },
  "General": {
    "AdminPermission": "@css/root,@css/admin",
    "ExtendPermission": "@css/admin,@css/changemap",
    "DebugLogging": false,
    "ForceMapChange": true,
    "MaxMapExtensions": 2,
    "DisableMapExtensions": "",
    "RoundTimeExtension": 15,
    "MapsInCoolDown": 3,
    "CooldownCommands": ["cooldown", "cd"],
    "HideHudAfterVote": true,
    "RandomStartMap": false,
    "IncludeSpectator": true,
    "IncludeAFK": true,
    "AFKCheckInterval": 30,
    "EnableMapValidation": false,
    "SteamApiKey": "",
    "DiscordWebhook": ""
  }
}
```

</details>

## Map List

Maps live in `addons/counterstrikesharp/plugins/RockTheVote/maplist.txt`, one per line. Lines starting with `//` are ignored. Reload mid-game with `!reloadmaps`.

```
// Default game maps
de_mirage
de_dust2

// Workshop maps: name:workshopId
surf_beginner:3070321829
surf_rookie:3082548297

// Display names can include extra text
surf_nyx (Tier 1, Linear):3129698096
```

The map cooldown is written to `mapcooldown.txt` in the same folder so it survives restarts.

## Translations

English, French, Spanish, Ukrainian, Turkish, Latvian, Hungarian, Polish, Russian, Portuguese (BR), Chinese (Simplified), Chinese (Traditional).

Translated with Google Translate, ymmv. Pull requests welcome. Files are in [lang/](lang/).

## Roadmap

- [ ] Vote percentage required for the winning map (e.g. must receive 25% of the vote)
- [ ] Vote runoff (second round between the top 2 maps if no map hits the minimum percentage)

## Changelog

See [CHANGELOG.md](CHANGELOG.md).
