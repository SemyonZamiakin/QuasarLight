# Builds core/ into ui/QuasarLight.App/native/qlcore.dll (x64, statically linked runtime) and runs tests.
$ErrorActionPreference = 'Stop'
$gpp = 'C:\msys64\mingw64\bin\g++.exe'
if (-not (Test-Path $gpp)) { $gpp = (Get-Command g++ -ErrorAction Stop).Source }
$env:PATH = (Split-Path $gpp) + ';' + $env:PATH
$root = $PSScriptRoot
$out = Join-Path $root 'ui\QuasarLight.App\native'
New-Item -ItemType Directory -Force $out | Out-Null
$src = Get-ChildItem "$root\core\src\*.cpp" | ForEach-Object FullName
& $gpp -std=c++17 -O2 -Wall -Wextra -shared -static -I"$root\core\include" $src -o "$out\qlcore.dll"
if ($LASTEXITCODE) { throw 'dll build failed' }
& $gpp -std=c++17 -O2 -Wall -Wextra -static -I"$root\core\include" "$root\core\tests\test_core.cpp" $src -o "$root\core\tests\test_core.exe"
if ($LASTEXITCODE) { throw 'test build failed' }
& "$root\core\tests\test_core.exe"
if ($LASTEXITCODE) { throw 'tests failed' }
