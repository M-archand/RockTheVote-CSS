# Changelog

## [2.4.0](https://github.com/M-archand/cs2-rockthevote/compare/v2.3.1...v2.4.0) (2026-09-08)


### Features

* **panorama:** add Panorama Custom HUD menu for the end-of-map vote, with click voting and configurable position, size, and color via the new `PanoramaMenu` config section ([4b55910](https://github.com/M-archand/cs2-rockthevote/commit/4b55910db1931f4280f6f42e1da51931966f1954)) ([8a4d7de](https://github.com/M-archand/cs2-rockthevote/commit/8a4d7de78c426689ea490984d094fdf1ea0b2b48))
* **panorama:** add an in-progress RTV toast that tracks vote progress on the HUD. `Rtv.EnablePanorama` is now split into `EnablePanoramaVote` and `EnablePanoramaToast` ([880e460](https://github.com/M-archand/cs2-rockthevote/commit/880e460b97d7cf4d6c2e6f6dbb2a5e810463a8d3))

<details>
<summary>Screenshots</summary>

**Panorama End of Map Vote menu**

![panoramamenu](https://github.com/user-attachments/assets/c207fbd4-a068-4071-982f-bd837edfe53d)

**Panorama RTV toast**

![rtvtoast](https://github.com/user-attachments/assets/7cec25c9-9c92-47af-887e-d08606b97e23)

</details>

* **config:** warn on startup about keys or sections missing from the local config ([e8c3b30](https://github.com/M-archand/cs2-rockthevote/commit/e8c3b30ac76f7d3bfed3848fa5692508f9fe29b5))
* **config:** command lists (`MapChooser.Command`, `General.CooldownCommands`) accept arrays or CSV strings, and the `css_` prefix is optional. Default cooldown aliases are now `cooldown` and `cd` ([6c0ddff](https://github.com/M-archand/cs2-rockthevote/commit/6c0ddff5a048b8ec929889d982d5902a2965a980))
* **config:** add `General.ExtendPermission` so the `!extend` permission is configurable instead of hardcoded to `@css/changemap` ([4c043d7](https://github.com/M-archand/cs2-rockthevote/commit/4c043d775e7ea1c928e854acf2b073c9691cdae0))
* **config:** add `General.ForceMapChange` so the end-of-map vote can be deferred to 00:00 when `mp_ignore_round_win_conditions` is set, with `VoteDuration` only clamped to the `TriggerSecondsBeforeEnd` window when it is enabled ([eac8bea](https://github.com/M-archand/cs2-rockthevote/commit/eac8bea2d57f547833b2b72952ec721127642b04))


### Bug Fixes

* **lifecycle:** clean up properly on unload and reload: cancel any in-progress panorama vote, restore convars, kill callbacks, timelimit, and votes, restart timers, and skip the random start map after a hot reload ([d56c148](https://github.com/M-archand/cs2-rockthevote/commit/d56c1485b5d158367f4b84d5c36f3ba331486ca1)) ([0da9301](https://github.com/M-archand/cs2-rockthevote/commit/0da9301c374888148983410ca11990c637e4c6a6)) ([e4538c6](https://github.com/M-archand/cs2-rockthevote/commit/e4538c62a9211434a0bac1b98f6b2096648c49f4))
* **panorama:** sync `PotentialVotes` when a player disconnects mid-vote, validate voter and vote controller before use, reset `ExtendTimeVoteHappening` when the extend vote fails to start, and keep a single toast refresh timer across panel respawns ([6267dd9](https://github.com/M-archand/cs2-rockthevote/commit/6267dd91c9f763a47f7b43d54fef7fac74a6299c)) ([13bf644](https://github.com/M-archand/cs2-rockthevote/commit/13bf644f5dfe4e7eeaed366609f397c829564633)) ([9dacdb9](https://github.com/M-archand/cs2-rockthevote/commit/9dacdb9f9ad81e7fa7daf69cfe9f0a3746e5d4a2)) ([0c68593](https://github.com/M-archand/cs2-rockthevote/commit/0c685937c1c3f7cec55da9cfef4fab0a03a81be8))
* **perf:** cache HUD strings so `OnTick` only re-localizes when text changes, and re-check slots each tick ([15c9fd9](https://github.com/M-archand/cs2-rockthevote/commit/15c9fd99caf5108ba6e0013eae0d31669cd895b7)) ([2e93839](https://github.com/M-archand/cs2-rockthevote/commit/2e938397d37c0254b6bcb186b7db6988bcfc2784))
* **config:** parse string config values (menu types, positions, colors, etc.) case-insensitively ([9802e43](https://github.com/M-archand/cs2-rockthevote/commit/9802e433f2194778963b4f01f2ed5cc5e5e893f7))
* skip the end-of-map vote when no maps are available ([56157b2](https://github.com/M-archand/cs2-rockthevote/commit/56157b2f89f3f3ec1bd31b665f8c43bd2bf640e9))


### Code Refactoring

* move plugin sources into `src/` and fix release paths ([b20b174](https://github.com/M-archand/cs2-rockthevote/commit/b20b17436c6a14a104ef01458118e16133b1ba4a))
* **logging:** prefix all log output with `[RTV.<Component>]` and gate only diagnostic messages behind `DebugLogging` ([372c5f6](https://github.com/M-archand/cs2-rockthevote/commit/372c5f642fc456cb8352c9a31ef36c0bf1294534))


### Config Changes (ConfigVersion 25 -> 26)

**New section**

* `PanoramaMenu` - shared settings for every menu drawn on the Panorama custom HUD
  * `AddonName` (`"panorama/layout/custom_game/rockthevote.xml"`)
  * `EnableClickVoting` (`true`)
  * `MapVoteMenuPosition` (`"CenterLeft"`), `MapVoteHeaderSize` (`"extralarge"`), `MapVoteHeaderColor` (`"orange"`), `MapVoteRowSize` (`"large"`), `MapVoteRowColor` (`"green"`)
  * `RtvPosition` (`"CenterRight"`), `RtvMapVoteHeaderSize` (`"large"`), `RtvMapVoteHeaderColor` (`"orange"`), `RtvColor` (`"white"`), `RtvSize` (`"normal"`)

**New keys**

* `Rtv.EnablePanoramaToast` (`false`) - show the in-progress RTV toast on the Panorama HUD
* `General.ForceMapChange` (`true`) - set to `false` with `mp_ignore_round_win_conditions 1` to defer the end-of-map vote to 00:00
* `General.ExtendPermission` (`"@css/admin,@css/changemap"`) - flags allowed to use `!extend`, previously hardcoded to `@css/changemap`

**Renamed keys**

* `Rtv.EnablePanorama` -> `Rtv.EnablePanoramaVote`

**Changed keys**

* `MapChooser.Command` and `General.CooldownCommands` now accept either a JSON array (`["mapmenu", "mm"]`) or a CSV string (`"mapmenu,mm"`), and the `css_` prefix is optional


### Miscellaneous

* **deps:** bump CounterStrikeSharp to API version 374 (required for the `CustomHudLayout` API) ([8a4d7de](https://github.com/M-archand/cs2-rockthevote/commit/8a4d7de78c426689ea490984d094fdf1ea0b2b48))

## [2.3.1](https://github.com/M-archand/cs2-rockthevote/compare/v2.3.0...v2.3.1) (2026-08-30)


### Bug Fixes

* add MatchEnd fallback poll so empty round-based servers change maps on timer expiry ([4df3fe3](https://github.com/M-archand/cs2-rockthevote/commit/4df3fe3ad194a4dcb52483a82401b73ee3dd4e8b))
* correct Spanish nomination localization keys (`nominate.nominated` / `nominate.already-nominated`) and remove zero-width spaces corrupting the zh-Hans `general.chat-countdown` key ([cb89f3d](https://github.com/M-archand/cs2-rockthevote/commit/cb89f3d1283ecdfd7010ba172af836eb01681540))
* populate `_nomConfig` from config in `OnTickDisplay` so `Nominate.MenuType = "HudMenu"` correctly registers the tick hook ([67ad3e3](https://github.com/M-archand/cs2-rockthevote/commit/67ad3e31e6d2ee160721ada09b80467255059eba))
* correct malformed structured-log call in `EndOfMapVote` config validation, now reports the original invalid `VoteDuration` with proper message template placeholders ([1264260](https://github.com/M-archand/cs2-rockthevote/commit/12642604752f411fb32c6c692b5e307e6b309833))


### Code Refactoring

* remove dead ScreenMenu code, ScreenMenu will not be returning (will add new panorama menu soontm) ([07b72e7](https://github.com/M-archand/cs2-rockthevote/commit/07b72e70ee80ea96ee2cebdbf518d25d7a3be6b7))
* extract shared `MapNameHelper.GetBaseName()` used by both cooldown and nomination base-map-name parsing ([19e25f3](https://github.com/M-archand/cs2-rockthevote/commit/19e25f3dfa42d613b3ef89b85ecd1005c492e92c))
* convert `PanoramaVote` from a static mutable singleton to an injected service with full `DependencyManager` lifecycle; vote recipient filter is now cleared on reset/cleanup ([7621a78](https://github.com/M-archand/cs2-rockthevote/commit/7621a787e9b08138f8b1cc025ed5209f0d922122))


### Miscellaneous

* **deps:** bump CounterStrikeSharp to API version 373 ([de22504](https://github.com/M-archand/cs2-rockthevote/commit/de225045280c2387aa08d09cc7528bb415bd3893))

## [2.3.0](https://github.com/M-archand/cs2-rockthevote/compare/v2.2.1...v2.3.0) (2026-08-23)


### Features

* **cooldown:** add !cooldown command that prints the maps currently on cooldown to the player's console, most recently played at the top. Command aliases are configurable via `General.CooldownCommands` (default `css_cooldown`), with a 10 second per-player command cooldown, a chat notice telling the player to check their console, and translations for all supported languages ([4747c8b](https://github.com/M-archand/cs2-rockthevote/commit/4747c8b664af46daf90bcc5ba32eb79f7ebabc8a))
