Packages has three peer filter groups: **Parasitic**, **Factions** and **Custom**.
The reviewed bundle has 44 class-scoped parasitic packages from 23 named synergy
families, plus 20 class-scoped faction branches. Existing saved Custom packages
remain independent. A shared keyword, name or flavour tag alone does not justify
a parasitic package. Each included group needs a concrete dependency: generators
and dedicated payoffs, or a resource/progression shared by its members.

Every built-in package has one playable class. Shared neutral and multi-class
cards are included only where they are legal for that same class. A neutral-only
package contains neutral cards throughout. The generator rejects cross-class
mixtures, singleton definitions, missing synergy explanations and inferred
keyword/faction membership. Packages are placed in their own native class bucket.

Sigils, Spellstones, Prime Legendaries, Fabled singletons, generic token generators
and the Alliance/Horde flavour collections have been removed. Galakrond, Jade
and Soul Fragments now have distinct class branches. Relic of Kings, Kabal Lackey,
Possessed Lackey and minions merely named Treant are excluded from unrelated
synergy packages. Factions remains independent of Parasitic; it does not imply
that every faction card is a parasitic mechanic.

The [explicit definitions and synergy reasons](CategoryOverrides.json) use the
pinned HearthstoneJSON [build 255790 collectible snapshot](https://api.hearthstonejson.com/v1/255790/enUS/cards.collectible.json),
SHA-256 `71be4da8bada839392c8b823af12ddd9e771a05fc6b974db8cea64b00c30f805`.
[The generator](../Tools/curate_categories.py) validates each member's actual
legal classes (including multi-class metadata) and produces [Packages.json](../Packages.json).
Membership is an editorial decision recorded as explicit card IDs, not a regex
match or automatic inclusion of new cards. Tokens are artwork choices rather
than inventory members. Legacy generated keyword buckets cannot restore retired
built-ins. Renamed legacy source packages appear under Custom; existing saved
custom package records remain intact.

| Group | Package | Playable class | Members | Synergy |
| --- | --- | --- | ---: | --- |
| Factions | Grimy Goons · Hunter | HUNTER | 7 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Grimy Goons · Paladin | PALADIN | 7 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Grimy Goons · Warrior | WARRIOR | 7 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Jade Lotus · Druid | DRUID | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Jade Lotus · Rogue | ROGUE | 7 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Jade Lotus · Shaman | SHAMAN | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Kabal · Mage | MAGE | 2 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Kabal · Priest | PRIEST | 2 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Kabal · Warlock | WARLOCK | 3 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Protoss · Druid | DRUID | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Protoss · Mage | MAGE | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Protoss · Priest | PRIEST | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Protoss · Rogue | ROGUE | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Terran · Paladin | PALADIN | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Terran · Shaman | SHAMAN | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Terran · Warrior | WARRIOR | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Zerg · Death Knight | DEATHKNIGHT | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Zerg · Demon Hunter | DEMONHUNTER | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Zerg · Hunter | HUNTER | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Factions | Zerg · Warlock | WARLOCK | 8 | Named faction branch restricted to one playable class and its shared legal members. |
| Parasitic | Abyssal Curses | WARLOCK | 5 | Curse damage progresses across generators; dedicated Curse payoffs. |
| Parasitic | Asteroids | SHAMAN | 4 | Bolide Behemoth upgrades Asteroids from all included generators. |
| Parasitic | Bombs | WARRIOR | 7 | Blastmaster Boom requires Bombs shuffled by the other members. |
| Parasitic | C'Thun Abilities · Demon Hunter | DEMONHUNTER | 4 | The Old God explicitly counts the four triggered C'Thun abilities. |
| Parasitic | C'Thun Abilities · Neutral | NEUTRAL | 3 | The Old God explicitly counts the four triggered C'Thun abilities. |
| Parasitic | C'Thun Abilities · Priest | PRIEST | 4 | The Old God explicitly counts the four triggered C'Thun abilities. |
| Parasitic | C'Thun Abilities · Shaman | SHAMAN | 4 | The Old God explicitly counts the four triggered C'Thun abilities. |
| Parasitic | C'Thun Abilities · Warlock | WARLOCK | 4 | The Old God explicitly counts the four triggered C'Thun abilities. |
| Parasitic | C'Thun · Druid | DRUID | 12 | Cultists specifically buff C'Thun and enable C'Thun thresholds. |
| Parasitic | C'Thun · Mage | MAGE | 11 | Cultists specifically buff C'Thun and enable C'Thun thresholds. |
| Parasitic | C'Thun · Neutral | NEUTRAL | 10 | Cultists specifically buff C'Thun and enable C'Thun thresholds. |
| Parasitic | C'Thun · Priest | PRIEST | 12 | Cultists specifically buff C'Thun and enable C'Thun thresholds. |
| Parasitic | C'Thun · Rogue | ROGUE | 11 | Cultists specifically buff C'Thun and enable C'Thun thresholds. |
| Parasitic | C'Thun · Warlock | WARLOCK | 11 | Cultists specifically buff C'Thun and enable C'Thun thresholds. |
| Parasitic | C'Thun · Warrior | WARRIOR | 11 | Cultists specifically buff C'Thun and enable C'Thun thresholds. |
| Parasitic | Concoctions | ROGUE | 5 | Mixed Concoctions and dedicated Concoction payoffs share this resource. |
| Parasitic | Earthen | PALADIN | 2 | Each Earthen scales with earlier Earthen summons. |
| Parasitic | Galakrond · Priest | PRIEST | 7 | Invoke advances the class Galakrond and enables dedicated Invoke payoffs. |
| Parasitic | Galakrond · Rogue | ROGUE | 7 | Invoke advances the class Galakrond and enables dedicated Invoke payoffs. |
| Parasitic | Galakrond · Shaman | SHAMAN | 7 | Invoke advances the class Galakrond and enables dedicated Invoke payoffs. |
| Parasitic | Galakrond · Warlock | WARLOCK | 7 | Invoke advances the class Galakrond and enables dedicated Invoke payoffs. |
| Parasitic | Galakrond · Warrior | WARRIOR | 7 | Invoke advances the class Galakrond and enables dedicated Invoke payoffs. |
| Parasitic | Jade Golems · Druid | DRUID | 8 | Each Jade Golem grows with earlier Jade summons. |
| Parasitic | Jade Golems · Rogue | ROGUE | 9 | Each Jade Golem grows with earlier Jade summons. |
| Parasitic | Jade Golems · Shaman | SHAMAN | 9 | Each Jade Golem grows with earlier Jade summons. |
| Parasitic | Lackeys · Priest | PRIEST | 4 | Dedicated payoffs require or improve Lackeys generated by these cards. |
| Parasitic | Lackeys · Rogue | ROGUE | 4 | Dedicated payoffs require or improve Lackeys generated by these cards. |
| Parasitic | Lackeys · Shaman | SHAMAN | 5 | Dedicated payoffs require or improve Lackeys generated by these cards. |
| Parasitic | Lackeys · Warlock | WARLOCK | 6 | Dedicated payoffs require or improve Lackeys generated by these cards. |
| Parasitic | Lackeys · Warrior | WARRIOR | 6 | Dedicated payoffs require or improve Lackeys generated by these cards. |
| Parasitic | Leylines | MAGE | 7 | Dedicated cost, effect and repeat upgrades apply only to Leylines. |
| Parasitic | Librams | PALADIN | 14 | Dedicated discounts and recasts require Libram spells. |
| Parasitic | Ogre-Gang | NEUTRAL | 4 | Kingpin Pud specifically resurrects the three Ogre-Gang members. |
| Parasitic | Plagues | DEATHKNIGHT | 6 | Helya, Tomb Traitor and Chained Guardian explicitly require Plagues. |
| Parasitic | Pogo-Hoppers | ROGUE | 2 | Pogo-Hopper scales with repeats; Lab Recruiter supplies repeat copies. |
| Parasitic | Relics | DEMONHUNTER | 5 | Relics upgrade later Relics; Relic Vault and Xy'mox explicitly support them. |
| Parasitic | Riffs | WARRIOR | 3 | A Riff replays the previously played Riff. |
| Parasitic | Rituals | WARLOCK | 4 | Kith'ix requires another completed Ritual. |
| Parasitic | Silver Hand Recruits | PALADIN | 24 | Recruit-specific buffs and discounts require Recruit generators. |
| Parasitic | Soul Fragments · Demon Hunter | DEMONHUNTER | 7 | Generators shuffle the same resource consumed by dedicated payoffs. |
| Parasitic | Soul Fragments · Warlock | WARLOCK | 7 | Generators shuffle the same resource consumed by dedicated payoffs. |
| Parasitic | Treants · Druid | DRUID | 34 | Treant-specific discounts, buffs and death counters require Treant generators. |
| Parasitic | Volatile Skeletons | MAGE | 5 | Kel'Thuzad requires the Volatile Skeletons generated by other members. |
| Parasitic | Wildseeds | HUNTER | 4 | Wild Spirits accelerates the shared Wildseed tokens. |

Opening a group creates lightweight result proxies. Portraits and native recipe
art are requested only for the actual destination page. Sorting does not compute
a folder's summed mana cost or inspect all member qualities/runes. A missing first
member uses the visible native carrier instead; missing portraits keep their recipe
shell and retry in place. Class branches cannot appear in another class's bucket
merely because a shared neutral member matched the native search.
