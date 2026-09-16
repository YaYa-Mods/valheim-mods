# Movement

Faster walking, jogging, running and swimming for the local player.

## What it does

- Multiplies the game's jog and run speed factors (which already include equipment and status effects), so the
  relative effect of armour, food and buffs is preserved.
- Swimming speed is multiplied the same way.
- Only the local player is affected; creatures and other players are untouched.

## Configuration (`BepInEx/config/vmods.movement.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod |
| SpeedMultiplier | 1.3 | Walk / jog / run multiplier (1 = vanilla) |
| SwimMultiplier | 1.3 | Swim multiplier (1 = vanilla) |

---

## En français

Marche, course et nage plus rapides pour le joueur local.

- Multiplie les facteurs de vitesse du jeu (qui intègrent déjà équipement et effets).
- Nage multipliée de la même façon. Créatures et autres joueurs ne sont pas concernés.

Réglages : `SpeedMultiplier`, `SwimMultiplier` (1.3 par défaut) ; fichier `vmods.movement.cfg` ou Mod Hub (F9).
