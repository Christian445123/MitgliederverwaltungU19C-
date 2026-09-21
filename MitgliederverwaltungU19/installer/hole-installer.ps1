# Holt den neuesten fertigen Installer (MSI) vom GitHub-Release in den Ordner installer\output.
#
# Aufruf (nach dem Push wartet das Skript, bis die automatische Pipeline das Release veroeffentlicht hat):
#   .\hole-installer.ps1              # neuestes Release (wartet, falls gerade gebaut wird)
#   .\hole-installer.ps1 -Push        # vorher committete Aenderungen pushen, dann warten und laden
#
# Danach liegt die MSI in installer\output, und "git pull" holt die automatisch erhoehte Versionsnummer.

param(
    [string]$Repo = 'Christian445123/MitgliederverwaltungU19C-',
    [switch]$Push,
    [int]$TimeoutMinuten = 20
)

$ErrorActionPreference = 'Stop'
$outDir = Join-Path $PSScriptRoot 'output'
New-Item -ItemType Directory -Force $outDir | Out-Null
$repoDir = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

$startRelease = $null
try { $startRelease = (Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'U19' }).tag_name } catch {}

if ($Push) {
    git -C $repoDir push origin HEAD
    if ($LASTEXITCODE -ne 0) { throw 'git push fehlgeschlagen.' }
    Write-Host "Warte auf das neue Release (bisher: $startRelease) ..." -ForegroundColor Cyan
}

$ende = (Get-Date).AddMinutes($TimeoutMinuten)
do {
    $rel = $null
    try { $rel = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'U19' } } catch {}
    if ($rel -and (-not $Push -or $rel.tag_name -ne $startRelease)) { break }
    $rel = $null
    Start-Sleep -Seconds 15
} while ((Get-Date) -lt $ende)
if (-not $rel) { throw 'Kein neues Release gefunden (Pipeline noch nicht fertig oder fehlgeschlagen: GitHub > Actions).' }

$asset = $rel.assets | Where-Object { $_.name -like '*.msi' } | Select-Object -First 1
if (-not $asset) { throw "Release $($rel.tag_name) enthaelt keine .msi." }
$ziel = Join-Path $outDir $asset.name
Invoke-WebRequest $asset.browser_download_url -OutFile $ziel -Headers @{ 'User-Agent' = 'U19' }
Write-Host "Fertig: $ziel" -ForegroundColor Green
Write-Host "`n$($rel.body)"
git -C $repoDir pull --rebase origin master | Out-Null
