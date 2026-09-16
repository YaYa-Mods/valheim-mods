# Craft From Chests

Crafting, building and stations use the materials stored in nearby chests, as if they were in your inventory.

## What it does

- The crafting menu, the build hammer and every crafting station (workbench, forge, black forge, artisan table...)
  count and consume materials from the chests around you.
- With `Stations` on, the fire, oven, smelter, blast furnace, cooking station and fermenter also take their input from
  chests when you press **E** on them.
- Nothing is moved: items are consumed directly from the chest they are in, and the game's own menus show the totals.
  No menu or station is modified; the game simply sees the chests' content as yours during those operations.

## Configuration (`BepInEx/config/vmods.craftfromchests.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod |
| Range | 30 | Radius (m) around the player in which chests are used (2 to 200) |
| Stations | true | Fire, oven, smelter, cooking station and fermenter take from chests too |

Also configurable in game with Mod Hub (F9).

---

## En français

Artisanat, construction et stations puisent dans les coffres proches comme s'ils étaient dans l'inventaire.

- Menu d'artisanat, marteau et toutes les stations comptent et consomment les matériaux des coffres alentour.
- Avec `Stations`, le feu, le four, la fonderie, le haut fourneau, la station de cuisson et le fermenteur prennent
  aussi dans les coffres (touche **E**).
- Rien n'est déplacé : les objets sont consommés dans leur coffre, les menus du jeu affichent les totaux.

Réglages : `Range` (rayon, 30 m), `Stations` ; fichier `vmods.craftfromchests.cfg` ou Mod Hub (F9).
