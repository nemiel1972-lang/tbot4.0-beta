#!/usr/bin/env python3
"""Splits instance_settings.json (and secrets from settings.json) into individual files.

  conf.d/<Block>.json   settings per worker/block
  secrets/<name>.json   credentials (password, proxy, webhooks, telegram), permissions 700/600

The main files keep only switches ("Active") and references ("Load").
Without --apply it only checks and shows the plan (nothing is changed).
Before writing, the result is merged back in a temp folder and compared with the original;
it is written only if they are identical. A backup is kept in backups/.
Secret values are never printed.
"""
import argparse
import copy
import json
import os
import shutil
import sys
import tempfile
import time
from collections import OrderedDict

DEFAULT_DIR = "conf.d"
SECRETS_DIR = "secrets"

# (Pfad zum Block, Schluessel oder None = alle ausser Active/Load, Zieldatei)
RULES_INSTANCE = [
    ("Credentials", ["Email", "Password", "BasicAuth"], "secrets/credentials"),
    ("General.Proxy", None, "secrets/proxy"),
    ("General", ["CaptchaAPIKey"], "secrets/captcha"),
    ("DiscordWebhook", None, "secrets/discord"),
    ("Defender.Discord", ["WebhookUrl"], "secrets/defender-discord"),
    ]
RULES_GLOBAL = [
    ("TelegramMessenger", ["API", "ChatId"], "secrets/telegram"),
]


def load_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f, object_pairs_hook=OrderedDict)


def dump_json(obj):
    return json.dumps(obj, indent=2, ensure_ascii=False) + "\n"


# ---- Nachbau der Load-Aufloesung aus SettingsService.cs (zur Gegenprobe) ----

def load_refs(block):
    if "Load" not in block:
        return None
    v = block["Load"]
    if isinstance(v, str):
        return [v]
    if isinstance(v, list) and all(isinstance(x, str) for x in v):
        return list(v)
    return None


def ref_path(base, ref):
    p = ref.strip().replace("\\", "/")
    if not p.lower().endswith(".json"):
        p += ".json"
    if "/" not in p:
        p = DEFAULT_DIR + "/" + p
    return os.path.join(base, p)


def deep_merge(a, b):
    for k, v in b.items():
        if v is None:
            continue  # wie MergeNullValueHandling.Ignore
        if isinstance(v, dict) and isinstance(a.get(k), dict):
            deep_merge(a[k], v)
        else:
            a[k] = copy.deepcopy(v)


def resolve(node, base, depth=0):
    if depth > 8:
        raise RuntimeError("Load nested too deep")
    for key in list(node.keys()):
        val = node[key]
        if isinstance(val, dict):
            refs = load_refs(val)
            if refs is not None:
                loaded = OrderedDict()
                for ref in refs:
                    part = load_json(ref_path(base, ref))
                    resolve(part, base, depth + 1)
                    deep_merge(loaded, part)
                overlay = copy.deepcopy(val)
                overlay.pop("Load", None)
                resolve(overlay, base, depth + 1)
                deep_merge(loaded, overlay)
                node[key] = loaded
            else:
                resolve(val, base, depth + 1)
        elif isinstance(val, list):
            for item in val:
                if isinstance(item, dict):
                    resolve(item, base, depth + 1)


def strip_nulls(x):
    if isinstance(x, dict):
        return {k: strip_nulls(v) for k, v in x.items() if v is not None}
    if isinstance(x, list):
        return [strip_nulls(v) for v in x]
    return x


# ---- Aufteilen ----

def add_load(node, ref):
    cur = node.get("Load")
    if cur is None:
        node["Load"] = ref
    elif isinstance(cur, str):
        node["Load"] = [cur, ref]
    else:
        cur.append(ref)


def navigate(root, path):
    node = root
    for part in path.split("."):
        if not isinstance(node, dict) or part not in node:
            return None
        node = node[part]
    return node if isinstance(node, dict) else None


def apply_secret_rule(main, files, rule, log):
    path, keys, target = rule
    node = navigate(main, path)
    if node is None:
        return
    if load_refs(node) and target in load_refs(node):
        log(f"  {path}: already moved out, skipped")
        return
    ks = keys if keys else [k for k in node if k not in ("Active", "Load")]
    extracted = OrderedDict((k, node.pop(k)) for k in list(ks) if k in node)
    if not extracted:
        return
    files.setdefault(target + ".json", OrderedDict()).update(extracted)
    add_load(node, target)
    log(f"  {path}: {len(extracted)} field(s) -> {target}.json")


