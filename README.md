# YaYa's Valheim mods

A set of fourteen BepInEx mods for Valheim, built from source against the game's own assemblies, no downloaded binaries,
no external dependencies beyond BepInEx and Harmony. The in-game UI follows the game language: **French** or **English** (any other language gets English). Each mod has its own README in `mods/<Mod>/` with keys, configuration and details.

| Mod | What it does |
|-----|--------------|
| [**Resource Finder**](mods/ResourceFinder/README.md) | Scanner window (F7) with a catalogue of ores, pickables, trees, locations and creatures. Finds the nearest ones in the known world, can generate unexplored zones to look further, pins results on the map, points at the target on screen (pill + minimap marker + glowing silhouette), **track mode** (F4) that chains to the next target when one is harvested or killed. Discovery by sight: what you look at or fight becomes visible in the catalogue. |
| [**Guide**](mods/Guide/README.md) | Progression guide: one chapter per boss, steps that check themselves from what the game records (crafted items, placed pieces, kills, explored locations…), an on-screen quest tracker (full / compact / hidden, F11), a journal window (F10), anti-spoiler locking of future chapters. Offerings, recipes and building costs are read from the game data, not hard-coded. |
| [**Mod Hub**](mods/ModHub/README.md) | In-game configuration of every mod (F9), a “Mods” group in the radial menu, mod buttons in the inventory panel, mod key hints in the game's own key-hint bar. |
| [**Inventory**](mods/Inventory/README.md) | Larger, scrollable inventory (up to 40 rows), equipment slots (head, chest, legs, cape, accessory) next to the grid, pickups fill from the top, sorting (category / name / quantity / weight), stacking, category filter toolbar, keep inventory and skills on death, tombstone recovery, bigger stacks and carry weight. |
| [**Craft From Chests**](mods/CraftFromChests/README.md) | Crafting stations and the crafting menu use materials from nearby chests. |
| [**Home Teleport**](mods/HomeTeleport/README.md) | F8: teleport to your bed (with confirmation). |
| [**Recycle**](mods/Recycle/README.md) | F3: recycle crafted gear for a share of its materials (one third by default, upgrades included), read from the game recipes; plus a buildable Recycler chest that turns everything thrown in it into materials. |
| [**Lumberjack**](mods/Lumberjack/README.md) | Woodcutting skill matters: high level fells trees in one hit and auto-cuts logs. |
| [**Longer Food**](mods/LongerFood/README.md) | Food lasts longer (× factor, configurable). |
| [**No Durability**](mods/NoDurability/README.md) | Tools, weapons and armour never wear out. |
| [**Short Nights**](mods/ShortNights/README.md) | Shorter nights without changing the day length. |
| [**Movement**](mods/Movement/README.md) | Faster running, jogging and swimming; no stamina used out of combat. |
| [**Auto Save**](mods/AutoSave/README.md) | The game's own autosave every 5 minutes instead of 30 (configurable). |
| [**Quick Start**](mods/QuickStart/README.md) | Skip the intro, auto-load a character and world. |
| [**Test Harness**](mods/TestHarness/README.md) | *(developers)* In-game integration tests with screenshots, run on a dedicated test character/world. |

All mods share `mods/Common` (IMGUI theme using the game's own fonts, gamepad navigation, the French/English text table) and talk to each other only by
reflection (`RadialEntries()`, `InventoryEntries()`, `KeyHints()`, `SearchLabel()`, `HudRects()`, `CloseWindow()`), so every DLL works alone.

Only one thing is on screen at a time: opening a mod window closes the other mods' windows, the inventory and the large map, and
the window closes itself when the game takes the screen back (inventory, map, pause menu, shop, death). On-screen elements
(quest tracker, target pill) never cover the game HUD or each other.

## Building

Requirements: .NET SDK (8+), Valheim installed, BepInEx 5 installed in the game folder (`BepInEx/core`).

```powershell
# game folder: environment variable VALHEIM_DIR, or copy mods/Directory.Build.props.user.example
#              to mods/Directory.Build.props.user and edit the path
dotnet build mods/ResourceFinder -c Release -p:Deploy=true   # builds and copies the DLL to BepInEx/plugins
tools/rebuild-all.ps1                                          # everything
```

BepInEx itself can be compiled from source with `tools/build-bepinex.ps1` / `tools/build-doorstop.ps1` (they clone the
upstream repositories into `tools/`, which is git-ignored).

## Tests

`tools/run-tests.ps1 -Quick` launches the game on a dedicated **test character and world** (`ModTester` / `ModTestWorld`,
local saves, created automatically), runs the harness (~70 checks: gamepad navigation, map pins, creature search,
inventory, guide, catalogue audit against the game's placement tables, track mode, localisation…), captures screenshots into
`BepInEx/config`, then closes the game without saving. The harness refuses to run on any other character or world.

## License

MIT, see `LICENSE`.

---

## En français

Quatorze mods BepInEx pour Valheim, compilés depuis les sources contre les DLL du jeu. Interface en français ou en anglais selon la langue du jeu. Chaque mod a son propre README dans `mods/<Mod>/`.

- **Resource Finder** (F7) : scanner de ressources, lieux et créatures ; épingles sur la carte ; cible à l'écran ;
  mode **Traque** (F4) qui enchaîne les cibles ; découverte par la vue.
- **Guide** (F10) : guide de progression par boss, étapes cochées automatiquement d'après le jeu, suivi de quête à
  l'écran (F11 : complet / réduit / masqué), anti-spoiler.
- **Mod Hub** (F9) : configuration en jeu, roue d'action, boutons dans l'inventaire, aides de touches.
- **Inventory** : inventaire agrandi et défilant, emplacements d'équipement à côté de la grille, ramassage dans la
  première case libre, tri, empilage, filtre par catégorie, mort sans perte (objets et compétences).
- **Recycle** (F3) : recyclage de l'équipement fabriqué contre un tiers de ses matériaux, et un meuble Recycleur à poser.
- **Craft From Chests**, **Home Teleport** (F8), **Lumberjack**, **Longer Food**, **No Durability**, **Short Nights**,
  **Movement** (avec endurance gratuite hors combat), **Auto Save** (sauvegarde toutes les 5 min), **Quick Start**.

Une seule chose à l'écran à la fois : ouvrir une fenêtre de mod ferme celles des autres mods, l'inventaire et la grande carte,
et la fenêtre se ferme d'elle-même si le jeu reprend l'écran (inventaire, carte, menu, boutique, mort). Le suivi de quête et
la pastille de cible ne recouvrent jamais le HUD du jeu ni l'un l'autre.

Compilation : `dotnet build mods/<Mod> -c Release -p:Deploy=true` (dossier du jeu : variable `VALHEIM_DIR` ou
`mods/Directory.Build.props.user`). Tests en jeu : `tools/run-tests.ps1 -Quick` (personnage et monde de test dédiés).
