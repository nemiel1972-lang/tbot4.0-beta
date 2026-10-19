# Config template

`config.vorlage.json` is an anonymised template of `instance_settings.json` (no credentials, no real coordinates).

Usage: copy the file, rename it to `instance_settings.json` and fill in credentials and coordinates.
The real `instance_settings.json` never belongs in Git (see `.gitignore`).

New under `Brain.Transports` (both optional, off by default):
- `UseTransportChain` - Brain workers use the transport chain (CargoFallback, MixFleet, KeepOnOrigin).
- `CompactSlotPlan` - when too few slots are free, the plan is recalculated with fewer shipments.

## Taking an old config over (MigrateConfig)

`MigrateConfig.exe` (Linux: `MigrateConfig`) sits in the main folder of the packages. Run it in the folder of the old
`instance_settings.json` before the first start of a new version. It reads the old file(s), keeps all values, adds the
settings that are new in the template with their defaults, switches new workers off and reports what is no longer used
(e.g. `DeutPlanets`, the old `MaxT2Building`/`MaxBuilding6-12`). Before writing it checks that every old value is still in
the result, makes a backup in `backups/config-<time>/` and asks for confirmation (`--dry-run` only shows, `--yes` skips the
question). It does nothing for configs that already use `Load`. After it ran: restart the bot and check the config.
Safety net: the template starts with `"ConfigVersion": 4`. If the bot finds a config WITHOUT it that still has the old
lifeform fields (`MaxT2Building`, `MaxBuilding6-12`), it logs "OLD CONFIG DETECTED" and starts only the Defender until the
config is migrated (or `"ConfigVersion": 4` is added by hand). Configs without those old fields are never blocked.
It is built from `tools/MigrateConfig/` (own project, template and `lifeform_targets.json` are embedded).

## DiscordWebhook (global)

`DiscordWebhook` mirrors all Telegram messages as Discord embeds (colour depends on the event).
Send only; commands still go through Telegram. `Defender.Discord` (its own attack alert)
takes precedence; if that block is missing, the attack alert goes through the global webhook.

## Brain.AutoMine.ColonyOverrides (optional)

Individual planets (e.g. a deuterium planet) get their own max values without needing a complete
block of their own. Matching is by exact coordinate; only the fields that are set override the
Colony block, everything else stays at the Colony default:

    "ColonyOverrides": [
        { "Galaxy": 1, "System": 100, "Position": 8, "Type": "Planet",
          "MaxMetalMine": 30, "MaxDeuteriumSynthetizer": 38 }
    ]

Without an entry the Colony values apply. The HomeWorld is never overridden. The former blocks
`DeutPlanets`/`DeutPlanet` no longer exist; their values belong in an entry in `ColonyOverrides`.

## Split config: conf.d/ and secrets/ (Load)

Instead of one large `instance_settings.json`, every block can be moved into its own file.
The main file then contains only switches and references:

```json
"AutoFarm": { "Active": true, "Load": "AutoFarm" },
"General":  { "Load": ["General", "secrets/captcha"] }
```

- `"Load": "Name"` loads `conf.d/Name.json`. With a folder (`"secrets/proxy"`) the path is taken relative to the config folder.
- Several files (`["A", "B"]`) are merged in order, later ones win. Keys directly next to `Load` (e.g. `Active`) always win.
- Arrays are replaced, not merged. Absolute paths and `..` are forbidden.
- A missing file is an error. When the config is reloaded, the bot aborts the reload and keeps running with the old settings (log: `Settings reload aborted`).
- The watcher observes `conf.d/` and `secrets/`: changes there trigger a reload as before.
- If the bot writes values itself (origin, slot tiles, worker switches), they end up in the file where the value lives.

**Web UI access:** passwords, user names/e-mail (`Credentials`, `BasicAuth`, proxy login), the captcha API key and your own alliance ID are neither shown nor editable in the web UI (Config page); change them only on the console or by editing the files directly. The Config page's Messenger form is the one exception: it can show/edit the Telegram token and chat ID and the Discord webhook URL (hidden while "Hide data" is on).

`secrets/` (permissions 700, files 600) holds credentials: `credentials` (email, password, BasicAuth), `proxy`, `captcha`, `discord`, `defender-discord`, `alliance`, `telegram` (for `settings.json`).
In the WebUI these files are read-only (masked); changes are made via SSH.
ogamed receives credentials through environment variables, never on the command line.

### Moving another block to secrets/ afterwards

For an already split config (dry run without `--apply`, with cross-check and backup):

```bash
python3 tools/split-config.py --move "AutoFarm.AllianceWhitelist=secrets/alliance"
python3 tools/split-config.py --move "AutoFarm.AllianceWhitelist=secrets/alliance" --apply
```

`Active` stays in the block, all other fields move to `secrets/alliance.json`.

### Converting

```bash
cd /home/tbot/TBot4.0
python3 tools/split-config.py                  # dry run: plan + cross-check, writes nothing
sudo systemctl stop tbot
python3 tools/split-config.py --apply          # backup to /home/tbot/tbot/backups/config-<time>/
sudo systemctl start tbot
```

Before writing, the script merges the result back together and compares it with the original; on any difference nothing is written.
`build-all.sh` backs up `instance_settings.json`, `settings.json`, `conf.d/` and `secrets/` to `backups/build-<time>/` before every build.
To undo: copy the files back from the backup and remove `conf.d/` and `secrets/`.

## Built-in safeguards (no config needed)

- Transports respect the deuterium reserve (`Brain.Transports.DeutToLeaveOnMoons`, `AutoRepatriate.LeaveDeut`) including the flight fuel: payload + reserve + fuel must fit into the stock, otherwise the fleet is not sent.
- The home planet (lowest planet ID) is never treated as an abandon candidate (`AutoColonize.Abandon`), so AutoMine cannot deadlock on a fresh account.
- AutoCargo no longer fails when lifeform bonuses are not loaded yet.

## lifeform_targets.json

Target table for `Brain.LifeformAutoMine` (`TargetSlot`). It lives next to `instance_settings.json` (working directory). Without it, LF building is skipped with a warning. It is included in the Windows package.

## Windows package

`tools/build-windows.sh` builds TBot and ogamed for Windows 11 (x64) and packs `dist/TBot-win-x64.zip` (default: a single `TBot.exe`, `--multi-file` for a folder with DLLs) without config,
secrets and data. `--framework-dependent` makes the package smaller (needs the .NET 9 runtime). Config, `secrets/`
and optionally `data/` are placed next to `TBot.exe` on the Windows machine by hand (see `README.txt` in the package).

## Linux package

`tools/build-linux.sh` builds TBot and ogamed for Linux x64 (`--arm64` for e.g. a Raspberry Pi) and packs `dist/TBot-linux-x64.zip` (single `TBot` executable, `--multi-file` for a folder with DLLs, `--framework-dependent` needs the .NET 9 runtime). It contains no real config, secrets or data, but a pre-split starter config in `config/split/` (`instance_settings.json` with `Load` references, `conf.d/`, `secrets/`, `settings.json` - placeholders only, generated from the template with `split-config.py`), a `start.sh`, a `tbot.service.example`, the config template, `REFERENCE.md`, `lifeform_targets.json` and a `README.txt`. `build-all.sh` builds it too (skip with `NO_LINUX=1`).
