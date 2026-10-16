#!/usr/bin/env python3
"""Erzeugt einen Fake-Update-Feed zum Testen der Update-Funktion im WebUI (ohne GitHub).

  python3 make_fake_feed.py <Ausgabeordner> [--tag v4.0-beta99-fake] [--from-dir <Bot-Ordner>] [--broken-checksum]

Ergebnis im Ausgabeordner: releases.json (GitHub-Format), TBot-win-x64.zip und SHA256SUMS.txt.
Im WebUI einstellen (settings.json):   "WebUI": { ..., "Update": { "FeedUrl": "<Ausgabeordner>", "AssetPattern": "win-x64" } }

Ohne --from-dir enthaelt das Paket nur eine Platzhalter-TBot.exe: die Installation laeuft durch, der Neustart schlaegt aber fehl und
der Updater spielt den alten Stand zurueck (testet also das Rollback).
Mit --from-dir <Bot-Ordner> enthaelt das Paket eine Kopie der dortigen Programmdateien (ohne Config, Daten, Logs) mit neuer
version.txt: das Update laeuft komplett durch und der Bot startet danach normal.
--broken-checksum schreibt eine falsche Summe in SHA256SUMS.txt: die Installation muss mit Pruefsummenfehler abbrechen.
"""
import argparse, hashlib, json, os, sys, zipfile, datetime

SKIP_DIRS = {"data", "log", "logs", "backups", "profiles", "secrets", "conf.d", "update"}
SKIP_NAMES = {"lifeform_targets.json"}

def skip(rel):
    l = rel.lower().replace("\\", "/")
    if l.split("/")[0] in SKIP_DIRS or "/conf.d/" in l or "/secrets/" in l:
        return True
    n = os.path.basename(l)
    return n in SKIP_NAMES or ("settings" in n and n.endswith(".json")) or n.endswith((".bak", ".db", ".db-wal", ".db-shm", ".part", ".log"))

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--tag", default="v4.0-beta99-fake")
    ap.add_argument("--from-dir")
    ap.add_argument("--broken-checksum", action="store_true")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    zpath = os.path.join(a.out, "TBot-win-x64.zip")
    top = "TBot-win-x64/"
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr(top + "version.txt", a.tag)
        z.writestr(top + "UPDATE-TEST.txt", "Fake update %s, created %s\n" % (a.tag, datetime.datetime.now().isoformat(timespec="seconds")))
        if a.from_dir:
            n = 0
            for root, _, files in os.walk(a.from_dir):
                for f in files:
                    full = os.path.join(root, f)
                    rel = os.path.relpath(full, a.from_dir)
                    if skip(rel) or rel in ("version.txt", "UPDATE-TEST.txt"):
                        continue
                    z.write(full, top + rel.replace("\\", "/")); n += 1
            print("%d Dateien aus %s uebernommen" % (n, a.from_dir))
        else:
            z.writestr(top + "TBot.exe", "PLACEHOLDER - kein startbares Programm (Fake-Update)\n")
    digest = hashlib.sha256(open(zpath, "rb").read()).hexdigest()
    if a.broken_checksum:
        digest = ("0" if digest[0] != "0" else "1") + digest[1:]
    open(os.path.join(a.out, "SHA256SUMS.txt"), "w").write("%s  TBot-win-x64.zip\n" % digest)
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    releases = [{
        "tag_name": a.tag, "name": "Fake update " + a.tag, "draft": False, "prerelease": True, "published_at": now,
        "html_url": "https://example.invalid/fake-release",
        "body": "This is a FAKE release for testing the update function.\n\n- New file UPDATE-TEST.txt\n- version.txt now says " + a.tag + "\n- Your config and data stay untouched",
        "assets": [
            {"name": "TBot-win-x64.zip", "browser_download_url": "TBot-win-x64.zip", "size": os.path.getsize(zpath)},
            {"name": "SHA256SUMS.txt", "browser_download_url": "SHA256SUMS.txt"},
        ],
    }]
    json.dump(releases, open(os.path.join(a.out, "releases.json"), "w"), indent=2)
    print("Feed fertig in", os.path.abspath(a.out))

if __name__ == "__main__":
    sys.exit(main())
