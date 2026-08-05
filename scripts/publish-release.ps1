# Build a Release zip for GitHub Releases (does NOT include NFSU2HUD art).
param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

$Out = Join-Path $Root "dist\Nfsu2ForzaHud-$Version-$Runtime"
$Zip = Join-Path $Root "dist\Nfsu2ForzaHud-$Version-$Runtime.zip"

Remove-Item -Recurse -Force (Join-Path $Root "dist") -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Out | Out-Null

dotnet publish -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Out

New-Item -ItemType Directory -Force -Path (Join-Path $Out "Assets") | Out-Null
Copy-Item (Join-Path $Root "ASSETS.md") (Join-Path $Out "ASSETS.md") -Force
Copy-Item (Join-Path $Root "README.md") (Join-Path $Out "README.md") -Force
Copy-Item (Join-Path $Root "LICENSE") (Join-Path $Out "LICENSE") -Force
if (Test-Path (Join-Path $Root "app.ico")) {
    Copy-Item (Join-Path $Root "app.ico") (Join-Path $Out "app.ico") -Force
}

# Placeholder so users see where art goes
$hint = Join-Path $Out "Assets\AcHud\PUT_NFSU2HUD_IMG_CONTENTS_HERE.txt"
New-Item -ItemType Directory -Force -Path (Split-Path $hint) | Out-Null
Set-Content -Path $hint -Value "Copy the contents of NFSU2HUD 3.0 apps/python/NFSU2HUD/img into this folder. See ASSETS.md."

if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path (Join-Path $Out "*") -DestinationPath $Zip -Force

Write-Host "Published: $Out"
Write-Host "Zip:       $Zip"
Write-Host "Upload this zip to the GitHub Release (art not included)."
