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
