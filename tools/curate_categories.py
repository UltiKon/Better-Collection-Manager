#!/usr/bin/env python3
"""Build the explicit, offline category catalog and a full coverage audit.

Rules propose role memberships from a pinned card-data snapshot. Editorial
overrides and named packages are kept separately so corrections survive rebuilds.
No part of this classifier runs inside Hearthstone.
"""
import argparse
import collections
import csv
import hashlib
import html
import io
import json
import re
from pathlib import Path
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
SOURCE = "https://api.hearthstonejson.com/v1/255790/enUS/cards.collectible.json"
SOURCE_SHA256 = "71be4da8bada839392c8b823af12ddd9e771a05fc6b974db8cea64b00c30f805"
ROLES = ["aoe", "single-target-removal", "smorc", "value", "win-conditions", "draw", "unique-effects", "packages", "techw"]


def normalized_id(card_id):
    while card_id.upper().startswith("CORE_") or card_id.startswith("VAN_"):
        card_id = card_id.split("_", 1)[1]
    return card_id


def text_of(card):
    text = card.get("collectionText") or card.get("text", "")
    text = re.sub(r"<[^>]+>", "", html.unescape(text)).replace("[x]", "")
    return re.sub(r"\s+", " ", text).strip().lower().replace("’", "'")


def playable(card):
    return card.get("collectible", False) and card["type"] in {"MINION", "SPELL", "WEAPON", "LOCATION", "HERO"} \
        and card["set"] != "HERO_SKINS" and not re.match(r"^(?:(?:VAN|CORE)_)?HERO_", card["id"], re.I)


