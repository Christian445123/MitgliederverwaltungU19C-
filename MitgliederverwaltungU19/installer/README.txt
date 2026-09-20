Installer fuer die Mitgliederverwaltung U19
===========================================

Fertiger Installer:   installer\output\MitgliederverwaltungU19-<Version>-x64.msi

Neuen Installer bauen (nach Aenderungen am Programm)
----------------------------------------------------
1. Version erhoehen: in MitgliederverwaltungU19.csproj  <Version>2.1.0</Version>
   (bei jedem neuen Installer eine hoehere Version, damit sich Updates installieren lassen)
2. Programm schliessen.
3. PowerShell im Ordner "installer" oeffnen und ausfuehren:
       .\build-installer.ps1
   Voraussetzung: .NET SDK und Internetverbindung (WiX wird als lokales Tool geladen).

Was der Installer macht
-----------------------
- installiert nach C:\Mitgliederverwaltung (Systemlaufwerk, Administratorrechte erforderlich); dort liegen alle Programmdateien inkl. .NET
- enthaelt .NET: auf dem Zielrechner muss nichts weiter installiert sein (Windows 10/11, 64 Bit)
- legt Verknuepfungen im Startmenue und auf dem Desktop an
- eine neuere Version ersetzt eine aeltere automatisch (Update): die Dateien im Ordner werden ausgetauscht, auch eine fruehere Installation unter Program Files wird ersetzt
- Deinstallation ueber "Einstellungen > Apps"
- Die Verbindungseinstellungen (Adresse, API-Schluessel) liegen pro Benutzer in
  %APPDATA%\MitgliederverwaltungU19 und bleiben bei Update und Deinstallation erhalten.

Hinweis: Der Installer ist nicht digital signiert. Windows SmartScreen kann deshalb beim ersten Start
"Unbekannter Herausgeber" anzeigen ("Weitere Informationen" > "Trotzdem ausfuehren").

Zugangsdaten bei der Installation
---------------------------------
Der Installer fragt nach: API-Adresse, API-Schluessel und Lizenzschluessel (alle drei Pflicht).
- Die Werte werden beim ersten Start der Anwendung uebernommen (pro Benutzer verschluesselt gespeichert)
  und danach aus der Registry (HKLM\Software\AFBOE U19\Mitgliederverwaltung\Setup) entfernt.
- Bei einem Update ueber die Anwendung (stille Installation) wird nicht erneut gefragt.
- Installation ohne Fenster (z. B. fuer mehrere Rechner):
      msiexec /i MitgliederverwaltungU19-2.4.1-x64.msi /passive API_URL=https://.../api API_KEY=... LICENSE_KEY=U19-XXXX-XXXX-XXXX-XXXX
