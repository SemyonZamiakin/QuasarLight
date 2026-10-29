# Builds a self-contained release zip: dist/QuasarLight-<version>-win-x64.zip
# (no .NET install needed on the target PC; OpenRGB is NOT bundled - the app downloads and verifies it).
param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
& "$root\build-core.ps1"

$stage = Join-Path $root "dist\QuasarLight"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Push-Location "$root\ui\QuasarLight.App"
try {
    dotnet publish -c Release -r win-x64 --self-contained true -p:Platform=x64 `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -o $stage
    if ($LASTEXITCODE) { throw 'publish failed' }
} finally { Pop-Location }

if (-not (Test-Path "$stage\qlcore.dll")) { Copy-Item "$root\ui\QuasarLight.App\native\qlcore.dll" $stage }
Copy-Item "$root\LICENSE" $stage
Copy-Item "$root\README.md" $stage
Copy-Item "$root\docs" "$stage\docs" -Recurse

$zip = Join-Path $root "dist\QuasarLight-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
$h = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$h  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256"
"Built $zip ($([math]::Round((Get-Item $zip).Length/1MB,1)) MB)`nSHA256 $h"