def propose(card):
    t = text_of(card)
    mechanics = set(card.get("mechanics", []))
    roles = collections.defaultdict(set)

    def add(role, rule):
        roles[role].add(rule)

    # Board-wide damage/removal, including split damage and repeated random pings.
    clauses = re.split(r"(?<=[.!])\s+|;", t)
    for clause in clauses:
        damage = bool(re.search(r"\bdeal\b[^.]{0,100}\bdamage\b|\bdamage\b[^.]{0,50}\bdealt\b", clause))
        removal = bool(re.search(r"\b(destroy|transform|return|shuffle|remove)\b", clause))
        many_targets = bool(re.search(r"damage[^.]{0,25}(?:all|each|every|adjacent|two|three|four|five|both) (?:other )?(?:random )?(?:undamaged |damaged )?(?:enemy )?(?:minions|characters|enemies)|damage[^.]*to a minion and all others|damage to an? (?:enemy )?minion and (?:[\d$@{}]+ damage to )?(?:its )?neighbors", clause))
        split_targets = bool(re.search(r"\bsplit\b[^.]{0,35}(?:enemies|(?:all )?(?:other )?minions|(?:all )?(?:other )?characters)", clause))
        random_repeats = bool(re.search(r"random enem", clause) and re.search(r"\brepeat|\bfor each\b|\beach (?:that|time)|\b(?:two|three|four|five|six) times", clause))
        board_removal = removal and bool(re.search(r"\b(?:destroy|transform|return|devour)\b[^.]{0,25}(?:all|each|every|both|two|three) [^.]{0,35}minions?", clause))
        only_own = bool(re.search(r"(?:all|each|every|two|three|both) (?:other )?(?:of )?(?:your |friendly )|split[^.]{0,25}friendly", clause))
        if not only_own and ((damage and (many_targets or split_targets or random_repeats)) or board_removal):
            add("aoe", "board-damage-or-removal")
        if damage and re.search(r"whenever|after you", clause) and re.search(r"random enem|all enem|enemy minions", clause):
            add("aoe", "repeatable-enemy-damage")
        if re.search(r"attack all (?:other minions|enemies|enemy minions)|enemy minions attack each other|devour all enemy minions", clause):
            add("aoe", "multiple-minion-combat-or-devour")

    # Single enemy minion answers; damage which is explicitly friendly/hero-only is excluded.
    if re.search(r"\b(destroy|transform|return|eat|take control of|steal)\b[^.]{0,100}(?:an? (?:enemy )?minion|enemy minion|a (?:damaged|random enemy|random|frozen) minion|an? (?:enemy )?(?:beast|pirate|murloc|demon|location))", t):
        add("single-target-removal", "single-minion-answer")
    if re.search(r"\bdeal\b[^.]{0,80}\bdamage\b", t):
        targeted = re.search(r"damage to (?:an? |the |a random )?(?:enemy )?(?:minion|character)|damage to (?:an? |the )?(?:damaged|frozen|undamaged) minion", t)
        generic_spell = re.search(r"\bdeal [\d$@{}]+(?: to [\d$@{}]+)? damage[.!]", t)
        random_one = re.search(r"damage to a random enemy(?! minions)", t)
        if targeted or generic_spell or random_one:
            add("single-target-removal", "single-target-damage")
    if re.search(r"give an? (?:enemy )?minion -\d+/|set an? (?:enemy )?minion's health to 1", t):
        add("single-target-removal", "single-target-debuff")
    if (mechanics & {"RUSH", "CHARGE"} or re.search(r"summon[^.]*\b(?:rush|charge)\b", t)) and "can't attack" not in t:
        add("single-target-removal", "immediate-minion-combat")
    if card["type"] == "WEAPON" and card.get("attack", 0) > 0 and "can't attack minions" not in t:
        add("single-target-removal", "weapon-combat")

    # Direct face damage, immediate attacks, hero attack and aggressive combat modifiers.
    if "CHARGE" in mechanics or re.search(r"\b(?:summon|give|have|gain|gains)[^.]*\bcharge\b", t):
        add("smorc", "charge")
    if "WINDFURY" in mechanics or re.search(r"\b(?:windfury|mega-windfury)\b", t):
        add("smorc", "multiple-attacks")
    if card["type"] == "WEAPON" and card.get("attack", 0) > 0 and "can't attack heroes" not in t:
        add("smorc", "weapon-face-attacks")
    if re.search(r"(?:give your hero|your hero (?:gains?|has))[^.]{0,60}(?:attack|windfury)|your hero can attack again", t):
        add("smorc", "hero-attack")
    for clause in clauses:
        if not re.search(r"\bdeal\b[^.]{0,100}\bdamage\b", clause):
            continue
        if re.search(r"enemy hero|your opponent|both heroes|all characters|all enemies|random enemy(?! minion)|split among (?:all )?enemies", clause):
            add("smorc", "face-damage")
        elif re.search(r"\bdeal [\d$@{}]+(?: to [\d$@{}]+)? damage[.!]", clause):
            add("smorc", "flexible-burn")

    # Draw includes tutors/recruiting cards from the deck into hand, but not opponents drawing.
    if re.search(r"(?:^|[.:!;] |, |then |and |to |you |whenever[^.]{0,70}, |after[^.]{0,70}, |if[^.]{0,70}, )draw (?!an? (?:card for your opponent|card from your opponent))", t):
        if not re.search(r"(?:your opponent|opponents?|both players|each player) (?:also )?draw", t) or re.search(r"\byou draw|draw[^.]*then your opponent|both players|each player", t):
            add("draw", "card-draw")
    if re.search(r"draw (?:a |an |\d|two|three|four|five|cards|until|that|it|them|one|your)", t) and not re.search(r"(?:opponent|opponents?) draws?|when (?:you|your opponent) draw", t):
        add("draw", "card-draw")
    if re.search(r"(?:put|add)[^.]{0,80}from your deck[^.]{0,50}(?:into|to) your hand|discover[^.]{0,60}from your deck(?!.*copy)", t):
        add("draw", "deck-to-hand")

    # Value means additional resources, recursion, ramp or persistent generation, not every buff.
    if ("DISCOVER" in mechanics or re.search(r"\bdiscover\b", t)) and not re.search(r"discover[^.]*from (?:your|the|your opponent's) deck", t):
        add("value", "discovery")
    if re.search(r"(?:add|adds|put)[^.]{0,120}(?:to|into) your hand", t) and not re.search(r"from your deck", t):
        add("value", "hand-generation")
    if re.search(r"\b(resurrect|resummon)\b|summon[^.]{0,100}(?:died|dead|deathrattle)|return[^.]{0,100}(?:friendly|your)[^.]{0,60}(?:hand|deck)", t):
        add("value", "recursion")
    if re.search(r"\b(?:gain|refresh) [\d#]+ (?:empty )?mana crystal|gain an? (?:empty )?mana crystal|\bgain (?:an? |\d+ )?mana crystal", t):
        add("value", "mana-ramp")
    if re.search(r"\b(copy|copies)\b", t) and not re.search(r"your opponent (?:gets|gains) (?:a |an? )?copy", t):
        add("value", "copy-generation")
    if re.search(r"(?:at the (?:end|start)|whenever|after you|each turn)[^.]{0,120}(?:summon|add|discover|draw|resurrect|equip)", t):
        add("value", "resource-engine")
    if "draw" in roles and card["type"] in {"MINION", "WEAPON", "HERO", "LOCATION"}:
        add("value", "body-or-engine-plus-draw")
    if card["type"] == "HERO":
        add("value", "persistent-hero-power")
    if "EXCAVATE" in mechanics or "IMBUE" in mechanics:
        add("value", "generated-rewards")
    if mechanics & {"TWINSPELL", "REBORN", "ECHO", "STARSHIP_PIECE"}:
        add("value", "additional-spell-or-body")
    if re.search(r"(?:battlecry(?: and inspire)?|deathrattle):[^.]{0,100}\bsummon\b|(?:at the (?:end|start)|whenever|after)[^.]{0,100}\b(?:fill your board|recruit)\b", t) and not re.search(r"summon[^.]{0,60}(?:from your hand|from your deck|for your opponent|your opponent)", t):
        add("value", "body-plus-summon")
    if re.search(r"\bget\b|fill your hand|shuffle[^.]{0,80}into your deck|\b(?:replay|recast)\b|cast[^.]{0,70}(?:twice|random spells|again)|deathrattles trigger twice|battlecries trigger twice", t):
        add("value", "generated-hand-resource")
    if mechanics & {"TITAN", "COLOSSAL"} or re.search(r"\btitan\b|\bcolossal\b", t):
        add("value", "multiple-bodies-or-abilities")
    if re.search(r"\brefresh\b[^.]{0,30}mana crystal|gain[^.]{0,30}mana crystal", t):
        add("value", "mana-ramp")
    if re.search(r"\brecruit\b|(?:battlecries|deathrattles)[^.]{0,35}(?:twice|trigger again)|(?:give|double)[^.]{0,80}(?:hand and deck|hand, deck)|(?:your|friendly) [^.]{0,35}(?:cards|spells|minions|murlocs|demons|dragons|elementals) cost[^.]{0,30}(?:less|\(1\)|\(0\))", t):
        add("value", "deck-resource-or-mana-engine")
    if re.search(r"reduce (?:their cost|the cost)[^.]{0,70}|your (?:first|next) [^.]{0,45}cost[^.]{0,20}less|every third spell[^.]{0,40}costs", t):
        if re.search(r"your (?:hand|deck|first|next)|in hand|every third spell|reduce their cost", t):
            add("value", "mana-reduction-engine")
    if re.search(r"summon[^.]{0,100}from your (?:hand|deck)|(?:craft|build|create|assemble) (?:a |an? )?custom", t):
        add("value", "mana-cheat-or-custom-resource")

    # Narrow counterplay; normal removal and healing do not become TECHW by default.
    if re.search(r"\b(?:destroy|steal|remove|swap|take)\b[^.]{0,70}(?:opponent's|enemy|their)[^.]{0,35}(?:weapon|secret)|\b(?:destroy|steal|remove)\b[^.]{0,50}(?:all|enemy) secrets", t):
        add("techw", "weapon-or-secret-hate")
    if re.search(r"\bsilence\b", t) and not re.search(r"silence (?:a |an? )?friendly|silence your|silence all friendly|silence all your|silence this minion|silence adjacent", t):
        add("techw", "enemy-silence")
    if re.search(r"\b(?:destroy|eat|remove)\b (?:an? |an? enemy |an? random enemy |enemy )(?:pirate|murloc|beast|demon|location)\b|destroy an? (?:enemy )?minion with taunt|enemy hero[^.]{0,40}(?:armor|armour)", t):
        if not re.search(r"(?:destroy|eat) a friendly", t):
            add("techw", "specific-threat-hate")
    if re.search(r"\b(?:discard|destroy|remove|swap|steal|replace|transform|eat)\b[^.]{0,95}(?:your opponent's|enemy|their)[^.]{0,40}(?:hand|deck)", t) \
            or re.search(r"your opponent (?:discards?|summons?|casts?)[^.]{0,65}(?:from their hand|from their deck)", t) \
            or re.search(r"shuffle[^.]{0,60}(?:your opponent's hand|cards? (?:in|from) your opponent's hand)", t):
        if not re.search(r"each turn this is in your hand, transform into", t):
            add("techw", "hand-or-deck-disruption")
    if re.search(r"(?:enemy spells|opponent's spells|enemy battlecry cards)[^.]{0,40}cost[^.]{0,20}more|(?:^|[.:] )spells cost[^.]{0,30}more|minions with battlecry cost", t):
        add("techw", "spell-or-battlecry-tax")
    if re.search(r"counter (?:the |a |an? )?(?:next )?(?:minion|spell)|your opponent (?:can't|cannot)|destroy (?:both players'|your opponent's) mana", t):
        add("techw", "counter-or-lockout")

    # Unambiguous alternate endings and deck-defining quest payoffs.
    if "QUEST" in mechanics or "QUESTLINE" in mechanics or re.search(r"^questline:", t):
        add("win-conditions", "quest-archetype")
    if re.search(r"(?:win|wins) the game|destroy the enemy hero|destroy your opponent|take an extra turn", t):
        add("win-conditions", "alternate-ending")
        add("unique-effects", "game-ending-rule")
    if re.search(r"start of game|start of your turn[^.]*win|your deck has no|your deck contains no|your starting deck|odd-cost|even-cost|maximum mana|turns? (?:are|is) only|replace your (?:starting )?(?:deck|hero power)|swap (?:hands|decks)|restart the game", t):
        add("unique-effects", "deck-or-game-rule")
    if re.search(r"tourist|\btitan\b", t):
        if "tourist" in t or "TITAN" in mechanics:
            add("unique-effects", "deck-access-or-titan-abilities")
    if re.search(r"\bfabled\b|\bold god\b", t):
        add("unique-effects", "fabled-deck-package-or-old-god-abilities")
    return roles


