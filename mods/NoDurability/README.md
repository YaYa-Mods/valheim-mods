# No Durability

Weapons, tools, armour and torches never wear out.

## What it does

- Turns off durability on every item of the game (`m_useDurability` on each prefab's shared data): nothing wears,
  nothing breaks, and the durability bar disappears from the inventory.
- Vanilla values are kept, so the mod can be disabled on the fly from Mod Hub and everything comes back.

## Configuration (`BepInEx/config/vmods.nodurability.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| Enabled | true | Enable the mod |

---

## En français

Armes, outils, armures et torches ne s'usent plus.

- Désactive la durabilité sur tous les objets du jeu : plus d'usure, plus de casse, plus de barre de durabilité.
- Valeurs vanilla conservées : désactivable à la volée depuis Mod Hub.

Réglage : `Enabled` ; fichier `vmods.nodurability.cfg`.
