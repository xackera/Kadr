# Сборка установщика Kadr: самодостаточная публикация + MSI.
# Требуется .NET SDK и WiX:  dotnet tool install --global wix
param(
    [string]$Version = "1.0.0",
    [switch]$SkipPublish
)
$ErrorActionPreference = "Stop"

$root      = Split-Path $PSScriptRoot -Parent
$publish   = Join-Path $root "publish"
$dist      = Join-Path $root "dist"
$wix       = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
if (-not (Test-Path $wix)) { $wix = "wix" }

if (-not $SkipPublish) {
    Write-Host "Публикация самодостаточной сборки..."
    Get-Process Kadr, Kadr.Recorder -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
    dotnet publish (Join-Path $root "src\Kadr.Recorder\Kadr.Recorder.csproj") -c Release -r win-x64 --self-contained true -o $publish --nologo -v q
    dotnet publish (Join-Path $root "src\Kadr.App\Kadr.App.csproj")          -c Release -r win-x64 --self-contained true -o $publish --nologo -v q
    Copy-Item (Join-Path $root "LICENSE"), (Join-Path $root "THIRD-PARTY-NOTICES.md") $publish -Force
}

New-Item -ItemType Directory -Force $dist | Out-Null
$msi = Join-Path $dist "Kadr-$Version-x64.msi"

Write-Host "Сборка $msi ..."
& $wix build (Join-Path $PSScriptRoot "Kadr.wxs") `
    -arch x64 `
    -culture ru-RU `
    -ext WixToolset.UI.wixext `
    -ext WixToolset.Util.wixext `
    -d Version=$Version `
    -d PublishDir=$publish `
    -o $msi
if ($LASTEXITCODE -ne 0) { throw "Сборка установщика не удалась" }

$size = [math]::Round((Get-Item $msi).Length / 1MB, 1)
Write-Host "Готово: $msi ($size МБ)"
