# Auto Save

More frequent automatic saves. The game already saves on its own, but only every 30 minutes and with no setting to
change it; a crash or a power cut can cost half an hour of play.

## What it does

- Sets the interval of the game's own autosave to **5 minutes** by default (1 to 60). It is the game's save that runs:
  character and world, with its usual warning and icon; nothing is written next to it.
- In multiplayer, the world is saved by the server as usual; your character is saved at the chosen interval.

## Configuration (`BepInEx/config/vmods.autosave.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod (off = the game's 30 minutes) |
| Minutes | 5 | Minutes between two autosaves |

Everything can also be changed in game with Mod Hub (F9).

---

## En français

Sauvegarde automatique plus fréquente. Le jeu sauvegarde seul, mais seulement toutes les 30 minutes, sans réglage.

- Intervalle de la sauvegarde automatique du jeu réglé à **5 minutes** par défaut (1 à 60). C'est la sauvegarde du jeu
  elle-même (personnage et monde, avec son avertissement et son icône), rien n'est écrit à côté.

Réglages : `Enabled`, `Minutes` ; fichier `vmods.autosave.cfg` ou Mod Hub (F9).
