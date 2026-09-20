# Baut den Installer (MSI) fuer die Mitgliederverwaltung U19.
#
# Aufruf (PowerShell, im Ordner "installer" oder von ueberall):
#   .\build-installer.ps1
#
# Ablauf:
#   1. Programm als eigenstaendige EXE veroeffentlichen (enthaelt .NET, auf dem Zielrechner ist nichts weiter noetig)
#   2. MSI mit WiX bauen (lokales dotnet-Tool, siehe dotnet-tools.json)
# Ergebnis: installer\output\MitgliederverwaltungU19-<Version>-x64.msi

$ErrorActionPreference = 'Stop'
$installerDir = $PSScriptRoot
$projectDir = Split-Path $installerDir -Parent
$csproj = Join-Path $projectDir 'MitgliederverwaltungU19.csproj'

# Version aus der .csproj (z. B. 2.1.0)
[xml]$xml = Get-Content $csproj -Encoding UTF8
$version = ($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'Keine <Version> in der .csproj gefunden.' }
# MSI-Versionen brauchen genau Major.Minor.Build
$parts = $version.Split('.')
while ($parts.Count -lt 3) { $parts += '0' }
$msiVersion = ($parts[0..2] -join '.')

Write-Host "Version $msiVersion" -ForegroundColor Cyan

# Geloescht wird mit Wiederholungen (Virenscanner/Dropbox sperren frisch geschriebene Dateien manchmal kurz)
function Remove-Folder([string]$path) {
    for ($i = 1; $i -le 6; $i++) {
        if (-not (Test-Path $path)) { return }
        try { Remove-Item $path -Recurse -Force -ErrorAction Stop; return }
        catch { Start-Sleep -Seconds 2 }
    }
    if (Test-Path $path) { throw "Ordner kann nicht geloescht werden (in Benutzung): $path" }
}

# Gebaut wird komplett in einem temporaeren Ordner AUSSERHALB von Dropbox und ohne Sonderzeichen im Pfad
# (WiX kommt mit "#" in "MitgliederverwaltungU19_C#" nicht zurecht, und Dropbox sperrt die vielen Dateien beim Synchronisieren).
$stage = Join-Path $env:TEMP 'U19Installer'
Remove-Folder $stage
$stageInstaller = Join-Path $stage 'installer'
$publishDir = Join-Path $stageInstaller 'publish'
New-Item -ItemType Directory -Force $publishDir | Out-Null
Copy-Item (Join-Path $installerDir 'Package.wxs') $stageInstaller
Copy-Item (Join-Path $installerDir 'dotnet-tools.json') $stageInstaller
Copy-Item (Join-Path $projectDir 'app.ico') $stage

# 1) Veroeffentlichen (Programm mit .NET, viele einzelne Dateien)
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -p:SatelliteResourceLanguages=en `
    -o $publishDir -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fehlgeschlagen.' }

# 2) MSI bauen und ins Projekt zurueckkopieren

Push-Location $stageInstaller
try {
    dotnet tool restore | Out-Null
    dotnet wix extension add WixToolset.UI.wixext/5.0.2 | Out-Null
    $msiName = "MitgliederverwaltungU19-$msiVersion-x64.msi"
    dotnet wix build Package.wxs -arch x64 -d ProductVersion=$msiVersion `
        -ext WixToolset.UI.wixext -culture de-DE -o $msiName
    if ($LASTEXITCODE -ne 0) { throw 'WiX-Build fehlgeschlagen.' }

    $outDir = Join-Path $installerDir 'output'
    New-Item -ItemType Directory -Force $outDir | Out-Null
    Copy-Item $msiName $outDir -Force
    Write-Host "Fertig: $(Join-Path $outDir $msiName)" -ForegroundColor Green
}
finally {
    Pop-Location
}