def split(cfg, rules, with_blocks, log):
    main = copy.deepcopy(cfg)
    files = OrderedDict()
    for rule in rules:
        apply_secret_rule(main, files, rule, log)
    if with_blocks:
        for key in list(main.keys()):
            val = main[key]
            if not isinstance(val, dict) or key == "Load":
                continue
            secret_refs = load_refs(val) or []
            if any(not r.startswith(SECRETS_DIR + "/") for r in secret_refs):
                log(f"  {key}: already has Load references, skipped")
                continue
            rest = OrderedDict((k, v) for k, v in val.items() if k not in ("Active", "Load"))
            if not rest:
                continue
            stub = OrderedDict()
            if "Active" in val:
                stub["Active"] = val["Active"]
            refs = [key] + secret_refs
            stub["Load"] = refs[0] if len(refs) == 1 else refs
            main[key] = stub
            files[DEFAULT_DIR + "/" + key + ".json"] = rest
            log(f"  {key}: {len(rest)} field(s) -> {DEFAULT_DIR}/{key}.json")
    return main, files


def string_values(x, out):
    if isinstance(x, dict):
        for v in x.values():
            string_values(v, out)
    elif isinstance(x, list):
        for v in x:
            string_values(v, out)
    elif isinstance(x, str) and len(x) >= 4:
        out.add(x)


def write_tree(root, main_name, main_obj, files):
    os.makedirs(root, exist_ok=True)
    with open(os.path.join(root, main_name), "w", encoding="utf-8") as f:
        f.write(dump_json(main_obj))
    for rel, obj in files.items():
        full = os.path.join(root, rel)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", encoding="utf-8") as f:
            f.write(dump_json(obj))


def plan_for(path, rules, with_blocks, log):
    original = load_json(path)
    main, files = split(original, rules, with_blocks, log)
    return original, main, files


def verify(original, main_name, main_obj, files, secret_texts_ok=True):
    """Merges back in a temp folder and compares with the original."""
    with tempfile.TemporaryDirectory() as tmp:
        write_tree(tmp, main_name, main_obj, files)
        merged = load_json(os.path.join(tmp, main_name))
        resolve(merged, tmp)
        if strip_nulls(json.loads(json.dumps(merged))) != strip_nulls(json.loads(json.dumps(original))):
            return "Merge result differs from the original"
        # Geheimnisse duerfen nur in secrets/ stehen
        secrets = set()
        for rel, obj in files.items():
            if rel.startswith(SECRETS_DIR + "/"):
                string_values(obj, secrets)
        leaked = []
        texts = [dump_json(main_obj)] + [dump_json(o) for r, o in files.items() if not r.startswith(SECRETS_DIR + "/")]
        for s in secrets:
            if any(s in t for t in texts):
                leaked.append(len(s))
        if leaked:
            return f"{len(leaked)} secret value(s) would remain outside secrets/"
    return None


def pick_doc(base, node, key, docs):
    """Follows Load references until the block contains the key. Returns (node, file or None)."""
    cur_file = None
    guard = 0
    while key not in node and load_refs(node) and guard < 8:
        guard += 1
        chosen = None
        for ref in load_refs(node):
            fp = ref_path(base, ref)
            if fp not in docs:
                docs[fp] = load_json(fp)
            if key in docs[fp]:
                chosen = fp
        if chosen is None:
            chosen = ref_path(base, load_refs(node)[0])
            if chosen not in docs:
                docs[chosen] = load_json(chosen)
        node = docs[chosen]
        cur_file = chosen
    return node, cur_file


