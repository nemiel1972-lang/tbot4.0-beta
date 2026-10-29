#!/usr/bin/env bash
# Builds TBot + ogamed for Linux (x64) and packs a package WITHOUT config, secrets and data.
# Run from the repo folder:  tools/build-linux.sh [--multi-file] [--framework-dependent] [--with-auction] [--arm64]
#   Default: a single TBot executable (single-file, self-contained) plus ogamed and the native libraries next to it
#   --multi-file: normal folder with many DLLs instead of a single executable
#   --framework-dependent: smaller, but needs the .NET 9 runtime on the target machine
#   --with-auction: include the auction feature (default for the package: without, -p:NoAuction=true)
#   --arm64: build for linux-arm64 (e.g. Raspberry Pi) instead of linux-x64
# Result: dist/TBot-linux-<arch>/ and dist/TBot-linux-<arch>.zip
set -euo pipefail
cd "$(dirname "$0")/.."

SELF_CONTAINED=true
SINGLE_FILE=true
NO_AUCTION=true
RID=linux-x64
GOARCH=amd64
for arg in "$@"; do
  case "$arg" in
    --framework-dependent) SELF_CONTAINED=false ;;
    --multi-file) SINGLE_FILE=false ;;
    --with-auction) NO_AUCTION=false ;;
    --arm64) RID=linux-arm64; GOARCH=arm64 ;;
    *) echo "Unknown option: $arg"; exit 1 ;;
  esac
done

NAME="TBot-$RID"
AUSGABE="dist/$NAME"
rm -rf "$AUSGABE"
mkdir -p "$AUSGABE"

echo "== ogamed (Go, linux/$GOARCH) =="
(cd ogamed && CGO_ENABLED=0 GOOS=linux GOARCH="$GOARCH" go build -o "../$AUSGABE/ogamed" ./cmd/ogamed-tbot)

# File/assembly version from the release tag (v4.0-beta16 -> 4.0.0.16), otherwise the value in TBot.csproj stays
VERSION_ARGS=""
if [[ "${TBOT_VERSION:-}" =~ ^v?([0-9]+)\.([0-9]+)-beta([0-9]+)$ ]]; then
  NUMVER="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.0.${BASH_REMATCH[3]}"
  VERSION_ARGS="-p:Version=$NUMVER -p:AssemblyVersion=$NUMVER -p:FileVersion=$NUMVER -p:InformationalVersion=$TBOT_VERSION"
fi

echo "== TBot (.NET, $RID, self-contained=$SELF_CONTAINED, single-file=$SINGLE_FILE, no-auction=$NO_AUCTION) =="
(cd TBot && dotnet publish -c Release -r "$RID" --self-contained "$SELF_CONTAINED" \
  -p:PublishSingleFile="$SINGLE_FILE" $VERSION_ARGS -p:NoAuction="$NO_AUCTION" -p:NoDiag=true -p:NoSlots=true -p:NoOrion=true -p:DebugType=none -p:DebugSymbols=false \
  -o "../$AUSGABE" -nologo -clp:"ErrorsOnly;Summary")
