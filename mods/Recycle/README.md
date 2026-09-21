# Recycle

Break crafted items back into a share of their materials. The game itself never takes an item back (dismantling a
building returns everything, the incinerator only gives coal), so gear you have outgrown just piles up in chests.

## What it does

- **F3** (also in the action wheel, group Mods, and a button in the inventory) opens the recycling window: every
  crafted item in your inventory (weapons, tools, armour, shields, torches, ammunition) with what recycling it gives.
- The yield is a share of the crafting cost read from the game's own recipe, **one third by default**, rounded down per
  material. Upgrades count: a level 3 axe gives back a third of the base cost plus a third of each upgrade.
  Ammunition is recycled by the stack (a craft makes several arrows, so the cost is spread).
- Two clicks: the first arms the button ("Confirm?"), the second recycles. The item disappears, the materials go
  into your inventory, or drop at your feet if it is full.
- Equipped items are never listed: take them off first. Raw materials are never recyclable; crafted materials
  (bronze, nails...) only if you turn `IncludeMaterials` on.
- No crafting station needed.

## Configuration (`BepInEx/config/vmods.recycle.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod |
| ToggleKey | F3 | Open / close the window |
| Ratio | 0.34 | Share of the crafting cost given back (0.05 to 1) |
| IncludeAmmo | true | Offer ammunition too |
| IncludeMaterials | false | Offer crafted materials (bronze, nails, tanned leather...) |

Everything can also be changed in game with Mod Hub (F9).

## Similar mods

Recycle_N_Reclaim (Azumatt), ValheimRecycle (SQG) and SimpleRecycling (abearcodes) do the same thing with a tab in the
crafting menu and a crafting station requirement. This one is written from scratch, with the same window style and
gamepad support as the other mods of this set.

---

## En français

Recycle l'équipement fabriqué contre une part de ses matériaux.

- **F3** (roue d'action, bouton dans l'inventaire) : fenêtre listant chaque objet fabriqué du sac et ce que son
  recyclage rend : **un tiers** du coût de fabrication par défaut (réglable), améliorations comprises, arrondi vers le bas.
  Les munitions se recyclent par pile.
- Deux clics (le second confirme). L'objet disparaît, les matériaux vont dans l'inventaire, au sol s'il est plein.
- Ce qui est équipé n'est jamais proposé ; les matériaux bruts non plus, les matériaux fabriqués seulement si
  `IncludeMaterials` est activé. Aucune station d'artisanat nécessaire.

Réglages : `ToggleKey`, `Ratio`, `IncludeAmmo`, `IncludeMaterials` ; fichier `vmods.recycle.cfg` ou Mod Hub (F9).