def move_block(base, main_name, path, target, apply, log):
    """Verschiebt einen Block (ausser Active/Load) einer schon aufgeteilten Config nach secrets/."""
    main_path = os.path.join(base, main_name)
    docs = {main_path: load_json(main_path)}
    original = copy.deepcopy(docs[main_path])
    resolve(original, base)

    node, cur_file = docs[main_path], main_path
    parts = path.split(".")
    for part in parts[:-1]:
        node, f = pick_doc(base, node, part, docs)
        if f:
            cur_file = f
        if part not in node or not isinstance(node[part], dict):
            log(f"  {path}: path not found, skipped")
            return None
        node = node[part]
    last = parts[-1]
    node, f = pick_doc(base, node, last, docs)
    if f:
        cur_file = f
    if last not in node or not isinstance(node[last], dict):
        log(f"  {path}: block not found, skipped")
        return None
    block = node[last]
    if load_refs(block) and target in load_refs(block):
        log(f"  {path}: already moved out, skipped")
        return None
    keys = [k for k in block if k not in ("Active", "Load")]
    if not keys:
        log(f"  {path}: nothing to move")
        return None
    target_file = os.path.join(base, target + ".json")
    if os.path.exists(target_file):
        log(f"ERROR: {target}.json already exists, aborting")
        sys.exit(1)
    extracted = OrderedDict((k, block.pop(k)) for k in keys)
    add_load(block, target)
    log(f"  {path}: {len(extracted)} field(s) aus {os.path.relpath(cur_file, base)} -> {target}.json")

    # Cross-check: merge everything back (the new file is not on disk yet -> temp copy)
    with tempfile.TemporaryDirectory() as tmp:
        for fp, doc in docs.items():
            rel = os.path.relpath(fp, base)
            out = os.path.join(tmp, rel)
            os.makedirs(os.path.dirname(out), exist_ok=True)
            with open(out, "w", encoding="utf-8") as fh:
                fh.write(dump_json(doc))
        # also copy files that are not loaded (all other Load targets)
        for root, _, names in os.walk(base):
            if os.sep + "backups" in root:
                continue
            for n in names:
                if n.endswith(".json"):
                    src = os.path.join(root, n)
                    dst = os.path.join(tmp, os.path.relpath(src, base))
                    if not os.path.exists(dst):
                        os.makedirs(os.path.dirname(dst), exist_ok=True)
                        shutil.copy2(src, dst)
        os.makedirs(os.path.join(tmp, SECRETS_DIR), exist_ok=True)
        with open(os.path.join(tmp, target + ".json"), "w", encoding="utf-8") as fh:
            fh.write(dump_json(extracted))
        merged = load_json(os.path.join(tmp, main_name))
        resolve(merged, tmp)
        if strip_nulls(json.loads(json.dumps(merged))) != strip_nulls(json.loads(json.dumps(original))):
            log("ERROR: cross-check failed, nothing written")
            sys.exit(1)
    log("  Cross-check OK")
    return cur_file, docs[cur_file], target_file, extracted


