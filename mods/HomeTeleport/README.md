# Home Teleport

Go back to your bed with one key.

## What it does

- **F8** teleports you to your spawn point: the bed you last slept in (the one the game uses on death), or the
  sacrificial stones if you have no bed.
- A confirmation window shows where you are going and how far it is; confirm with the same key, Enter or A on a
  gamepad, cancel with Esc or B. The confirmation can be turned off.
- Uses the game's own portal travel (loading screen, zone loaded before arrival) but without the portal restriction
  on ore and metal.
- Works whenever you want, even in a fight: only an optional cooldown between two trips.
- Also in the action wheel (group Mods) and the game's key-hint panel through Mod Hub.

## Configuration (`BepInEx/config/vmods.hometeleport.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod |
| Key | F8 | Teleport key |
| Confirm | true | Ask for confirmation before leaving |
| Cooldown | 0 | Minimum seconds between two trips (0 = none) |

---

## En français

Retour au lit d'une touche.

- **F8** : téléportation vers votre point d'apparition (dernier lit utilisé, sinon les pierres sacrificielles).
- Fenêtre de confirmation avec la destination et la distance : même touche, Entrée ou A pour partir, Échap ou B pour
  rester. Désactivable.
- Trajet des portails du jeu (écran de chargement), mais sans la restriction sur le minerai.
- Possible à tout moment, même en combat ; délai optionnel entre deux retours.

Réglages : `Key`, `Confirm`, `Cooldown` ; fichier `vmods.hometeleport.cfg` ou Mod Hub (F9).
