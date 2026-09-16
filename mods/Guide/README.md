# Guide

A progression guide that follows the normal order of the game, one chapter per boss, with steps that tick themselves
from what the game already records. Nothing is hard-coded that the game can tell us: altar offerings are read from the
altar itself, recipes and building costs from the object database. The interface follows the game language.

## How it works

- **Chapters**: Eikthyr, The Elder, Bonemass, Moder, Yagluth, The Queen, Fader, then the Frozen King (Deep North).
  Each chapter lists what to do before the fight: stations to build, tools to craft, biomes to explore, offerings to
  bring, plus a few tips.
- **Steps check themselves**: crafted items, placed pieces, kills, discovered materials, visited biomes, explored
  locations, current inventory (for offerings) and active food. A filled diamond is required, a hollow one is advised
  or optional, a check mark is done. Steps marked "current state" (food, offerings in the bag) are re-evaluated all
  the time.
- **On-screen tracker** (like a quest tracker): the current objective, a progress bar and the next steps. **F11**
  cycles between full, compact (one line) and hidden. It unfolds for a few seconds when a step is completed, and hides
  during loading screens, teleports, the map, the inventory and menus.
- **Journal window (F10)**: all chapters, current one highlighted, previous ones collapsed. Each step shows the real
  recipe or building cost under its hint, a trophy icon in front of the chapter, and a **Target** button that starts
  the Resource Finder scanner on the related catalogue entry (altar, crypt, village...) when that mod is installed.
- **Anti-spoiler**: future chapters are locked (title, steps and reward hidden) until the previous boss is defeated.
  Any chapter can be revealed by hand.
- **Ready-made positions**: below the map, top left, bottom left, bottom right. Each one is checked automatically against the game HUD (health and stamina bars, action bar, status effects, minimap, key hints) so a tracker never covers them, whatever the number of steps shown.
- **Altar pin**: once the current chapter's altar lies in an explored part of the map, a boss pin is placed on it.
  Nothing is revealed that you have not seen.

## Keys

| Key | Action |
|-----|--------|
| F10 | Open / close the journal |
| F11 | Tracker mode: full, compact, hidden |

Also in the action wheel (group Mods) and the inventory buttons (Mod Hub).

## Configuration (`BepInEx/config/vmods.guide.cfg`)

| Section | Key | Default | Meaning |
|---------|-----|---------|---------|
| General | Enabled | true | Enable the guide |
| General | ToggleKey | F10 | Journal window |
| General | Notify | true | On-screen message when a step is completed |
| General | HideFuture | true | Anti-spoiler locking of chapters not yet reached |
| General | AltarPin | true | Automatic pin on the current chapter's altar (explored areas only) |
| Tracker | ShowTracker | true | Show the on-screen tracker |
| Tracker | Mode | Complet | Complet (full), Reduit (compact), Masque (hidden) |
| Tracker | TrackerKey | F11 | Cycle the tracker mode |
| Tracker | Steps | 4 | Steps shown under the objective |
| Tracker | X, Y | -350, 290 | Tracker position (1080p units; negative X = from the right edge, negative Y = from the bottom, where the panel grows upwards and never covers the health bars) |

## Notes

- Progress is derived, not stored: the guide reads the character's statistics, known materials, the world's global
  keys (bosses) and the explored map. Only the per-chapter "revealed" flags and the chosen chapter are saved in the
  character's custom data.
- The test harness audits every chapter against the game data (offerings, recipes, locations) so the guide cannot
  drift from the game version it is built against.

---

## En français

Guide de progression dans l'ordre normal du jeu, un chapitre par boss, avec des étapes qui se cochent toutes seules
d'après ce que le jeu enregistre. Rien de chiffré n'est écrit en dur : offrandes lues sur l'autel, recettes et coûts de
construction lus dans le jeu. L'interface suit la langue du jeu.

- **Chapitres** : Eikthyr, L'Ancien, Bonemass, Moder, Yagluth, La Reine, Fader, puis le Roi gelé.
- **Étapes automatiques** : objets fabriqués, pièces posées, kills, matériaux connus, biomes visités, lieux explorés,
  inventaire du moment (offrandes), nourriture active. Losange plein : obligatoire ; creux : conseillé ; coche : fait.
- **Suivi à l'écran** : objectif courant, barre, prochaines étapes. **F11** : complet, réduit, masqué. Se déploie
  quelques secondes quand une étape est accomplie ; caché pendant les chargements, la carte, l'inventaire et les menus.
- **Journal (F10)** : tous les chapitres, recette réelle sous chaque étape, icône du trophée, bouton **Cibler** qui
  lance le scanner de Resource Finder sur l'entrée liée.
- **Anti-spoiler** : les chapitres à venir sont verrouillés jusqu'à la victoire sur le boss précédent ; chacun peut
  être révélé à la main.
- **Épingle d'autel** : posée dès que l'autel du chapitre courant se trouve en zone explorée.

Configuration : `BepInEx/config/vmods.guide.cfg` (tableau ci-dessus) ou en jeu avec Mod Hub (F9).
