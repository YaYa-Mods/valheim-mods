# Movement

Faster walking, jogging, running and swimming for the local player.

## What it does

- Multiplies the game's jog and run speed factors (which already include equipment and status effects), so the
  relative effect of armour, food and buffs is preserved.
- Swimming speed is multiplied the same way.
- Only the local player is affected; creatures and other players are untouched.
- **Free stamina out of combat**: running, jumping, swimming, woodcutting, mining cost no stamina while nothing is
  fighting you. As soon as you take a hit from a creature or land one on a creature, or a creature attacks you up close (`CombatRange`, 10 m), stamina
  drains normally again, until `CombatSeconds` (5 s) after the last hit. A monster that merely spotted you from afar does not count.

## Configuration (`BepInEx/config/vmods.movement.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod |
| SpeedMultiplier | 1.3 | Walk / jog / run multiplier (1 = vanilla) |
| SwimMultiplier | 1.3 | Swim multiplier (1 = vanilla) |
| Stamina / FreeOutOfCombat | true | No stamina used outside combat |
| Stamina / CombatSeconds | 5 | Seconds you stay in combat after the last hit taken or dealt |
| Stamina / CombatRange | 10 | A creature targeting you within this distance (m) puts you in combat |

---

## En français

Marche, course et nage plus rapides pour le joueur local.

- Multiplie les facteurs de vitesse du jeu (qui intègrent déjà équipement et effets).
- Nage multipliée de la même façon. Créatures et autres joueurs ne sont pas concernés.
- **Endurance gratuite hors combat** : course, saut, nage, bûcheronnage, minage ne coûtent rien tant que rien ne vous
  attaque. Dès qu'un coup est échangé avec une créature ou qu'une créature vous attaque tout près (10 m), l'endurance s'use normalement
  (jusqu'à `CombatSeconds`, 5 s, après le dernier coup). Un monstre qui vous a seulement repéré de loin ne compte pas.

Réglages : `SpeedMultiplier`, `SwimMultiplier` (1.3 par défaut) ; fichier `vmods.movement.cfg` ou Mod Hub (F9).
