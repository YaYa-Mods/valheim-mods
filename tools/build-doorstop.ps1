# Compile UnityDoorstop (winhttp.dll) depuis tools\UnityDoorstop-src avec MSVC, sans xmake.
# Reproduit ce que fait xmake.lua : génération de proxy.c / dll.def / info.rc, puis cl + link.
# Prérequis : Visual Studio Build Tools 2022 avec la charge de travail C++.
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'UnityDoorstop-src'
$build = Join-Path $src 'build'
New-Item -ItemType Directory -Force $build | Out-Null

# --- génération des fichiers que xmake produit (proxygen.lua / rcgen.lua) ---
$funcs = Get-Content (Join-Path $src 'src\windows\proxy\proxylist.txt') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
$proxy = @('/* GENERATED from proxylist.txt */', '#include <windows.h>', '')
$proxy += $funcs | ForEach-Object { "static FARPROC __${_}__;" }
$proxy += '', 'void load_functions(void *dll)', '{'
$proxy += $funcs | ForEach-Object { "__${_}__ = GetProcAddress((HMODULE)dll, `"$_`");" }
$proxy += '}', ''
$proxy += $funcs | ForEach-Object { "void *exp_${_}() { return __${_}__(); }" }
Set-Content -Encoding ascii (Join-Path $build 'proxy.c') $proxy
Set-Content -Encoding ascii (Join-Path $build 'dll.def') (@('EXPORTS') + ($funcs | ForEach-Object { "$_ = exp_$_" }))

$rc = Get-Content (Join-Path $src 'src\windows\build_tools\info.rc.in') -Raw
$rc = $rc -replace '\$\{MAJOR\}', '4' -replace '\$\{MINOR\}', '5' -replace '\$\{PATCH\}', '0' -replace '\$\{RELEASE\}', '' `
          -replace '\$\{NAME\}', 'Doorstop' -replace '\$\{DESCRIPTION\}', '.NET code preloader for Unity' -replace '\$\{ORGANIZATION\}', 'NeighTools'
Set-Content -Encoding ascii (Join-Path $build 'info.rc') $rc

# --- compilation (flags identiques à xmake.lua : /O1 = "smallest", /GS- /Ob2 /MT /GL-, link sans CRT) ---
$vcvars = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
$cmd = "`"$vcvars`" >nul && cd /d `"$src`" && " +
  'rc /nologo /fo build\info.res build\info.rc && ' +
  'cl /nologo /c /O1 /GS- /Ob2 /MT /GL- /FS /DUNICODE /D_UNICODE /DNDEBUG /Fobuild\ src\*.c src\config\*.c src\util\*.c src\runtimes\*.c src\windows\*.c build\proxy.c && ' +
  'link /nologo /DLL /NODEFAULTLIB /ENTRY:DllEntry /DYNAMICBASE:NO /DEF:build\dll.def /OUT:build\winhttp.dll build\*.obj build\info.res shell32.lib kernel32.lib user32.lib'
cmd /c $cmd
if ($LASTEXITCODE -ne 0) { throw "Build Doorstop échoué ($LASTEXITCODE)" }
Write-Host "OK -> $build\winhttp.dll"
Get-FileHash (Join-Path $build 'winhttp.dll') -Algorithm SHA256
