#!/usr/bin/env bash
# Builds TBot + ogamed for Windows 11 (x64) and packs a package WITHOUT config, secrets and data.
# Run from the repo folder:  tools/build-windows.sh [--multi-file] [--framework-dependent]
#   Default: a single TBot.exe (single-file, self-contained) plus ogamed.exe
#            and the two native DLLs (e_sqlite3.dll, aspnetcorev2_inprocess.dll) next to it
#   --multi-file: normal folder with many DLLs instead of a single EXE
#   --framework-dependent: smaller, but needs the .NET 9 runtime on the Windows machine
#   --with-auction: include the auction feature (default for the Windows package: without, -p:NoAuction=true)
# Result: dist/TBot-win-x64/ and dist/TBot-win-x64.zip (if "zip" is available)
set -euo pipefail
cd "$(dirname "$0")/.."

SELF_CONTAINED=true
SINGLE_FILE=true
NO_AUCTION=true
for arg in "$@"; do
  case "$arg" in
    --framework-dependent) SELF_CONTAINED=false ;;
    --multi-file) SINGLE_FILE=false ;;
    --with-auction) NO_AUCTION=false ;;
    *) echo "Unknown option: $arg"; exit 1 ;;
  esac
done

AUSGABE="dist/TBot-win-x64"
rm -rf "$AUSGABE"
mkdir -p "$AUSGABE"

echo "== ogamed.exe (Go, windows/amd64) =="
(cd ogamed && CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build -o "../$AUSGABE/ogamed.exe" ./cmd/ogamed-tbot)

# File/assembly version from the release tag (v4.0-beta16 -> 4.0.0.16, v4.0-beta16.1 -> also 4.0.0.16, the tag stays in InformationalVersion), otherwise the value in TBot.csproj stays
VERSION_ARGS=""
if [[ "${TBOT_VERSION:-}" =~ ^v?([0-9]+)\.([0-9]+)-beta([0-9]+)(\.[0-9]+)?$ ]]; then
  NUMVER="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.0.${BASH_REMATCH[3]}"
  VERSION_ARGS="-p:Version=$NUMVER -p:AssemblyVersion=$NUMVER -p:FileVersion=$NUMVER -p:InformationalVersion=$TBOT_VERSION"
fi

