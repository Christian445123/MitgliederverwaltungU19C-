# Mitgliederverwaltung U19 – Windows-Programm

Desktop-Client (C# / WinForms / .NET 10) für die Web-Anwendung im Ordner `U19`.
Das Programm greift **nicht direkt auf die Datenbank** zu, sondern über die
REST-API der Web-Anwendung (`/api`). Datenbank-Zugangsdaten liegen dadurch
nur auf dem Server.

## Funktionen

- Mitgliederliste mit Sofortsuche, Statusfilter und sortierbaren Spalten
- Mitglieder anlegen, bearbeiten, löschen (alle Felder, gruppiert in Registerkarten)
- Import aus **Excel (.xlsx)** und **CSV** mit Vorschau (Prüfung auf dem Server)
- Export als CSV (öffnet direkt in Excel), Import-Vorlage zum Herunterladen
- API-Schlüssel wird per Windows-DPAPI verschlüsselt gespeichert
  (`%APPDATA%\MitgliederverwaltungU19\settings.json`)

## Einrichtung

1. In der Web-Anwendung als Administrator unter **API-Zugang** einen Schlüssel
   erstellen („Lesen & Schreiben“ für volle Funktion, „Nur lesen“ für Ansicht/Export).
2. Programm starten, API-Adresse (z. B. `https://verein.example.at/api`) und
   Schlüssel eintragen, „Verbindung testen“, speichern.

## Bauen

Voraussetzung: .NET 10 SDK (Windows).

```
dotnet build
dotnet publish -c Release -r win-x64 --self-contained false
```

Der Zielrechner benötigt die .NET-10-Desktop-Runtime (oder Publish mit
`--self-contained true` verwenden).
