# Notice / Credits

This project builds on the work of others. Thank you to everyone listed here.

## ogamed / ogame (Go)
The Go part in `ogamed/` is a modified fork of [alaingilbert/ogame](https://github.com/alaingilbert/ogame) and the `ogamed` daemon by **Alain Gilbert**,
licensed under the MIT License (see `ogamed/LICENSE`, Copyright (c) 2018 Alain Gilbert).
The upstream project is no longer actively maintained; it is the foundation that makes TBot possible and is credited here on purpose.
Local changes (extra endpoints, fixes) are in `ogamed/cmd/ogamed-tbot/` and `ogamed/pkg/`.

## TBot (C#)
The C# bot is a derivative of the original TBot (ogame-tbot/TBot) and of the fork by ronnie32 ("classic runtime with OGame v13 fixes").
The original authors keep their copyright. Their repositories do not state a license; permission to publish has been requested.

## Config UI
The Config web UI (files listed in `LICENSE-UI.md`) is original work and is **All Rights Reserved** - view only, no copying or modification without permission.

## Third-party data
`config/lifeform_targets.json` contains slot tables from a third-party source (not verified against the game). Check the rights before publishing.

## Included libraries
jQuery, jQuery Validation, Bootstrap (see their LICENSE files under `TBot.WebUI/wwwroot/lib/`).

## Game content, icons and texts (OGame)
OGame is a trademark and game of **GameForge** (Gameforge 4D GmbH). TBot is an unofficial, independent tool and is not affiliated with or endorsed by GameForge.
Names, icons and images of the game (buildings, research, ships, lifeforms) remain the property of GameForge.

Some lifeform images/icons (`TBot.WebUI/wwwroot/img/lf/`) and some descriptive texts originate from the **OGame Wiki on Fandom** (https://ogame.fandom.com).
Text content there is available under **CC BY-SA 3.0** (https://creativecommons.org/licenses/by-sa/3.0/); the license applies per page/file, and images may carry a different license or a fair-use claim.
Material taken from there is credited here: source = OGame Wiki (Fandom) contributors, license = CC BY-SA 3.0, changes = resized/converted for use in the web interface.
Material derived from CC BY-SA sources stays under CC BY-SA.
If you are a rights holder and want something changed or removed, please open an issue; it will be handled promptly.
