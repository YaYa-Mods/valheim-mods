# Resource Finder

Find ores, pickables, trees, locations and creatures around you, pin them on the map and get guided to the nearest one.
The interface follows the game language (French or English).

## How it works

- **F7** opens the scanner window. Pick an entry in the catalogue (tabs: all, ores, pickables, trees, locations,
  creatures) or type a name in the search field (catalogue name or internal game name such as `Pickable_Thistle`,
  `Crypt`, `copper`).
- **Materials tab**: every item the world gives (bone fragments, resin, feathers, leather scraps...) that you have
  picked up at least once, with its sources read from the game's own drop tables (creatures, bone piles, rocks, trees,
  plants). Type `bones` or `os` and the scanner looks for skeletons and bone piles.
- The scanner first searches the **known world** (zones you have already explored). If it does not find enough results,
  a button offers to **search further**: unexplored zones are generated up to a radius (3 km by default), exactly as if
  you had walked there. Generated zones are saved in the world.
- Results are **pinned on the map** (one layer per search, kept between sessions), the nearest one is shown at the top
  of the list and pointed at on screen: a small pill above the object with its name and distance, a marker at the edge
  of the minimap when it is out of frame, and a glowing silhouette when the object is loaded.
- **F6** jumps to the next result of the current layer without opening the window. Right-click a result to see it on
  the large map.
- **Track mode (F4)**: as soon as the target is harvested or killed, a new search runs from where you stand and the
  nearest one from there becomes the target (not the next one of the list found at the start). When nothing is left,
  tracking stops. Press F4 again to stop.
- **Discovery**: by default the catalogue only shows what you have already seen, held or fought (a rock you looked at,
  a deer that ran away, a biome you visited). Undiscovered entries are counted and can be revealed one by one if you do
  not mind the spoiler.
- Creatures are found only when they are loaded around you. If none is loaded, the window tells you in which biomes
  the creature lives.

## Keys

| Key | Action |
|-----|--------|
| F7 | Open / close the scanner |
| F6 | Next target in the current layer |
| F4 | Track mode on / off |
| Enter | Search (in the window) |
| Right-click on a result | Show it on the map |

All of them are also reachable from the action wheel, group **Mods**, and from the mod buttons in the inventory
(both provided by Mod Hub).

## Configuration (`BepInEx/config/vmods.resourcefinder.cfg`)

| Section | Key | Default | Meaning |
|---------|-----|---------|---------|
| General | Enabled | true | Enable the mod |
| General | ToggleKey | F7 | Open / close the window |
| General | NextTargetKey | F6 | Next target |
| General | TrackKey | F4 | Track mode |
| General | ResultCount | 3 | Results wanted: the scan stops as soon as it has them |
| Scan | MaxScanRadius | 3000 | First scan radius (m) for unknown zones; 0 = never generate |
| Scan | ExtendedScanRadius | 10000 | Radius reached by the "search further" button |
| Scan | ScanBudgetMs | 8 | Milliseconds spent scanning per frame (more = faster, less smooth) |
| Display | ShowHud | true | On-screen indicator towards the target |
| Display | HighlightStyle | Lueur | Target highlight: Lueur (glow), Cadre (corner frame), LesDeux (both), Aucun (none) |
| Display | MinimapMarker | true | Marker at the edge of the minimap |
| Display | AddMapPins | true | Map pins for the results |
| Display | PinNames | false | Resource name under each pin |
| Display | MaxPinsPerLayer | 30 | Pins kept per layer (the nearest ones) |
| Display | HideUndiscovered | true | Immersion: only show what you have already discovered |
| Debug | DumpNames | true | Write the list of prefab and location names to `BepInEx/config/ResourceFinder.names.txt` once |

Everything can also be changed in game with Mod Hub (F9).

## Notes

