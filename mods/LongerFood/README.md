# Longer Food

Food with positive effects lasts longer.

## What it does

- Multiplies the duration of every food item (the `m_foodBurnTime` of its prefab). The food gives the same health,
  stamina and eitr, for longer, and the tooltip shows the new duration.
- Only items whose food values are all positive are changed. Potions and meads have their own status-effect
  duration, which is left alone.
- Original values are kept, so changing the multiplier in game (Mod Hub) reapplies on the fly, and disabling the mod
  restores vanilla.

## Configuration (`BepInEx/config/vmods.longerfood.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod |
| FoodDurationMultiplier | 1.5 | 1.0 = vanilla, 1.5 = +50%, 1.67 turns 3 min into 5 min |

---

## En français

Les aliments à effets positifs durent plus longtemps.

- Multiplie la durée de chaque aliment ; mêmes stats, plus longtemps, et l'infobulle suit.
- Seuls les aliments aux valeurs positives sont touchés ; potions et hydromels gardent leur durée.
- Valeurs d'origine conservées : le multiplicateur se change à la volée et désactiver le mod rend le vanilla.

Réglage : `FoodDurationMultiplier` (1.5 par défaut) ; fichier `vmods.longerfood.cfg` ou Mod Hub (F9).
