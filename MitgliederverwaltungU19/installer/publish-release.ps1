# Veroeffentlicht die aktuelle Version als GitHub-Release, damit sich installierte Anwendungen selbst aktualisieren.
#
# Voraussetzungen:
#   - Der Code ist auf GitHub hochgeladen (committet und gepusht).
#   - Ein GitHub-Token mit Schreibrecht auf "Contents" (Fine-grained token) oder "repo" (klassisch),
#     entweder in der Umgebungsvariable GITHUB_TOKEN oder als Parameter -Token.
#
# Aufruf:
#   $env:GITHUB_TOKEN = 'ghp_...'
#   .\publish-release.ps1 -Notes "Neu: Staff-Bereich, fehlende Dokumente ..."
#
# Ohne Skript geht es auch von Hand: github.com/<Besitzer>/<Repo> > Releases > "Create a new release",
# Tag "v<Version>" (z. B. v2.2.0) eintragen und die .msi aus installer\output hochladen.

param(
    [string]$Repo = 'Christian445123/MitgliederverwaltungU19C-',
    [string]$Token = $env:GITHUB_TOKEN,
    [string]$Notes = ''
)

$ErrorActionPreference = 'Stop'
if (-not $Token) { throw 'Kein GitHub-Token: $env:GITHUB_TOKEN setzen oder -Token angeben.' }

# 1) Installer bauen
& (Join-Path $PSScriptRoot 'build-installer.ps1')

$csproj = Join-Path (Split-Path $PSScriptRoot -Parent) 'MitgliederverwaltungU19.csproj'
[xml]$xml = Get-Content $csproj -Encoding UTF8
$version = ($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
$parts = $version.Split('.'); while ($parts.Count -lt 3) { $parts += '0' }
$version = ($parts[0..2] -join '.')
$tag = "v$version"
$msi = Join-Path $PSScriptRoot "output\MitgliederverwaltungU19-$version-x64.msi"
if (-not (Test-Path $msi)) { throw "Installer nicht gefunden: $msi" }

$headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'MitgliederverwaltungU19-Release'
}

# 2) Release anlegen (der Tag wird vom letzten Stand des Standard-Branches erzeugt)
$body = @{ tag_name = $tag; name = "Version $version"; body = $Notes; draft = $false; prerelease = $false } | ConvertTo-Json
Write-Host "Erstelle Release $tag ..." -ForegroundColor Cyan
$release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$Repo/releases" -Headers $headers -Body $body -ContentType 'application/json'

# 3) Installer anhaengen
$name = [IO.Path]::GetFileName($msi)
Write-Host "Lade $name hoch ..." -ForegroundColor Cyan
Invoke-RestMethod -Method Post -Uri "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$name" `
    -Headers $headers -InFile $msi -ContentType 'application/octet-stream' | Out-Null

Write-Host "Fertig: $($release.html_url)" -ForegroundColor Green
