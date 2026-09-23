# Miner

Faster mining of ore deposits, and only of ore deposits: ordinary stone is unchanged. Same idea as Lumberjack: your
skill matters.

## What it does

- **More pickaxe damage on ores** (copper, tin, iron scrap, silver, obsidian, flametal...): ×3 at any level, up to ×6 at
  Pickaxes level 100 (proportional to the level).
- **Area hits on large deposits**: a copper vein is made of dozens of chunks. The game can already apply one hit to every
  chunk in a radius (it just never does it for the player's pickaxe); this mod gives it 1.5 m, so a vein falls in a
  handful of hits instead of several minutes.
- A rock counts as ore when the game's own drop table for it contains an ore; nothing is hard-coded.
- The tool tier the game requires (bronze pickaxe for iron, and so on) is still required.

## Configuration (`BepInEx/config/vmods.miner.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod |
| DamageMultiplier | 3 | Pickaxe damage multiplier on ores, at any level |
| SkillBonus | 3 | Added at Pickaxes level 100, proportional to the level |
| AreaRadius | 1.5 | Radius (m) of a hit on large deposits (0 = one chunk, as in the game) |

Everything can also be changed in game with Mod Hub (F9).

---

## En français

Minage rapide des filons de minerai uniquement (la pierre ordinaire ne change pas), la compétence compte.

- Dégâts de pioche sur les minerais **×3**, jusqu'à **×6** au niveau 100 de Pioche.
- **Coup en zone** sur les gros filons (1,5 m) : les morceaux voisins cassent aussi, un filon de cuivre tombe en
  quelques coups. C'est un mécanisme du jeu, simplement activé pour la pioche du joueur.
- Le niveau d'outil exigé par le jeu reste exigé.

Réglages : `DamageMultiplier`, `SkillBonus`, `AreaRadius` ; fichier `vmods.miner.cfg` ou Mod Hub (F9).
