#!/usr/bin/env bash
# Builds ogamed (Go) and TBot (.NET) from this repo, restarts the service and then builds the Windows 11 package.
# Run on the server:  cd /home/tbot/TBot4.0 && ./build-all.sh
# Skip the Windows package:  NO_WIN=1 ./build-all.sh   (extra args for the Windows build: WIN_ARGS)
# Skip the Linux package:    NO_LINUX=1 ./build-all.sh   (extra args: LINUX_ARGS="--arm64")
set -euo pipefail
cd "$(dirname "$0")"
ZIEL=/home/tbot/tbot

echo "== Building ogamed (Go) =="
(cd ogamed && go build -o "$ZIEL/ogamed.neu" ./cmd/ogamed-tbot)

# Back up config: publish writes into the same folder (backup + restore if something is missing)
SICHERUNG="$ZIEL/backups/build-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$SICHERUNG"
chmod 700 "$ZIEL/backups" "$SICHERUNG"
for f in instance_settings.json settings.json; do
  if [ -f "$ZIEL/$f" ]; then cp -p "$ZIEL/$f" "$SICHERUNG/$f"; fi
done
for d in conf.d secrets; do
  if [ -d "$ZIEL/$d" ]; then cp -a "$ZIEL/$d" "$SICHERUNG/$d"; fi
done
# keep only the last 10 backups
ls -1dt "$ZIEL"/backups/build-* 2>/dev/null | tail -n +11 | xargs -r rm -rf

echo "== Building TBot (.NET) =="
(cd TBot && dotnet publish -c Release -o "$ZIEL" -nologo -clp:"ErrorsOnly;Summary")

# Restore missing config files from the backup (existing ones stay unchanged)
for f in instance_settings.json settings.json; do
  if [ ! -f "$ZIEL/$f" ] && [ -f "$SICHERUNG/$f" ]; then cp -p "$SICHERUNG/$f" "$ZIEL/$f"; echo "Restored: $f"; fi
done
for d in conf.d secrets; do
  if [ ! -d "$ZIEL/$d" ] && [ -d "$SICHERUNG/$d" ]; then cp -a "$SICHERUNG/$d" "$ZIEL/$d"; echo "Restored: $d/"; fi
done

# Swap the binary only after a successful build
mv -f "$ZIEL/ogamed.neu" "$ZIEL/ogamed"
chmod +x "$ZIEL/ogamed"

echo "== Restarting service =="
sudo systemctl restart tbot
echo "Server build done."

# Windows 11 package (after the restart, so a failure here never affects the running server)
if [ "${NO_WIN:-0}" != "1" ]; then
  echo "== Building Windows 11 package =="
  # shellcheck disable=SC2086
  if tools/build-windows.sh ${WIN_ARGS:-}; then
    echo "Windows package: $(pwd)/dist/TBot-win-x64.zip"
  else
    echo "WARNING: Windows build failed (server is unaffected)."
  fi
fi

# Linux package for sharing (same rules: a failure never affects the running server)
if [ "${NO_LINUX:-0}" != "1" ]; then
  echo "== Building Linux package =="
  # shellcheck disable=SC2086
  if tools/build-linux.sh ${LINUX_ARGS:-}; then
    echo "Linux package: $(pwd)/dist/ (TBot-linux-*.tar.gz)"
  else
    echo "WARNING: Linux build failed (server is unaffected)."
  fi
fi
echo "Done."
