# Short Nights

Shorter nights without changing the length of the day/night cycle.

## What it does

- The game's cycle is 30 minutes, of which the night takes 30% (9 minutes). This mod changes that share: with the
  default 0.15 the night lasts 4.5 minutes and the day gets the time back.
- Everything that depends on the time of day (sun, lighting, night spawns, sleeping) follows, and the wake-up time in
  bed is aligned with the new sunrise.

## Configuration (`BepInEx/config/vmods.shortnights.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod |
| NightFraction | 0.15 | Share of the cycle taken by the night (vanilla 0.30) |

---

## En français

Des nuits plus courtes sans toucher à la durée du cycle.

- Le cycle du jeu dure 30 minutes, dont 30 % de nuit (9 minutes). Avec 0.15, la nuit dure 4,5 minutes et le jour
  récupère le temps gagné.
- Soleil, éclairage, apparitions nocturnes, sommeil et heure de réveil suivent.

Réglage : `NightFraction` (0.15 par défaut) ; fichier `vmods.shortnights.cfg` ou Mod Hub (F9).
