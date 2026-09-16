# Lumberjack

The Woodcutting skill finally matters, and fallen trees cut themselves up.

## What it does

- **Skill**: every axe hit removes at least (Woodcutting level %) of the target's max health (standing tree, log on
  the ground, stump, bush). At level 50 a tree needs at most 2 hits, at level 100 a single one. Vanilla axe damage
  remains the floor, and the game still requires the right tool tier (you cannot chop a birch with a stone axe).
- **Auto-chop**: when you fell a tree, the falling log, then its half-logs, then the stump are cut up on their own
  once they rest on the ground. Trees knocked down by the one you felled (chain reaction) are cut too. Everything goes
  through the game's normal damage path, so drops and effects are the same as chopping by hand. Logs that were already
  lying in the world are not touched.

## Configuration (`BepInEx/config/vmods.lumberjack.cfg`)

| Section | Key | Default | Meaning |
|---------|-----|---------|---------|
| General | Enabled | true | Enable the mod |
| Skill | HpPercentAtMaxLevel | 100 | Percentage of max HP removed at least per hit at level 100; scales with level (0 = off) |
| AutoChop | AutoChopLogs | true | Logs, half-logs and stumps of trees you fell are cut up automatically |
| AutoChop | AutoChopChainReaction | true | Trees knocked down by your tree are cut too |
| AutoChop | AutoChopDelay | 1.5 | Seconds to wait before a fallen log is cut (lets the falling animation play) |

---

## En français

La compétence Bûcheron compte enfin, et les arbres abattus se découpent tout seuls.

- **Compétence** : chaque coup de hache enlève au moins (niveau Bûcheron %) des PV max de la cible. Niveau 50 :
  2 coups au plus ; niveau 100 : 1 coup. Les dégâts vanilla restent le plancher et le tier d'outil est toujours exigé.
- **Auto-découpe** : le tronc qui tombe, ses sous-troncs puis la souche se découpent seuls une fois au sol ; les arbres
  renversés en chaîne aussi. Mêmes drops et effets qu'à la main. Les troncs déjà présents dans le monde ne sont pas
  concernés.

Réglages : `HpPercentAtMaxLevel`, `AutoChopLogs`, `AutoChopChainReaction`, `AutoChopDelay` ; fichier
`vmods.lumberjack.cfg` ou Mod Hub (F9).
