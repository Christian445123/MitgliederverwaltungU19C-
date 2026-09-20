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

    /// <summary>Ordner des Web-Projekts (Git-Repository) für "Änderungen einspielen".</summary>
    public string RepoPath { get; set; } = "";

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

    private static string FilePath => Path.Combine(
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
