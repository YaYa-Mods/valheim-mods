# Test Harness (developers only)

In-game integration tests for the other mods. Not meant to be installed by players.

## What it does

- Runs once when a world is loaded (`AutoRun = true` in its config, reset to false afterwards) and logs every check as
  `[TEST] PASS ...` or `[TEST] FAIL ...` in `BepInEx/LogOutput.log`, ending with `[TEST] ===== fin : N PASS, M FAIL`.
- **Refuses to run** on anything but the dedicated test character and world (`ModTester` / `ModTestWorld`, local
  saves). It creates them itself at the main menu if they are missing, skips the valkyrie intro, and turns the
  world autosave off so nothing is written.
- Test groups: gamepad navigation of the mod windows, map pins, creature search and discovery by sight, inventory
  (sorting, filter, consumables), guide (tracker, altar pin, offerings, loading-screen gate), catalogue audit against
  the game's placement tables (spawn lists, vegetation, locations, dungeon rooms), end-to-end search of every
  catalogue entry (each prefab is instantiated, found, then destroyed), home teleport, track mode, skills kept on
  death, localisation (table consistency, switch to English with screenshots, language restored).
- Screenshots are written to `BepInEx/config` (`finder_scan.png`, `guide_tracker.png`, `finder_en.png`, ...), plus
  dumps of the available fonts, the key-hint hierarchy and the world's vegetation table.
- `WorldReport = true` writes a report of the loaded world (key locations, biomes, coast around the spawn) instead.

## Running

```powershell
$env:VALHEIM_DIR = 'path\to\Valheim'
tools\run-tests.ps1 -Quick     # builds nothing: run tools\rebuild-all.ps1 first
```

The script deploys the harness DLL, points Quick Start at the test character and world, launches the game, waits for
the end marker in the log, then closes the game, removes the harness DLL and restores the player's Quick Start
configuration. Everything is in a `try / finally` so an interrupted run still cleans up. It refuses to start if the
game is already open.

---

## En français

Tests d'intégration en jeu pour les autres mods, réservés au développement.

- S'exécute une fois au chargement d'un monde (`AutoRun`), journalise chaque vérification `[TEST] PASS / FAIL` dans
  `BepInEx/LogOutput.log`.
- **Refuse de tourner** ailleurs que sur le personnage et le monde de test (`ModTester` / `ModTestWorld`, sauvegardes
  locales), qu'il crée lui-même ; intro sautée, sauvegarde automatique coupée.
- Groupes : manette, épingles, créatures, inventaire, guide, audit du catalogue contre les tables du jeu, recherche de
  bout en bout de chaque entrée, retour au lit, traque, compétences à la mort, localisation (bascule en anglais avec
  captures, langue rétablie).
- Captures dans `BepInEx/config`.

Lancement : `tools\run-tests.ps1 -Quick` (jeu fermé, `VALHEIM_DIR` défini). Le script restaure la configuration de
Quick Start du joueur quoi qu'il arrive.
