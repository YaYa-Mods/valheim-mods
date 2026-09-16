# Localisation : remplace les chaînes interpolées françaises par des modèles L.F (remplacements littéraux, un seul usage).
$root = Split-Path $PSScriptRoot -Parent
$pairs = @(
  @('ResourceFinder\Finder.cs',
    '$"Scan… {ScanRadius:0} m, {ZonesGenerated} zones générées, {ZonesFiltered} ignorées (biome), {Results.Count} trouvé(s)"',
    'L.F("Scan… {0:0} m, {1} zones générées, {2} ignorées (biome), {3} trouvé(s)", ScanRadius, ZonesGenerated, ZonesFiltered, Results.Count)'),
  @('ResourceFinder\Finder.cs', '$"Scan arrêté à {ScanRadius:0} m."', 'L.F("Scan arrêté à {0:0} m.", ScanRadius)'),
  @('ResourceFinder\Plugin.cs', '$"Traque : {DisplayName(_target)} à {_target.Distance(from):0} m"', 'L.F("Traque : {0} à {1:0} m", DisplayName(_target), _target.Distance(from))'),
  @('ResourceFinder\Plugin.cs', '$"Traque activée{(_currentEntry != null ? " : " + _currentEntry.Label : "")}, {TrackKey.Value} pour arrêter"', 'L.T("Traque activée") + (_currentEntry != null ? " : " + L.T(_currentEntry.Label) : "") + L.F(", {0} pour arrêter", TrackKey.Value)'),
  @('ResourceFinder\Plugin.cs', '$"Cible {i + 2}/{list.Count} : {DisplayName(_target)}, {_target.Distance(from):0} m"', 'L.F("Cible {0}/{1} : {2}, {3:0} m", i + 2, list.Count, DisplayName(_target), _target.Distance(from))'),
  @('ResourceFinder\Plugin.cs', '$"Traque : {DisplayName(_target)} à {_target.Distance(player.transform.position):0} m"', 'L.F("Traque : {0} à {1:0} m", DisplayName(_target), _target.Distance(player.transform.position))'),
  @('ResourceFinder\Plugin.cs', '$"Scanner : {_finder.Results.Count} × {_finder.Label}, le plus proche à {_target.Distance(player.transform.position):0} m"', 'L.F("Scanner : {0} × {1}, le plus proche à {2:0} m", _finder.Results.Count, L.T(_finder.Label), _target.Distance(player.transform.position))'),
  @('ResourceFinder\Plugin.cs', '$"Scanner : rien trouvé pour {_finder.Label}"', 'L.F("Scanner : rien trouvé pour {0}", L.T(_finder.Label))'),
  @('ResourceFinder\Plugin.cs', '$" Non découverts ({_hiddenEntries.Count}), révéler brise l''immersion"', 'L.F(" Non découverts ({0}), révéler brise l''immersion", _hiddenEntries.Count)'),
  @('ResourceFinder\Plugin.cs', '$"Traque en cours  ({TrackKey.Value} : arrêter)"', 'L.F("Traque en cours  ({0} : arrêter)", TrackKey.Value)'),
  @('ResourceFinder\Plugin.cs', '$"Traquer  ({TrackKey.Value})"', 'L.F("Traquer  ({0})", TrackKey.Value)'),
  @('ResourceFinder\Plugin.cs', '$"<b>{_finder.Label}</b>  <color=#cfcabf>-</color>  {_finder.Status}"', '$"<b>{L.T(_finder.Label)}</b>  <color=#cfcabf>-</color>  {_finder.Status}"'),
  @('ResourceFinder\Plugin.cs', '$"Vit dans : <b>{where}</b>, allez-y, elle sera détectée une fois la zone chargée."', 'L.F("Vit dans : <b>{0}</b>, allez-y, elle sera détectée une fois la zone chargée.", where)'),
  @('ResourceFinder\Plugin.cs', '$"Seulement {_finder.Results.Count}/{ResultCount.Value} dans {_finder.EffectiveRadius:0} m."', 'L.F("Seulement {0}/{1} dans {2:0} m.", _finder.Results.Count, ResultCount.Value, _finder.EffectiveRadius)'),
  @('ResourceFinder\Plugin.cs', '$"Chercher plus loin (jusqu''à {ExtendedScanRadius.Value / 1000f:0.#} km, ~{zones} zones, ≈ {eta})"', 'L.F("Chercher plus loin (jusqu''à {0:0.#} km, ~{1} zones, ≈ {2})", ExtendedScanRadius.Value / 1000f, zones, eta)'),
  @('ResourceFinder\Plugin.cs', '$"Résultats  <size=12><color=#cfcabf>({_shown.Count})</color></size>"', 'L.T("Résultats") + $"  <size=12><color=#cfcabf>({_shown.Count})</color></size>"'),
  @('ResourceFinder\Plugin.cs', '$"Couches sur la carte  <size=12><color=#cfcabf>({Layers.All.Count}), positions connues, conservées entre les sessions</color></size>"', 'L.T("Couches sur la carte") + $"  <size=12><color=#cfcabf>({Layers.All.Count}), " + L.T("positions connues, conservées entre les sessions") + "</color></size>"'),
  @('ResourceFinder\Plugin.cs', '$"{l.Label}  <color=#d6d1c6>({l.Results.Count})</color>"', '$"{L.T(l.Label)}  <color=#d6d1c6>({l.Results.Count})</color>"'),
  @('ResourceFinder\Plugin.cs', '$" (cueilli, repousse dans {remaining:0} min)"', 'L.F(" (cueilli, repousse dans {0:0} min)", remaining)'),
  @('ResourceFinder\Plugin.cs', '"ici"', 'L.T("ici")'),
  @('Guide\Chapters.cs', '$"Rapporter {fallbackCount * multiplier} × {fallbackItem}"', 'L.F("Rapporter {0} × {1}", fallbackCount * multiplier, fallbackItem)'),
  @('Guide\Chapters.cs', '$"Rapporter {o.Count * multiplier} × {Facts.ItemLabel(o.Item)}"', 'L.F("Rapporter {0} × {1}", o.Count * multiplier, Facts.ItemLabel(o.Item))'),
  @('Guide\Chapters.cs', '$"Rapporter {o.Count * 2} × {Facts.ItemLabel(o.Item)}"', 'L.F("Rapporter {0} × {1}", o.Count * 2, Facts.ItemLabel(o.Item))'),
  @('Guide\Plugin.cs', '$"Chapitre terminé : {c.DisplayTitle}"', 'L.F("Chapitre terminé : {0}", c.DisplayTitle)'),
  @('Guide\Plugin.cs', '$"… {rest} chapitre(s) à venir"', 'L.F("… {0} chapitre(s) à venir", rest)'),
  @('Guide\Plugin.cs', '$"Chapitre {idx + 1}, verrouillé"', 'L.F("Chapitre {0}, verrouillé", idx + 1)'),
  @('Guide\Plugin.cs', '$"<b>Échap</b> ou <b>{ToggleKey.Value}</b> fermer    <b>{TrackerKey.Value}</b> mode du suivi    <b>Cibler</b> lance le scanner et ferme le guide"', 'L.F("<b>Échap</b> ou <b>{0}</b> fermer    <b>{1}</b> mode du suivi    <b>Cibler</b> lance le scanner et ferme le guide", ToggleKey.Value, TrackerKey.Value)'),
  @('HomeTeleport\Plugin.cs', '$"Retour possible dans {Cooldown.Value - since:0} s"', 'L.F("Retour possible dans {0:0} s", Cooldown.Value - since)'),
  @('Inventory\Death.cs', '$"{recoveredItems} objet(s) récupéré(s) de votre tombe"', 'L.F("{0} objet(s) récupéré(s) de votre tombe", recoveredItems)'),
  @('Inventory\Grid.cs', '$"Piles fusionnées ({toRemove.Count} case(s) libérée(s))"', 'L.F("Piles fusionnées ({0} case(s) libérée(s))", toRemove.Count)'),
  @('ModHub\Plugin.cs', '$"Mods réactivés ({n})"', 'L.F("Mods réactivés ({0})", n)'),
  @('ModHub\Plugin.cs', '$"MODS DÉSACTIVÉS ({n}), jeu vanilla (usure, poids, piles...)"', 'L.F("MODS DÉSACTIVÉS ({0}), jeu vanilla (usure, poids, piles...)", n)')
)
$done = 0
foreach ($p in $pairs) {
  $path = Join-Path $root ('mods\' + $p[0])
  $c = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
  if ($c.Contains($p[1])) { $c = $c.Replace($p[1], $p[2]); [System.IO.File]::WriteAllText($path, $c, (New-Object System.Text.UTF8Encoding $false)); $done++ }
  else { Write-Host ("NON TROUVE : " + $p[0] + " :: " + $p[1]) }
}
"remplacements : $done / $($pairs.Count)"