- The mouse wheel scrolls the window's lists instead of zooming the camera, the way build mode frees it to rotate a piece.
- The on-screen pill stays fully visible: near a screen edge it moves inward and shows an arrow towards the target, and it never lands on the creature name plate the game draws, on the game HUD (health bars, hotbar, minimap, messages, boss bar) or on another mod's panel.
- Dungeon interiors (crypts, caves, infested mines) are ignored: the entrance is what gets pinned. Inside a burial
  chamber or a sunken crypt, the pill points at the nearest thing left (chest, surtling core, scrap pile) in that
  dungeon only; once every chest is empty and every core or pile is taken, the marker is removed and a new search no
  longer offers that dungeon. The contents are read from the world's saved objects, so a half-loaded interior never
  counts as cleared.
- The pill draws its text at the screen's real resolution: sharp at 1440p and 4K, not an enlarged 1080p image.
- The catalogue is checked against the game's own placement tables (spawn lists, vegetation, locations) by the test
  harness, so every entry can actually be found in a normal world.
- Layers and the "seen" list are stored in the character's custom data; removing the mod leaves nothing behind but that
  data, which the game ignores.

---

## En français

Trouve minerais, plantes, arbres, lieux et créatures autour de vous, les épingle sur la carte et vous guide vers le plus
proche. L'interface suit la langue du jeu.

- **F7** : fenêtre du scanner. Choisissez une entrée du catalogue (onglets : tout, minerais, cueillette, arbres, lieux,
  créatures) ou tapez un nom (nom du catalogue ou nom interne du jeu).
- **Onglet Matériaux** : chaque objet que le monde donne (fragments d'os, résine, plumes, bouts de cuir...) et que vous
  avez déjà ramassé une fois, avec ses sources lues dans les tables de butin du jeu (créatures, ossuaires, rochers,
  arbres, plantes). Tapez `os` : le scanner cherche les squelettes et les ossuaires.
- Le scanner cherche d'abord dans le **monde connu** ; s'il manque des résultats, un bouton propose de **chercher plus
  loin** en générant les zones inconnues (3 km par défaut), comme si vous y étiez passé.
- La molette fait défiler les listes de la fenêtre sans zoomer la caméra ; la pastille à l'écran reste toujours entièrement lisible (flèche vers la cible quand elle est hors champ) et ne se pose ni sur le nom de créature du jeu, ni sur le HUD (barres, barre d'action, mini-carte, messages, barre du boss), ni sur un autre panneau.
- Les résultats sont **épinglés sur la carte** (une couche par recherche, conservée entre les sessions) ; le plus proche
  est pointé à l'écran (pastille au-dessus de l'objet, repère au bord de la mini-carte, silhouette lumineuse).
- **F6** : cible suivante sans ouvrir la fenêtre. Clic droit sur un résultat : le voir sur la grande carte.
- **Traque (F4)** : dès que la cible est récoltée ou tuée, une nouvelle recherche part de là où vous êtes et la plus
  proche de vous devient la cible (pas la suivante de la liste trouvée au départ). Plus rien : la traque s'arrête.
- **Donjons** (chambres funéraires, cryptes des marais) : l'entrée est épinglée ; à l'intérieur, la pastille montre le
  plus proche de ce qui reste dans ce donjon (coffre, cœur de surtling, ferraille) ; coffres vides et cœurs ramassés,
  le repère disparaît et une nouvelle recherche ne le propose plus. Le contenu est lu dans la sauvegarde du monde : un
  intérieur encore en chargement n'est jamais pris pour vidé.
- Textes de la pastille nets à toutes les résolutions (1440p, 4K) : dessinés à la taille réelle de l'écran.
- **Découverte** : le catalogue ne montre que ce que vous avez déjà vu, tenu en main ou combattu. Le reste peut être
  révélé entrée par entrée.
- Les créatures ne sont trouvées que si elles sont chargées autour de vous ; sinon la fenêtre indique leurs biomes.

Configuration : `BepInEx/config/vmods.resourcefinder.cfg` (voir le tableau ci-dessus), ou en jeu avec Mod Hub (F9).
