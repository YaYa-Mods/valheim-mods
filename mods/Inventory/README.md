| General | EquipmentSlots | true | Head / Chest / Legs / Cape / Accessory slots next to the grid |
| General | FillTopFirst | true | Picked-up items go to the first free slot from the top |
# Inventory

A more comfortable inventory: bigger, sortable, filterable, and death without loss.

## What it does

- **More rows**: the inventory gets more rows (20 by default, up to 40) using the same mechanism the game uses when
  you buy rows from traders, so the rows are saved on the character and nothing is lost if the mod is removed. The
  panel shows a fixed number of rows (9 by default) and the rest scrolls (mouse wheel, right stick).
- **No weight limit** and **bigger stacks** (9999 by default) for every stackable item. Equipment (stack of 1) is
  untouched.
- **Sorting**: a "Sort" button in the inventory (also in the action wheel and Mod Hub). Modes: by category (the action
  wheel groups: consumables, weapons and tools, armour and accessories, then materials, trophies, misc), by name, by
  quantity, by weight. The button's icon shows the current mode; click it to cycle.
- **Stacking**: merges piles of the same item and frees slots.
- **First free slot**: anything you pick up, craft or take from a chest lands in the first free slot from the top (the game
  fills from the bottom, which sends everything to row 20). The hotbar stays the last resort, weapons still go to the hotbar first.
- **Equipment slots**: Head, Chest, Legs, Cape and Accessory get their own slots in a small panel next to the grid. What you
  equip moves there (right click, hotkey, wheel) and comes back to the grid when you take it off; you can also drag a piece
  onto its slot to equip it, or out of it to remove it. A slot only accepts its own type. Nothing new in the save: the
  slots are the last inventory row, drawn apart, so removing the mod leaves everything in place.
- **Category filter**: a toolbar above the grid with one button per category. Choosing one sorts that category first
  and either **hides** the other items (slots look empty and cannot be clicked) or **dims** them, depending on
  `FilterStyle`.
- **Death**: keep the whole inventory (no tombstone) and keep your skills (the game normally removes 5% of each).
  Tombstones left in the world from before are emptied into your inventory on spawn and removed.
- **Toolbar**: actions (sort, stack) then one button per category, in the same icon language as the game's action wheel. It disappears with the mod when you disable it, and the mouse wheel scrolls the grid instead of zooming the camera.
- **Safety backup**: a copy of the inventory is written in the character data at every save; on load, anything lost
  (truncated stack, destroyed item) is restored. It survives removing the mod.

## Configuration (`BepInEx/config/vmods.inventory.cfg`)

| Section | Key | Default | Meaning |
|---------|-----|---------|---------|
| General | Enabled | true | Enable weight, stacks and rows |
| General | NoWeightLimit | true | Never encumbered |
| General | MaxStackSize | 9999 | Max stack size for stackable items (0 = vanilla) |
| General | InventoryRows | 20 | Rows applied on spawn (vanilla 4; the game caps the visible grid at 9, beyond that it scrolls) |
| General | VisibleRows | 9 | Rows visible at once in the panel |
| General | SortMode | Categorie | Categorie, Nom (name), Quantite (quantity), Poids (weight) |
| General | FilterStyle | Masquer | Masquer (hide the other items) or Estomper (dim them) |
| Death | KeepInventoryOnDeath | true | No tombstone, everything stays on you |
| Death | KeepSkillsOnDeath | true | No skill loss on death |
| Death | RecoverTombstones | true | Empty your remaining tombstones into your inventory on spawn |
| Safety | BackupEnabled | true | Inventory backup in the character data |

---

## En français

Un inventaire plus confortable : plus grand, triable, filtrable, et une mort sans perte.

- **Plus de lignes** (20 par défaut, jusqu'à 40), par le mécanisme que le jeu utilise pour les lignes achetées chez
  les marchands : sauvegardé sur le personnage, rien n'est perdu si le mod est retiré. Le panneau affiche 9 lignes,
  le reste défile.
- **Plus de limite de poids**, **piles de 9999**.
- **Tri** (bouton dans l'inventaire, roue d'action, Mod Hub) : catégorie, nom, quantité, poids.
- **Empilage** : fusionne les piles identiques.
- **Première case libre** : tout objet ramassé, fabriqué ou pris dans un coffre va dans la première case libre en partant
  du haut (le jeu remplit par le bas). La barre d'action reste en dernier recours, les armes y vont toujours d'abord.
- **Emplacements d'équipement** : Tête, Torse, Jambes, Cape, Accessoire ont leurs cases dans un panneau à côté de la
  grille. Ce qui est équipé y va, ce qui est retiré revient dans la grille ; on peut aussi glisser une pièce sur sa case
  ou hors d'elle. Une case n'accepte que son type. Rien de nouveau dans la sauvegarde (dernière ligne de l'inventaire).
- **Filtre par catégorie** : barre d'outils au-dessus de la grille ; la catégorie choisie passe en premier et les
  autres objets sont masqués (ou estompés selon `FilterStyle`).
- **Mort** : inventaire et compétences conservés ; les pierres tombales restantes sont vidées dans l'inventaire à
  l'apparition.
- **Sauvegarde de sécurité** de l'inventaire dans les données du personnage.

Configuration : `BepInEx/config/vmods.inventory.cfg` (tableau ci-dessus) ou en jeu avec Mod Hub (F9).
