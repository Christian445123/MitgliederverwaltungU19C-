using Microsoft.Win32;

namespace MitgliederverwaltungU19.Services;

/// <summary>
/// Übernimmt die im Installer eingegebenen Zugangsdaten (API-Adresse, API-Schlüssel, Lizenzschlüssel).
/// Der Installer legt sie unter HKLM\Software\AFBOE U19\Mitgliederverwaltung\Setup ab; die Anwendung kopiert sie beim
/// Start in ihre Einstellungen (dort pro Benutzer mit Windows-DPAPI verschlüsselt) und entfernt danach die
/// beiden Schlüssel aus der Registry, soweit ihre Rechte dafür reichen.
/// </summary>
public static class SetupImport
{
    public const string DefaultKeyPath = @"SOFTWARE\AFBOE U19\Mitgliederverwaltung\Setup";

    /// <returns>true, wenn neue Werte aus dem Installer übernommen wurden</returns>
    /// <param name="baseKey">Nur für Tests: anderer Registry-Zweig statt HKLM (64 Bit).</param>
    public static bool Apply(AppSettings s, RegistryKey? baseKey = null, string keyPath = DefaultKeyPath)
    {
        try
        {
            var ownsBase = baseKey is null;
            baseKey ??= RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            try
            {
                using var key = baseKey.OpenSubKey(keyPath, writable: false);
                if (key is null) return false;

                var stamp = key.GetValue("Stamp") as string ?? "";
                var url = key.GetValue("ApiUrl") as string ?? "";
                var apiKey = key.GetValue("ApiKey") as string ?? "";
                var license = key.GetValue("LicenseKey") as string ?? "";

                // Nur neue Installer-Eingaben übernehmen: gleicher Zeitstempel = schon erledigt, leere Schlüssel = schon entfernt
                if (stamp.Length == 0 || stamp == s.SetupStamp) return false;
                if (apiKey.Length == 0 && license.Length == 0) return false;

                if (!string.IsNullOrWhiteSpace(url)) s.BaseUrl = url.Trim();
                if (apiKey.Length > 0) s.Token = apiKey.Trim();
                if (license.Length > 0)
                {
                    s.LicenseKey = LicenseService.NormalizeKey(license);
                    s.LicenseLease = null;      // neuer Schlüssel: alte Freigabe verwerfen
                    s.LicenseOfflineSince = 0;
                    s.LicenseName = "";
                }
                s.SetupStamp = stamp;
                s.Save();

                // Schlüssel aus der Registry entfernen (klappt nur mit Administratorrechten, sonst bleiben sie stehen)
                try
                {
                    using var writable = baseKey.OpenSubKey(keyPath, writable: true);
                    writable?.DeleteValue("ApiKey", throwOnMissingValue: false);
                    writable?.DeleteValue("LicenseKey", throwOnMissingValue: false);
                }
                catch (Exception)
                {
                }
                return true;
            }
            finally
            {
                if (ownsBase) baseKey.Dispose();
            }
        }
        catch (Exception)
        {
            return false;
        }
    }
}