echo "== TBot (.NET, win-x64, self-contained=$SELF_CONTAINED, single-file=$SINGLE_FILE, no-auction=$NO_AUCTION) =="
(cd TBot && dotnet publish -c Release -r win-x64 --self-contained "$SELF_CONTAINED" \
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

# Add template and start helpers (only anonymised files from the repo)
mkdir -p "$AUSGABE/config"
if [ "$NO_AUCTION" = true ]; then
  # Remove the "AutoBidAuction" block from the template (count braces, validate result as JSON)
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
    t = re.sub(r',(\s*\})', r'\1', t)  # comma before closing brace if the block was the last one
json.loads(t)
open(dst, 'w', encoding='utf-8', newline='').write(t)
PY
else
  cp config/config.vorlage.json "$AUSGABE/config/"
fi

# Field-by-field explanation of the config template
cp config/REFERENCE.md "$AUSGABE/config/REFERENCE.md"
# Kommentierte Referenz (alle Eintraege mit ##-Erklaerung, nicht lauffaehig), deutsch und englisch
cp config/instance_settings_kommentiert_de.txt "$AUSGABE/config/instance_settings_kommentiert_de.txt"
cp config/instance_settings_kommentiert_en.txt "$AUSGABE/config/instance_settings_kommentiert_en.txt"

# LF target table: the Lifeform AutoMine needs it next to the config (without it LF building is skipped)
cp config/lifeform_targets.json "$AUSGABE/lifeform_targets.json"

# Standalone helper that takes an OLD instance_settings.json over into the new template (own EXE, embedded template).
# A failure here never stops the package build.
echo "== MigrateConfig (win-x64) =="
(cd tools/MigrateConfig && dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true \
  -p:TemplatePath="$OLDPWD/$AUSGABE/config/config.vorlage.json" -p:DebugType=none -p:DebugSymbols=false \
  -o "$OLDPWD/$AUSGABE" -nologo -clp:"ErrorsOnly;Summary") || echo "WARNING: MigrateConfig build failed (package is built without it)."

# Standalone plausibility check of the config (read-only, same rules as the start page). A failure here never stops the package build.
echo "== ConfigDoctor (win-x64) =="
(cd tools/ConfigDoctor && dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true \
  -p:DebugType=none -p:DebugSymbols=false \
  -o "$OLDPWD/$AUSGABE" -nologo -clp:"ErrorsOnly;Summary") || echo "WARNING: ConfigDoctor build failed (package is built without it)."
rm -f "$AUSGABE"/*.pdb

# Update helper: the "Install update" button on the WebUI start page starts it. It replaces the files after the bot has stopped
# (config, data, logs and logins stay), runs MigrateConfig and restarts the bot; if the new version does not start it restores the old one.
echo "== TBotUpdater (win-x64) =="
(cd tools/TBotUpdater && dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true \
  -p:DebugType=none -p:DebugSymbols=false -o "$OLDPWD/$AUSGABE" -nologo -clp:"ErrorsOnly;Summary") || echo "WARNING: TBotUpdater build failed (package is built without the update button)."
rm -f "$AUSGABE"/*.pdb

# Version for the update check on the start page: the release tag when built by the release workflow (TBOT_VERSION), otherwise "dev"
printf '%s' "${TBOT_VERSION:-dev}" > "$AUSGABE/version.txt"

cat > "$AUSGABE/start.bat" <<'BAT'
@echo off
rem Starts TBot in the folder of this file (config, data and log live here).
cd /d "%~dp0"
TBot.exe
pause
BAT

cat > "$AUSGABE/clean_config.bat" <<'BAT'
@echo off
rem Removes config entries that no longer exist in this version. Shows the list, asks, makes a backup first.
cd /d "%~dp0"
MigrateConfig.exe --clean %*
BAT

cat > "$AUSGABE/README.txt" <<'TXT'
TBot for Windows
================
This package contains the bot (TBot.exe), the game connector (ogamed.exe), a start script and two templates.
It contains NO config, credentials or data - you create those yourself (steps below).

Folder contents
  TBot.exe, ogamed.exe     the bot and the connector (keep them in the same folder)
  start.bat                starts the bot in this folder
  clean_config.bat         removes config entries that no longer exist (asks first, makes a backup)
  lifeform_targets.json    target table for Lifeform AutoMine (keep it next to TBot.exe)
  MigrateConfig.exe        takes an old config over into the new template (see "Updating from an older version")
  ConfigDoctor.exe         checks the config for implausible values (read-only; exit code 0 = ok, 1 = warnings, 2 = errors)
  TBotUpdater.exe          helper for "Install update" in the WebUI (do not start it by hand)
  version.txt              the installed version (used by the update check)
  config\config.vorlage.json   anonymised template for the instance config
  config\REFERENCE.md          explains every field of the template (start with "New account quick start")
  config\instance_settings_kommentiert_en.txt   every setting with a ## explanation, English (reading aid, NOT a working config)
  config\instance_settings_kommentiert_de.txt   the same in German
  (the two native DLLs next to TBot.exe belong to the bot, do not delete them)

First start
  0. Read config\REFERENCE.md (field-by-field explanation, incl. a quick start for new accounts).
  1. Copy config\config.vorlage.json to the folder of TBot.exe and rename it to instance_settings.json.
     Enter your account data (email, password, universe, language) and your own coordinates.
     Remove or switch off (Active: false) every worker you do not want.
  2. Create settings.json next to TBot.exe, for example:
       {
         "Instances": [ { "Settings": "instance_settings.json", "Alias": "MAIN" } ],
         "TelegramMessenger": { "Active": false, "API": "<bot token>", "ChatId": "<chat id>" },
         "WebUI": { "Enable": true, "Urls": "http://localhost:5000" }
       }
     "Settings" is the path of the instance config relative to settings.json. Telegram is optional.
  3. Double-click start.bat. The window stays open after the bot stops so you can read the last messages.
  4. Open the WebUI address from settings.json (WebUI -> Urls) in your browser.

Files the bot creates by itself (next to TBot.exe)
  data\   state files (ledger, farm state, pending recalls ...)
  log\    log files
  backups\ only if you use the config tools

Keeping secrets safe
  - Never share instance_settings.json, settings.json or the secrets folder; they contain your password and tokens.
  - Optional: split passwords out into a secrets\ folder with "Load" references (see the repo: config\README.md and
     tools\split-config.py, needs Python). The secrets folder should be readable only by your Windows user
     (right-click > Properties > Security).
  - Do not put coordinates or credentials into screenshots or chats.

Moving an existing bot to this PC
  Copy the data\ folder from the old machine to the folder of TBot.exe. Never run the same account on two machines
  at the same time. Without data\ the bot starts with an empty state.

Updating from an older version or from the original TBot
  Run MigrateConfig.exe (it sits next to the bot) in the folder of your OLD config - ideally BEFORE the first start of the new version.
  It finds instance_settings.json (and the instances listed in settings.json), shows what it will change and asks
  before it writes anything. It makes a backup in backups\config-<time>, keeps ALL your old values, adds the new
  settings with default values and switches every new worker OFF. Options: --dry-run (only show), --yes (no question).
  Afterwards: restart the bot and CHECK the config - especially Lifeform AutoMine (see lifeform_targets.json).
  Old entries that no longer exist stay in your file (and are listed as "not used by this version"). Run clean_config.bat when you want
  them removed: it shows the list, asks, and makes a backup first. Only entries from its built-in list are removed, never your own ones.

Notes about this build
  - The auction feature is not included in this package.
  - Lifeform AutoMine needs lifeform_targets.json. If it is missing, LF building is skipped (warning in the log).
  - Update from the WebUI: open the Config page, "Start" (first entry), card "Version". If a new release exists, click
    "Install update". The bot stops, the files are replaced (config, data, logs, logins and lifeform_targets.json stay), the config is
    checked by MigrateConfig and the bot restarts. If the new version does not start, the old one is restored automatically.
    The old files stay in backups\update-<time> (the last three are kept). Details: update\updater.log
  - Manual update: replace TBot.exe, ogamed.exe and the DLLs with the new package; keep your config, data and lifeform_targets.json
    (if you changed the target table, do not overwrite it).
TXT

# Windows-friendly line endings for the text files
sed -i 's/$/\r/' "$AUSGABE/README.txt" "$AUSGABE/start.bat" "$AUSGABE/clean_config.bat"

if command -v zip >/dev/null 2>&1; then
  (cd dist && rm -f TBot-win-x64.zip && zip -qr TBot-win-x64.zip TBot-win-x64)
  echo "Package: dist/TBot-win-x64.zip"
else
  echo "zip not installed - folder is ready: $AUSGABE"
fi
echo "Done."
