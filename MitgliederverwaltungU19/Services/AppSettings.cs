using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MitgliederverwaltungU19.Services;

/// <summary>
/// Verbindungseinstellungen: Adresse der Web-API und API-Schlüssel. Der Schlüssel wird
/// mit Windows-DPAPI (nur für den aktuellen Windows-Benutzer lesbar) gespeichert.
/// </summary>
public sealed class AppSettings
{
    public string BaseUrl { get; set; } = "";
    public string TokenProtected { get; set; } = "";


    [JsonIgnore]
    public string Token
    {
        get
        {
            if (string.IsNullOrEmpty(TokenProtected)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(TokenProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                return "";
            }
        }
        set => TokenProtected = string.IsNullOrEmpty(value)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    // ── Lizenz ─────────────────────────────────────────────────────────────

    public string LicenseKeyProtected { get; set; } = "";

    /// <summary>Lizenzschlüssel (aus dem Web-Bereich „Lizenzen“), per DPAPI geschützt gespeichert.</summary>
    [JsonIgnore]
    public string LicenseKey
    {
        get
        {
            if (string.IsNullOrEmpty(LicenseKeyProtected)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(LicenseKeyProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                return "";
            }
        }
        set => LicenseKeyProtected = string.IsNullOrEmpty(value)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    /// <summary>Zuletzt vom Server signierte Offline-Freigabe (höchstens 3 Tage gültig).</summary>
    public LicenseLease? LicenseLease { get; set; }

    /// <summary>Öffentlicher Schlüssel des Servers zur Prüfung der Signatur (PEM).</summary>
    public string LicensePublicKey { get; set; } = "";

    public string LicenseName { get; set; } = "";

    /// <summary>Spätester bekannter Zeitpunkt (Unix-Sekunden), um zurückgestellte Systemuhren zu erkennen.</summary>
    public long LicenseLastSeen { get; set; }

    /// <summary>Beginn der automatischen Offline-Lizenz (Unix-Sekunden, 0 = nicht aktiv). Gilt höchstens 3 Tage.</summary>
    public long LicenseOfflineSince { get; set; }

    /// <summary>Zeitstempel der zuletzt übernommenen Installer-Eingaben (siehe SetupImport).</summary>
    public string SetupStamp { get; set; } = "";

    // ── Anmeldung (Benutzerdaten des Web-Panels) ───────────────────────────

    /// <summary>Zuletzt angemeldeter Benutzername (wird im Anmeldefenster vorbelegt).</summary>
    public string LastUsername { get; set; } = "";

    public string SessionTokenProtected { get; set; } = "";

    /// <summary>Sitzungs-Token bei „Angemeldet bleiben“ (per DPAPI geschützt); leer = beim nächsten Start neu anmelden.</summary>
    [JsonIgnore]
    public string SessionToken
    {
        get
        {
            if (string.IsNullOrEmpty(SessionTokenProtected)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(SessionTokenProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                return "";
            }
        }
        set => SessionTokenProtected = string.IsNullOrEmpty(value)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    // ── Programm-Updates (GitHub Releases) ─────────────────────────────────

    /// <summary>GitHub-Repository der Anwendung im Format "Besitzer/Repository".</summary>
    public string GitHubRepo { get; set; } = "Christian445123/MitgliederverwaltungU19C-";

    /// <summary>Beim Start automatisch nach einer neuen Version suchen.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    public string GitHubTokenProtected { get; set; } = "";

    /// <summary>Nur bei privatem Repository nötig (Lesezugriff auf Releases). Wird per DPAPI geschützt gespeichert.</summary>
    [JsonIgnore]
    public string GitHubToken
    {
        get
        {
            if (string.IsNullOrEmpty(GitHubTokenProtected)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(GitHubTokenProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                return "";
            }
        }
        set => GitHubTokenProtected = string.IsNullOrEmpty(value)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrEmpty(Token);

    /// <summary>Nur für Tests: anderer Speicherort statt %APPDATA%.</summary>
    public static string? PathOverride { get; set; }

    private static string FilePath => PathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MitgliederverwaltungU19", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // defekte Datei -> Standardwerte
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
