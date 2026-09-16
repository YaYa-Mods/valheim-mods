# Mod Hub

One place to reach and configure every mod in game, without editing `.cfg` files or restarting.

## What it does

- **Configuration window (F9)**: lists every BepInEx plugin that exposes settings and lets you change them on the fly
  (toggles, numbers with their allowed range, keys with a "press a key" capture, enums). Changes are applied and saved
  immediately; each setting has a "default" button. Buttons at the bottom disable or re-enable all mods at once (the
  game then behaves as vanilla: wear, weight, stacks...) or reset everything to default. A text filter narrows the
  list.
- **Action wheel**: a **Mods** group in the game's radial menu with entries provided by the other mods (scanner, next
  target, track mode, guide, tracker mode, go home to bed, sort and stack inventory, config).
- **Inventory buttons**: a themed toolbar in the inventory panel with the same entries (icons drawn by the mods).
- **Key hints**: the mods' shortcuts appear in the game's own key-hint panel (bottom right) when the game shows none of
  its own, so they never cover the game's hints. At most four are shown, and they follow the situation (for example
  "Next target" and "Track" only while a target exists).
- **Disable everything**: the button gives the vanilla game back, panels included; re-enabling restores them.
- **Welcome hint**: once per session, a few seconds after arriving in the world, a short reminder of how to reach the
  mods.

Mods do not depend on the hub; they publish their entries through plain static methods (`RadialEntries()`,
`InventoryEntries()`, `KeyHints()`) that the hub reads by reflection. Every mod works alone.

## Keys

| Key | Action |
|-----|--------|
| F9 | Open / close the configuration window |

## Configuration (`BepInEx/config/vmods.modhub.cfg`)

| Key | Default | Meaning |
|-----|---------|---------|
| ToggleKey | F9 | Open / close the hub |
| WelcomeHint | true | Reminder of the mod shortcuts on arrival, once per session |
| ShowKeyHints | true | Mod key hints in the game's key-hint panel |

---

## En français

Un seul endroit pour accéder à tous les mods et les régler en jeu, sans éditer de fichier ni redémarrer.

- **Fenêtre de configuration (F9)** : tous les réglages de tous les plugins, modifiés à la volée et enregistrés
  aussitôt ; bouton « défaut » par réglage ; tout désactiver / réactiver / remettre par défaut ; filtre texte.
- **Roue d'action** : groupe **Mods** avec les entrées fournies par les autres mods.
- **Boutons dans l'inventaire** : barre d'outils avec les mêmes entrées.
- **Aides de touches** : les raccourcis des mods apparaissent dans le panneau d'aides du jeu quand celui-ci n'en
  affiche pas, quatre au plus, selon la situation.
- **Rappel d'accueil** : une fois par session, quelques secondes après l'arrivée dans le monde.

Les mods ne dépendent pas du hub : chacun fonctionne seul.
