# Localisation : enveloppe les chaînes françaises affichées dans L.T("…") (une passe, idempotente).
# Exclus : tests, Common, Catalog.cs (les libellés du catalogue sont des clés internes, traduits à l'affichage),
# lignes de journal, réflexion (GetField/GetMethod/Find…), attributs Harmony, comparaisons.
$root = Split-Path $PSScriptRoot -Parent
$files = Get-ChildItem -Path (Join-Path $root 'mods') -Recurse -Filter *.cs | Where-Object {
    $_.FullName -notmatch '[\\/](TestHarness|Common|bin|obj)[\\/]' -and $_.Name -ne 'Catalog.cs'
}
$rx = [regex]'(?<![\$@\\\w])"((?:[^"\\]|\\.)*)"'
$skip = 'GetField\(|GetMethod\(|GetType\(|GetProperty\(|\.Find\(|Log\.Log|LogInfo|LogWarning|LogError|GetManifestResourceStream|SetNextControlName|GetNameOfFocusedControl|Shader\.PropertyToID|StartsWith\(|IndexOf\(|Equals\(|GetStableHashCode|\.name ==|ToString\(\) ==|== "|Path\.Combine|Localize\(|PlayerPrefs|\[Harmony|ContainsKey\(|m_customData\[|const string|Config\.Bind\("[A-Za-z]+", "[A-Za-z]+", "[^"]*"\)$'
$total = 0
foreach ($f in $files) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName, [System.Text.Encoding]::UTF8)
    $changed = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*//' -or $line -match $skip) { continue }
        $new = $rx.Replace($line, {
            param($m)
            $s = $m.Groups[1].Value
            $hasAccent = $s -match '[éèêëàâçùûôîïÉÈÀÇ«»…]'
            $hasSpace = ($s -match ' ') -and ($s -match '[a-z]')
            if (-not ($hasAccent -or $hasSpace)) { return $m.Value }
            if ($s -match '^\$' -or $s -match '\.png$' -or $s -match '^\{\d\}$' -or $s -match '^<[^>]*>$') { return $m.Value }
            $script:total++
            return 'L.T(' + $m.Value + ')'
        })
        if ($new -ne $line) { $lines[$i] = $new; $changed = $true }
    }
    if ($changed) {
        if (-not (($lines -join "`n") -match 'using ModsCommon;')) {
            $idx = [Array]::FindLastIndex($lines, [Predicate[string]] { param($l) $l -match '^using ' })
            $lines = $lines[0..$idx] + @('using ModsCommon;') + $lines[($idx + 1)..($lines.Count - 1)]
        }
        [System.IO.File]::WriteAllLines($f.FullName, $lines, (New-Object System.Text.UTF8Encoding $false))
    }
}
"chaînes enveloppées : $total"
