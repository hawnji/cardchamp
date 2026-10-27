#!/usr/bin/env python3
"""Builds tools/art/prompts.json: one image-generation job per card template and set.

Prompts are assembled from the game content (cardvault_content.json) and the art direction
in art_direction.json, so the same athlete or character is described the same way on every
card they appear on. Re-run after editing either file:

    python3 tools/art/build_prompts.py              # cards + set banners
    python3 tools/art/build_prompts.py --variants   # also unique art for 1/1 parallels

Each job carries a hash of its prompt; generate_art.py uses it to regenerate only the
images whose prompt changed.
"""
import argparse
import hashlib
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
CONTENT = os.path.join(ROOT, "Assets", "CardVault", "Resources", "Content", "cardvault_content.json")
DIRECTION = os.path.join(HERE, "art_direction.json")
OUT = os.path.join(HERE, "prompts.json")

# Output sizes match the on-screen frames: cards are 1:1.4, set banners are wide heroes.
CARD_SIZE = [512, 720]
SET_SIZE = [1024, 400]

NAMED_COLORS = [
    ("navy", (22, 50, 92)), ("royal blue", (31, 78, 140)), ("teal", (18, 124, 140)), ("forest green", (46, 107, 63)),
    ("crimson", (140, 47, 31)), ("brick red", (179, 65, 42)), ("purple", (74, 59, 105)), ("charcoal", (59, 59, 59)),
    ("black", (43, 43, 43)), ("gold", (227, 179, 65)), ("yellow", (242, 183, 5)), ("white", (240, 240, 240)),
    ("silver", (217, 217, 217)), ("sand", (242, 215, 182)),
]


def color_name(hex_value):
    """Image models ignore hex codes, so team colours are described with the nearest plain name."""
    h = hex_value.lstrip("#")
    rgb = tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
    return min(NAMED_COLORS, key=lambda c: sum((a - b) ** 2 for a, b in zip(rgb, c[1])))[0]


def join(*parts):
    return " ".join(p.strip() for p in parts if p and p.strip())


def job(key, kind, size, prompt, negative):
    digest = hashlib.sha256(json.dumps([prompt, negative, size]).encode("utf-8")).hexdigest()[:16]
    return {"key": key, "kind": kind, "size": size, "prompt": prompt, "negative": negative, "hash": digest}


def pick(options, index):
    if isinstance(options, list):
        return options[index % len(options)] if options else ""
    return options or ""


def subject_line(subject, direction, content, occurrence):
    desc = direction["subjects"].get(subject["id"])
    if not desc:
        raise SystemExit("art_direction.json has no description for subject '%s'" % subject["id"])
    if subject["kind"] != "Athlete":
        element = direction["elements"].get(subject.get("element"), "")
        return join("Subject: " + desc + ".", ("Surrounded by " + element + ".") if element else "")
    team = next(t for t in content["teams"] if t["id"] == subject["teamId"])
    pose = pick(direction["positions"].get(subject.get("position"), "in action"), occurrence)
    uniform = "%s and %s" % (color_name(team["primaryColor"]), color_name(team["secondaryColor"]))
    return "Subject: %s, wearing a plain %s uniform with no lettering or logos, %s." % (desc, uniform, pose)


def card_prompt(card, set_def, subject, direction, content, occurrence, extra=""):
    """occurrence counts earlier cards of the same subject, so repeat cards get a new pose and shot."""
    g = direction["global"]
    m = direction["modifiers"]
    style = direction["sets"][set_def["id"]]["style"]
    flags = [
        m["rookie"] if card["isRookie"] else "",
        m["autograph"] if card["isAutograph"] else "",
        m["alternateArt"] if card["isAlternateArt"] else "",
        m["vintage"] if set_def.get("isVintage") else "",
        direction["rarity"].get(card["rarity"], ""),
    ]
    shot = pick(g.get("shots"), occurrence)
    return join(g["prefix"], style, subject_line(subject, direction, content, occurrence), shot, *flags, extra,
                g["composition"], g["suffix"])


def build(include_variants=False):
    with open(CONTENT, encoding="utf-8") as f:
        content = json.load(f)
    with open(DIRECTION, encoding="utf-8") as f:
        direction = json.load(f)

    sets = {s["id"]: s for s in content["sets"]}
    subjects = {s["id"]: s for s in content["subjects"]}
    negative = direction["global"]["negative"]
    variants = {k: v for k, v in direction["variants"].items() if not k.startswith("_")}
    jobs = []
    seen = {}

    for card in content["cards"]:
        set_def = sets[card["setId"]]
        subject = subjects[card["subjectId"]]
        occurrence = seen.get(subject["id"], 0)
        seen[subject["id"]] = occurrence + 1
        prompt = card_prompt(card, set_def, subject, direction, content, occurrence)
        jobs.append(job(card["artKey"], "card", CARD_SIZE, prompt, negative))
        if include_variants:
            for parallel_id in set_def.get("parallelIds", []):
                if parallel_id in variants:
                    prompt = card_prompt(card, set_def, subject, direction, content, occurrence, variants[parallel_id])
                    jobs.append(job(card["artKey"] + "_" + parallel_id, "variant", CARD_SIZE, prompt, negative))

    for set_def in content["sets"]:
        art = direction["sets"][set_def["id"]]
        prompt = join("Wide banner artwork, no people in focus.", art["style"], art["setArt"], direction["global"]["setSuffix"])
        jobs.append(job("sets/" + set_def["artKey"], "set", SET_SIZE, prompt, negative))

    keys = [j["key"] for j in jobs]
    if len(keys) != len(set(keys)):
        raise SystemExit("duplicate art keys in prompts")
    prompts = [j["prompt"] for j in jobs]
    if len(prompts) != len(set(prompts)):
        raise SystemExit("two jobs share an identical prompt; add poses or shots to art_direction.json")
    return {"contentVersion": content["contentVersion"], "jobs": jobs}


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--variants", action="store_true", help="also build unique art jobs for 1/1 parallels")
    args = parser.parse_args(argv)
    data = build(args.variants)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write("\n")
    kinds = {}
    for j in data["jobs"]:
        kinds[j["kind"]] = kinds.get(j["kind"], 0) + 1
    print("wrote %s: %s" % (os.path.relpath(OUT, ROOT), ", ".join("%d %s" % (n, k) for k, n in sorted(kinds.items()))))


if __name__ == "__main__":
    main(sys.argv[1:])