def build(cards, source_hash):
    edits = json.loads((ROOT / "data/CategoryOverrides.json").read_text())
    by_id = {c["id"]: c for c in cards}
    universe = [c for c in cards if playable(c)]
    groups = collections.defaultdict(list)
    for card in universe:
        groups[normalized_id(card["id"])].append(card)
    unknown = set(edits["cards"]) - set(groups)
    if unknown:
        raise ValueError("Overrides reference absent cards: " + ", ".join(sorted(unknown)))
    memberships = {}
    for card_id, printings in groups.items():
        roles = collections.defaultdict(set)
        current = by_id.get(card_id) or min(printings, key=lambda c: c["dbfId"])
        # Historical Classic text can differ from the current Constructed card.
        # Use current base text; printings remain explicitly covered in the audit.
        for printing in [current]:
            for role, evidence in propose(printing).items():
                roles[role].update(evidence)
        edit = edits["cards"].get(card_id, {})
        if "replace" in edit:
            roles.clear()
            for role in edit["replace"]:
                roles[role].add("editorial:" + edit["reason"])
        for role in edit.get("remove", []):
            roles.pop(role, None)
        for role in edit.get("add", []):
            roles[role].add("editorial:" + edit["reason"])
        if set(roles) - set(ROLES):
            raise ValueError("Unknown category in " + card_id)
        memberships[card_id] = roles

    package_members = collections.defaultdict(set)
    seen_packages = set()
    for package in edits["packages"]:
        if package["id"] in seen_packages:
            raise ValueError("Duplicate package ID: " + package["id"])
        seen_packages.add(package["id"])
        if package.get("class") not in {"NEUTRAL", "DEMONHUNTER", "DEATHKNIGHT", "DRUID", "HUNTER", "MAGE", "PALADIN", "PRIEST", "ROGUE", "SHAMAN", "WARLOCK", "WARRIOR"} or package.get("kind") not in {"Parasitic", "Factions"}:
            raise ValueError("Unknown package class or kind: " + package["name"])
        explicit = set(package.get("cards", []))
        card_class = package.get("class")
        if len(explicit) < 2 or not package.get("reason") or not card_class:
            raise ValueError("A reviewed package needs multiple members, one class and a synergy reason: " + package["name"])
        if explicit - set(groups):
            raise ValueError("Unknown package cards in " + package["name"])
        if any(key in package for key in ("pattern", "mechanics", "faction", "factionClasses")):
            raise ValueError("Package membership must be explicit, never inferred from keywords or faction labels")
        for card_id in explicit:
            current = by_id.get(card_id) or min(groups[card_id], key=lambda c: c["dbfId"])
            legal = set(current.get("classes") or [current.get("cardClass", "NEUTRAL")])
            if card_class not in legal and legal != {"NEUTRAL"}:
                raise ValueError("Cross-class package: " + package["name"] + "/" + card_id)
            package_members[package["id"]].add(card_id)
            memberships[card_id]["packages"].add("package:" + package["id"])

    def requirement(card_id):
        printings = groups[card_id]
        primary = by_id.get(card_id, min(printings, key=lambda c: c["dbfId"]))
        item = {"id": primary["id"], "count": 1}
        aliases = sorted(c["id"] for c in printings if c["id"] != primary["id"] and normalized_id(c["id"]) != normalized_id(primary["id"]))
        if aliases:
            item["aliases"] = aliases
        return item

    entries = []
    audit = []
    for card_id in sorted(groups):
        printings = groups[card_id]
        primary = by_id.get(card_id, min(printings, key=lambda c: c["dbfId"]))
        roles = memberships[card_id]
        ordinary_roles = [r for r in ROLES if r in roles and r != "packages"]
        if ordinary_roles:
            entries.append({"id": "card-" + card_id, "name": primary["name"], "categories": ordinary_roles, "cards": [requirement(card_id)]})
        for card in sorted(printings, key=lambda c: c["id"]):
            evidence = set(rule for rules in roles.values() for rule in rules)
            if card_id in edits["cards"]:
                evidence.add("editorial-note:" + edits["cards"][card_id]["reason"])
            audit.append([card["id"], card_id, card["name"], card["set"], card.get("cardClass", "/".join(card.get("classes", [])) or "NEUTRAL"), card["type"],
                          ";".join(r for r in ROLES if r in roles),
                          "editorial" if card_id in edits["cards"] else "data-rules" if roles else "outside-role-definitions",
                          ";".join(sorted(evidence)) or "no-specific-role"])
    packages = []
    for package in edits["packages"]:
        members = sorted(package_members[package["id"]])
        if not members:
            raise ValueError("Empty package " + package["name"])
        packages.append({"id": package["id"], "name": package["name"], "categories": ["packages"],
                         "packageKind": package["kind"], "packageClass": package["class"], "reason": package["reason"], "cards": [requirement(card_id) for card_id in members]})

    header = {"version": 1, "catalogRevision": 4, "source": SOURCE, "sourceSha256": source_hash,
              "note": "Complete Constructed catalog pass; data-assisted role assignments with explicit editorial overrides. See data/CategoryAudit.tsv and data/Categories.md for coverage and definitions."}
    catalog = "{\n" + ",\n".join("  " + json.dumps(k) + ": " + json.dumps(v) for k, v in header.items())
    catalog += ',\n  "entries": [\n' + ",\n".join("    " + json.dumps(entry, ensure_ascii=False) for entry in entries) + "\n  ]\n}\n"
    output = io.StringIO(newline="")
    writer = csv.writer(output, delimiter="\t", lineterminator="\n")
    writer.writerow(["card_id", "canonical_id", "name", "set", "class", "type", "categories", "method", "evidence"])
    writer.writerows(audit)
    summary = {"source": SOURCE, "sourceSha256": source_hash, "sourceRows": len(cards), "playablePrintings": len(universe),
               "canonicalCards": len(groups), "excludedRows": len(cards) - len(universe),
               "assignedCards": sum(any(role != "packages" for role in roles) for roles in memberships.values()),
               "outsideRoleDefinitions": sum(not any(role != "packages" for role in roles) for roles in memberships.values()),
               "editorialCards": len(edits["cards"]), "entries": len(entries), "packages": len(package_members),
               "packageFolders": dict(collections.Counter(p["packageKind"] for p in packages)),
               "categoryCounts": {role: sum(role in roles for roles in memberships.values()) for role in ROLES},
               "sets": dict(sorted(collections.Counter(c["set"] for c in universe).items()))}
    package_catalog = json.dumps({**header, "entries": packages}, ensure_ascii=False, indent=2) + "\n"
    return {ROOT / "data/Categories.json": catalog, ROOT / "data/Packages.json": package_catalog, ROOT / "data/CategoryAudit.tsv": output.getvalue(),
            ROOT / "data/CategoryCoverage.json": json.dumps(summary, indent=2) + "\n"}, summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, help="Cached cards.collectible.json from the pinned source")
    parser.add_argument("--check", action="store_true", help="Verify committed outputs instead of writing them")
    args = parser.parse_args()
    if args.input:
        raw = args.input.read_bytes()
    else:
        with urlopen(Request(SOURCE, headers={"User-Agent": "BetterCollectionManager catalog maintenance"}), timeout=45) as response:
            raw = response.read()
    source_hash = hashlib.sha256(raw).hexdigest()
    if source_hash != SOURCE_SHA256:
        raise SystemExit("Input differs from the pinned source snapshot; update source/version explicitly before rebuilding")
    outputs, summary = build(json.loads(raw), source_hash)
    for path, content in outputs.items():
        if args.check:
            if not path.exists() or path.read_text() != content:
                raise SystemExit("Catalog output differs: " + str(path))
        else:
            path.write_text(content)
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
