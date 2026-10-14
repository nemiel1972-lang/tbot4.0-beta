#!/usr/bin/env bash
# Builds TBot + ogamed for Windows 11 (x64) and packs a package WITHOUT config, secrets and data.
# Run from the repo folder:  tools/build-windows.sh [--multi-file] [--framework-dependent]
#   Default: a single TBot.exe (single-file, self-contained) plus ogamed.exe
#            and the two native DLLs (e_sqlite3.dll, aspnetcorev2_inprocess.dll) next to it
#   --multi-file: normal folder with many DLLs instead of a single EXE
#   --framework-dependent: smaller, but needs the .NET 9 runtime on the Windows machine
# Result: dist/TBot-win-x64/ and dist/TBot-win-x64.zip (if "zip" is available)
set -euo pipefail
cd "$(dirname "$0")/.."

SELF_CONTAINED=true
SINGLE_FILE=true
for arg in "$@"; do
  case "$arg" in
    --framework-dependent) SELF_CONTAINED=false ;;
    --multi-file) SINGLE_FILE=false ;;
    *) echo "Unknown option: $arg"; exit 1 ;;
  esac
done

AUSGABE="dist/TBot-win-x64"
rm -rf "$AUSGABE"
mkdir -p "$AUSGABE"

echo "== ogamed.exe (Go, windows/amd64) =="
(cd ogamed && CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build -o "../$AUSGABE/ogamed.exe" ./cmd/ogamed-tbot)

echo "== TBot (.NET, win-x64, self-contained=$SELF_CONTAINED, single-file=$SINGLE_FILE) =="
(cd TBot && dotnet publish -c Release -r win-x64 --self-contained "$SELF_CONTAINED" \
  -p:PublishSingleFile="$SINGLE_FILE" -p:DebugType=none -p:DebugSymbols=false \
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
cp config/config.vorlage.json "$AUSGABE/config/"

# Field-by-field explanation of the config template
cp config/REFERENCE.md "$AUSGABE/config/REFERENCE.md"

# LF target table: the Lifeform AutoMine needs it next to the config (without it LF building is skipped)
cp config/lifeform_targets.json "$AUSGABE/lifeform_targets.json"

cat > "$AUSGABE/start.bat" <<'BAT'
@echo off
rem Starts TBot in the folder of this file (config, data and log live here).
cd /d "%~dp0"
TBot.exe
pause
BAT

cat > "$AUSGABE/README.txt" <<'TXT'
TBot for Windows
================
This package contains the bot (TBot.exe), the game connector (ogamed.exe), a start script and two templates.
It contains NO config, credentials or data - you create those yourself (steps below).

Folder contents
  TBot.exe, ogamed.exe     the bot and the connector (keep them in the same folder)
  start.bat                starts the bot in this folder
  lifeform_targets.json    target table for Lifeform AutoMine (keep it next to TBot.exe)
  config\config.vorlage.json   anonymised template for the instance config
  config\REFERENCE.md          explains every field of the template (start with "New account quick start")
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

Notes about this build
  - Lifeform AutoMine needs lifeform_targets.json. If it is missing, LF building is skipped (warning in the log).
  - Update: replace TBot.exe, ogamed.exe and the DLLs with the new package; keep your config, data and lifeform_targets.json
    (if you changed the target table, do not overwrite it).
TXT

# Windows-friendly line endings for the text files
sed -i 's/$/\r/' "$AUSGABE/README.txt" "$AUSGABE/start.bat"

if command -v zip >/dev/null 2>&1; then
  (cd dist && rm -f TBot-win-x64.zip && zip -qr TBot-win-x64.zip TBot-win-x64)
  echo "Package: dist/TBot-win-x64.zip"
else
  echo "zip not installed - folder is ready: $AUSGABE"
fi
echo "Done."