def run_moves(base, args):
    results = []
    print(f"{args.instance}:")
    for spec in args.move:
        path, _, target = spec.partition("=")
        if not path or not target.startswith(SECRETS_DIR + "/"):
            print(f"Invalid: {spec} (format PATH=secrets/name)")
            sys.exit(1)
        r = move_block(base, args.instance, path.strip(), target.strip(), args.apply, print)
        if r:
            results.append(r)
    if not results:
        print("Nothing to do.")
        return
    if not args.apply:
        print("\nDry run: nothing written. Run with --apply.")
        return
    stamp = time.strftime("%Y%m%d-%H%M%S")
    backup = os.path.join(base, "backups", "config-" + stamp)
    os.makedirs(backup, mode=0o700, exist_ok=True)
    for cur_file, doc, target_file, extracted in results:
        rel = os.path.relpath(cur_file, base)
        os.makedirs(os.path.dirname(os.path.join(backup, rel)), exist_ok=True)
        shutil.copy2(cur_file, os.path.join(backup, rel))
        with open(cur_file, "w", encoding="utf-8") as fh:
            fh.write(dump_json(doc))
        os.makedirs(os.path.dirname(target_file), exist_ok=True)
        with open(target_file, "w", encoding="utf-8") as fh:
            fh.write(dump_json(extracted))
        os.chmod(target_file, 0o600)
        print(f"Written: {rel} and {os.path.relpath(target_file, base)}")
    print(f"Backup: {backup}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dir", default="/home/tbot/tbot", help="config folder (default: /home/tbot/tbot)")
    ap.add_argument("--instance", default="instance_settings.json")
    ap.add_argument("--global-file", default="settings.json")
    ap.add_argument("--move", action="append", default=[], metavar="PATH=secrets/name",
                    help='Block einer schon aufgeteilten Config nach secrets/ verschieben, z. B. "AutoFarm.AllianceWhitelist=secrets/alliance"')
    ap.add_argument("--apply", action="store_true", help="really write (without: plan and cross-check only)")
    args = ap.parse_args()

    base = os.path.abspath(args.dir)
    if args.move:
        run_moves(base, args)
        return
    targets = [
        (args.instance, RULES_INSTANCE, True),
        (args.global_file, RULES_GLOBAL, False),
    ]
    plans = []
    for name, rules, with_blocks in targets:
        path = os.path.join(base, name)
        if not os.path.exists(path):
            print(f"{name}: not found, skipped")
            continue
        print(f"{name}:")
        original, main_obj, files = plan_for(path, rules, with_blocks, print)
        if not files:
            print("  nothing to do")
            continue
        # Kollisionen mit vorhandenen Dateien
        for rel in files:
            if os.path.exists(os.path.join(base, rel)):
                print(f"ERROR: {rel} already exists, aborting (nothing written)")
                sys.exit(1)
        err = verify(original, name, main_obj, files)
        if err:
            print(f"ERROR in cross-check for {name}: {err}. Nothing written.")
            sys.exit(1)
        print("  Cross-check OK (merged back = original, no secrets outside secrets/)")
        plans.append((name, original, main_obj, files))

    if not plans:
        print("Nothing to do.")
        return
    # Target collisions between plans (same file in two plans)
    seen = set()
    for _, _, _, files in plans:
        for rel in files:
            if rel in seen:
                print(f"ERROR: {rel} appears twice")
                sys.exit(1)
            seen.add(rel)

    if not args.apply:
        print("\nDry run: nothing written. Run with --apply.")
        return

    stamp = time.strftime("%Y%m%d-%H%M%S")
    backup = os.path.join(base, "backups", "config-" + stamp)
    os.makedirs(backup, mode=0o700, exist_ok=True)
    for name, _, _, _ in plans:
        shutil.copy2(os.path.join(base, name), os.path.join(backup, name))
    os.chmod(os.path.join(base, "backups"), 0o700)
    print(f"\nBackup: {backup}")

    created = []
    try:
        for name, _, main_obj, files in plans:
            for rel, obj in files.items():
                full = os.path.join(base, rel)
                os.makedirs(os.path.dirname(full), exist_ok=True)
                with open(full, "w", encoding="utf-8") as f:
                    f.write(dump_json(obj))
                created.append(full)
            with open(os.path.join(base, name), "w", encoding="utf-8") as f:
                f.write(dump_json(main_obj))
        # Rechte
        os.makedirs(os.path.join(base, SECRETS_DIR), exist_ok=True)
        os.chmod(os.path.join(base, SECRETS_DIR), 0o700)
        for full in created:
            os.chmod(full, 0o600 if os.sep + SECRETS_DIR + os.sep in full else 0o640)
        for name, _, _, _ in plans:
            os.chmod(os.path.join(base, name), 0o600)
        if os.path.isdir(os.path.join(base, DEFAULT_DIR)):
            os.chmod(os.path.join(base, DEFAULT_DIR), 0o750)
        # Gegenprobe auf den echten Dateien
        for name, original, _, _ in plans:
            merged = load_json(os.path.join(base, name))
            resolve(merged, base)
            if strip_nulls(json.loads(json.dumps(merged))) != strip_nulls(json.loads(json.dumps(original))):
                raise RuntimeError(f"final check failed for {name}")
    except Exception as e:  # zurueckrollen
        print(f"ERROR: {e}. Restoring backup.")
        for name, _, _, _ in plans:
            shutil.copy2(os.path.join(backup, name), os.path.join(base, name))
        for full in created:
            try:
                os.remove(full)
            except OSError:
                pass
        sys.exit(1)

    print("Done. New files:")
    for full in created:
        print("  " + os.path.relpath(full, base))
    print("The bot reloads the config on the next change event; for a safe test: sudo systemctl restart tbot")


if __name__ == "__main__":
    main()
