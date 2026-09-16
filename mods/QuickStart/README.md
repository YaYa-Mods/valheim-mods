# Quick Start

Get into the game faster.

## What it does

- **Skip the intro**: the intro cinematic is disabled before the menu starts it, so the game opens straight on the
  main menu (the Unity logo comes before any mod code and cannot be skipped).
- **Auto-load** (off by default): on the first visit to the menu, the mod "clicks" Start, then the last character,
  then the last world, the ones the game already preselects from its own preferences. Only once per launch: going
  back to the menu from a game leaves the choice free.
- `AutoLoadCharacter` / `AutoLoadWorld` name a specific character and world instead of the last ones. They are only
  honoured when the test harness is installed (they exist for the automated tests, which must never load a real save);
  otherwise they are ignored with a warning in the log.

## Configuration (`BepInEx/config/vmods.quickstart.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod |
| SkipIntro | true | Skip the intro cinematic |
| AutoLoadLastWorld | false | Load the last character into the last world on start |
| AutoLoadDelay | 0.15 | Seconds between each automatic step (menu, character, world) |
| AutoLoadCharacter | (empty) | Character to load (test harness only) |
| AutoLoadWorld | (empty) | World to load (test harness only) |

---

## En français

Entrer plus vite dans le jeu.

- **Sans intro** : la cinématique est désactivée, arrivée directe sur le menu principal.
- **Chargement automatique** (désactivé par défaut) : au premier passage par le menu, le mod « clique » Démarrer, le
  dernier personnage puis le dernier monde. Une seule fois par lancement.
- `AutoLoadCharacter` / `AutoLoadWorld` : personnage et monde précis, honorés seulement quand le harnais de test est
  installé (ils servent aux tests automatiques, qui ne doivent jamais charger une vraie partie).

Réglages : `SkipIntro`, `AutoLoadLastWorld`, `AutoLoadDelay` ; fichier `vmods.quickstart.cfg` ou Mod Hub (F9).
