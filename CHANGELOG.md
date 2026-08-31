# Changelog

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