rm -f "$AUSGABE"/*.pdb

# Safety net: never put config, secrets, data, logs or backups into the package
for p in settings.json instance_settings.json conf.d secrets data log backups profiles; do
  if [ -e "$AUSGABE/$p" ]; then
    echo "Removing $p from the package"
    rm -rf "$AUSGABE/$p"
  fi
done
find "$AUSGABE" -maxdepth 1 \( -name '*settings*.json' -o -name '*.db' -o -name '*.bak' \) -print -delete

# Template (same handling as the Windows package: without the auction block unless --with-auction)
mkdir -p "$AUSGABE/config"
if [ "$NO_AUCTION" = true ]; then
  python3 - "config/config.vorlage.json" "$AUSGABE/config/config.vorlage.json" <<'PY'
import json, re, sys
src, dst = sys.argv[1], sys.argv[2]
t = open(src, encoding='utf-8-sig', newline='').read()
m = re.search(r'[ \t]*"AutoBidAuction"\s*:\s*\{', t)
if m:
    i, depth, instr = m.end(), 1, False
    while depth:
        c = t[i]
        if instr:
            if c == '\\': i += 1
            elif c == '"': instr = False
        elif c == '"': instr = True
        elif c == '{': depth += 1
        elif c == '}': depth -= 1
        i += 1
    rest = re.match(r'\s*,?', t[i:]).end()
    t = t[:m.start()] + t[i + rest:].lstrip('\r\n')
    t = re.sub(r',(\s*\})', r'\1', t)
json.loads(t)
open(dst, 'w', encoding='utf-8', newline='').write(t)
PY
else
  cp config/config.vorlage.json "$AUSGABE/config/"
fi
cp config/REFERENCE.md "$AUSGABE/config/REFERENCE.md"
# Kommentierte Referenz (alle Eintraege mit ##-Erklaerung, nicht lauffaehig), deutsch und englisch
cp config/instance_settings_kommentiert_de.txt "$AUSGABE/config/instance_settings_kommentiert_de.txt"
cp config/instance_settings_kommentiert_en.txt "$AUSGABE/config/instance_settings_kommentiert_en.txt"
cp config/lifeform_targets.json "$AUSGABE/lifeform_targets.json"

# Standalone helper that takes an OLD instance_settings.json over into the new template (own EXE, embedded template).
# A failure here never stops the package build.
echo "== MigrateConfig ("$RID") =="
(cd tools/MigrateConfig && dotnet publish -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true \
  -p:TemplatePath="$OLDPWD/$AUSGABE/config/config.vorlage.json" -p:DebugType=none -p:DebugSymbols=false \
  -o "$OLDPWD/$AUSGABE" -nologo -clp:"ErrorsOnly;Summary") || echo "WARNING: MigrateConfig build failed (package is built without it)."

# Standalone plausibility check of the config (read-only, same rules as the start page). A failure here never stops the package build.
echo "== ConfigDoctor ("$RID") =="
(cd tools/ConfigDoctor && dotnet publish -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true \
  -p:DebugType=none -p:DebugSymbols=false \
  -o "$OLDPWD/$AUSGABE" -nologo -clp:"ErrorsOnly;Summary") || echo "WARNING: ConfigDoctor build failed (package is built without it)."

# Helper for "Install update" in the WebUI (unpacks the zip, replaces files, restarts or leaves the restart to systemd)
echo "== TBotUpdater ($RID) =="
(cd tools/TBotUpdater && dotnet publish -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true \
  -p:DebugType=none -p:DebugSymbols=false -o "$OLDPWD/$AUSGABE" -nologo -clp:"ErrorsOnly;Summary") || echo "WARNING: TBotUpdater build failed (package is built without the update button)."
rm -f "$AUSGABE"/*.pdb

# Version for the update check on the start page: the release tag when built by the release workflow (TBOT_VERSION), otherwise "dev"
printf '%s' "${TBOT_VERSION:-dev}" > "$AUSGABE/version.txt"

# Pre-split starter config (instance_settings.json with "Load" references + conf.d/ + secrets/), built from the
# template with tools/split-config.py. Contains placeholders only; the recipient copies config/split/* next to TBot.
SPLIT_TMP="$(mktemp -d)"
cp "$AUSGABE/config/config.vorlage.json" "$SPLIT_TMP/instance_settings.json"
cat > "$SPLIT_TMP/settings.json" <<'JSON'
{
  "Instances": [ { "Settings": "instance_settings.json", "Alias": "MAIN" } ],
  "TelegramMessenger": { "Active": false, "API": "<bot token>", "ChatId": "<chat id>" },
  "WebUI": { "Enable": true, "Urls": "http://localhost:5000" }
}
JSON
python3 tools/split-config.py --dir "$SPLIT_TMP" --apply >/dev/null
rm -rf "$SPLIT_TMP/backups"
mkdir -p "$AUSGABE/config/split"
cp -a "$SPLIT_TMP/instance_settings.json" "$SPLIT_TMP/settings.json" "$SPLIT_TMP/conf.d" "$SPLIT_TMP/secrets" "$AUSGABE/config/split/"
chmod 700 "$AUSGABE/config/split/secrets"; chmod 600 "$AUSGABE/config/split/secrets/"*.json
rm -rf "$SPLIT_TMP"

cat > "$AUSGABE/clean_config.sh" <<'SH'
#!/usr/bin/env bash
# Removes config entries that no longer exist in this version (list: removed_keys.txt inside MigrateConfig).
# Shows what it will remove, asks, and makes a backup in backups/config-clean-<time> first. Options: --dry-run, --yes
cd "$(dirname "$0")"
exec ./MigrateConfig --clean "$@"
SH

cat > "$AUSGABE/start.sh" <<'SH'
#!/usr/bin/env bash
# Starts TBot in the folder of this file (config, data and log live here).
cd "$(dirname "$0")"
exec ./TBot
SH

cat > "$AUSGABE/tbot.service.example" <<'UNIT'
# Optional: run the bot as a systemd service.
# 1. Copy this file to /etc/systemd/system/tbot.service and replace USER and the path.
# 2. sudo systemctl daemon-reload && sudo systemctl enable --now tbot
[Unit]
Description=TBot
After=network-online.target
Wants=network-online.target

[Service]
User=USER
WorkingDirectory=/home/USER/tbot
ExecStart=/home/USER/tbot/TBot
Restart=on-failure
RestartSec=15
# Wichtig fuer "Install update": ohne dies beendet systemd beim Stoppen des Bots auch den Updater
KillMode=process

[Install]
WantedBy=multi-user.target
UNIT

cat > "$AUSGABE/README.txt" <<'TXT'
TBot for Linux
==============
This package contains the bot (TBot), the game connector (ogamed), a start script and two templates.
It contains NO config, credentials or data - you create those yourself (steps below).

Folder contents
  TBot, ogamed             the bot and the connector (keep them in the same folder)
  start.sh                 starts the bot in this folder
  clean_config.sh          removes config entries that no longer exist (asks first, makes a backup)
  tbot.service.example     optional systemd unit
  lifeform_targets.json    target table for Lifeform AutoMine (keep it next to TBot)
  TBotUpdater              helper for "Install update" in the WebUI (do not start it by hand)
  MigrateConfig            takes an old config over into the new template (see "Updating from an older version")
  ConfigDoctor             checks the config for implausible values (read-only; exit code 0 = ok, 1 = warnings, 2 = errors)
  config/config.vorlage.json   anonymised template as ONE file (alternative)
  config/split/                the same template pre-split: instance_settings.json, settings.json, conf.d/, secrets/
  config/REFERENCE.md          explains every field of the template (start with "New account quick start")
  config/instance_settings_kommentiert_en.txt   every setting with a ## explanation, English (reading aid, NOT a working config)
  config/instance_settings_kommentiert_de.txt   the same in German
  (the native libraries next to TBot, e.g. libe_sqlite3.so, belong to the bot - do not delete them)

First start (recommended: split config - secrets are kept in their own files)
  0. Unpack:  unzip TBot-linux-*.zip && cd TBot-linux-*   and read config/REFERENCE.md.
  1. cp -a config/split/. .      (gives you instance_settings.json, settings.json, conf.d/ and secrets/)
  2. Enter your account data in secrets/credentials.json (email, password) and, if used, secrets/telegram.json,
     secrets/discord.json, secrets/proxy.json, secrets/captcha.json. Set your universe, language and your own
     coordinates in conf.d/General.json and the other conf.d/*.json files. Switch workers on in instance_settings.json
     ("Active": true) only when you want them. Every block is documented in config/REFERENCE.md.
  3. chmod 700 secrets; chmod 600 secrets/*.json instance_settings.json settings.json
  4. ./start.sh   (for permanent operation use tbot.service.example or a tmux/screen session)
  5. Open the WebUI address from settings.json (WebUI -> Urls) in your browser.

Single-file alternative
  cp config/config.vorlage.json instance_settings.json, fill everything in that one file and create settings.json:
       { "Instances": [ { "Settings": "instance_settings.json", "Alias": "MAIN" } ],
         "WebUI": { "Enable": true, "Urls": "http://localhost:5000" } }

Run as a systemd service (recommended for permanent operation; needed for automatic start after a reboot)
  The file tbot.service.example in this folder is a ready unit. Replace USER and the folder (here /home/USER/tbot) and install it:
       sudo cp tbot.service.example /etc/systemd/system/tbot.service
       sudo nano /etc/systemd/system/tbot.service      # set User=, WorkingDirectory= and ExecStart= to your user and folder
       sudo chown -R USER:USER /path/to/your/tbot       # the user must be able to write in the bot folder (config, data, updates)
       sudo systemctl daemon-reload
       sudo systemctl enable --now tbot
  Status: systemctl status tbot     Log: journalctl -u tbot -f     Restart: sudo systemctl restart tbot
  Stop a bot that runs in screen/tmux first, otherwise two instances run.
  KEEP the line "KillMode=process" in the unit: without it systemd also stops the updater when the bot stops,
  and "Install update" in the WebUI fails (the bot stops and nothing is installed). Do not run the bot as root.
  Without systemd (screen/tmux): start it with ./start.sh in a screen or tmux session instead.

Files the bot creates by itself (next to TBot)
  data/  state files (ledger, farm state, pending recalls ...)
  log/   log files

Keeping secrets safe
  - Never share instance_settings.json, settings.json or the secrets folder; they contain your password and tokens.
  - Optional: split passwords out into a secrets/ folder (chmod 700, files 600) with "Load" references
    (see config/README.md in the repo and tools/split-config.py, needs Python).
  - Do not put coordinates or credentials into screenshots or chats.

Updating from an older version or from the original TBot
  Run MigrateConfig (it sits next to the bot) in the folder of your OLD config - ideally BEFORE the first start of the new version.
  It finds instance_settings.json (and the instances listed in settings.json), shows what it will change and asks
  before it writes anything. It makes a backup in backups/config-<time>, keeps ALL your old values, adds the new
  settings with default values and switches every new worker OFF. Options: --dry-run (only show), --yes (no question).
  Afterwards: restart the bot and CHECK the config - especially Lifeform AutoMine (see lifeform_targets.json).
  Old entries that no longer exist stay in your file (and are listed as "not used by this version"). Run ./clean_config.sh when you want
  them removed: it shows the list, asks, and makes a backup first. Only entries from its built-in list are removed, never your own ones.

Notes about this build
  - The auction feature is not included in this package.
  - Lifeform AutoMine needs lifeform_targets.json. If it is missing, LF building is skipped (warning in the log).
  - Update: replace TBot, ogamed and the native libraries with the new package; keep your config, data and
    lifeform_targets.json.
TXT

chmod +x "$AUSGABE/start.sh" "$AUSGABE/clean_config.sh" "$AUSGABE/ogamed"
[ -f "$AUSGABE/TBot" ] && chmod +x "$AUSGABE/TBot"
[ -f "$AUSGABE/ConfigDoctor" ] && chmod +x "$AUSGABE/ConfigDoctor"

# zip keeps the Unix executable bits; plain "zip" is needed (apt install zip)
(cd dist && rm -f "$NAME.zip" && zip -qr "$NAME.zip" "$NAME")
echo "Package: dist/$NAME.zip"
echo "Done."
