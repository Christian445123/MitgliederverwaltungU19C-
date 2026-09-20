using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace MitgliederverwaltungU19.Services;

/// <summary>Vom Server signierte Offline-Freigabe (siehe includes/licenses.php).</summary>
public sealed class LicenseLease
{
    public string KeyHash { get; set; } = "";
    public string MachineId { get; set; } = "";
    public long IssuedAt { get; set; }
    public long ValidUntil { get; set; }
    public string Signature { get; set; } = "";
}

/// <summary>Antwort des Servers auf POST license/validate.</summary>
public sealed class LicenseResponse
{
    public bool Valid { get; set; }
    public string Reason { get; set; } = "";
    public string Message { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ExpiresAt { get; set; }
    public LicenseLease? Lease { get; set; }
    public string PublicKey { get; set; } = "";
    public long ServerTime { get; set; }

    public static LicenseResponse FromJson(JsonElement e)
    {
        var r = new LicenseResponse
        {
            Valid = e.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True,
            Reason = e.TryGetProperty("reason", out var re) ? re.GetString() ?? "" : "",
            Message = e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
            PublicKey = e.TryGetProperty("public_key", out var pk) ? pk.GetString() ?? "" : "",
            ServerTime = e.TryGetProperty("server_time", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt64() : 0,
        };
        if (e.TryGetProperty("license", out var l) && l.ValueKind == JsonValueKind.Object)
        {
            r.Name = l.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            r.ExpiresAt = l.TryGetProperty("expires_at", out var ex) && ex.ValueKind == JsonValueKind.String ? ex.GetString() : null;
        }
        if (e.TryGetProperty("lease", out var le) && le.ValueKind == JsonValueKind.Object)
        {
            r.Lease = new LicenseLease
            {
                KeyHash = le.GetProperty("key_hash").GetString() ?? "",
                MachineId = le.GetProperty("machine_id").GetString() ?? "",
                IssuedAt = le.GetProperty("issued_at").GetInt64(),
                ValidUntil = le.GetProperty("valid_until").GetInt64(),
                Signature = e.TryGetProperty("signature", out var sig) ? sig.GetString() ?? "" : "",
            };
        }
        return r;
    }
}

public enum LicenseStatus
{
    /// <summary>Vom Server soeben bestätigt.</summary>
    Valid,
    /// <summary>Server nicht erreichbar, gültige Offline-Freigabe (höchstens 3 Tage).</summary>
    OfflineGrace,
    /// <summary>Kein Schlüssel hinterlegt.</summary>
    NoKey,
    /// <summary>Server lehnt den Schlüssel ab (gesperrt, abgelaufen, unbekannt, Gerätelimit).</summary>
    Denied,
    /// <summary>Server nicht erreichbar und keine (gültige) Offline-Freigabe mehr.</summary>
    OfflineExpired,
}

public sealed record LicenseCheck(LicenseStatus Status, string Message, DateTimeOffset? ValidUntil = null)
{
    public bool Allowed => Status is LicenseStatus.Valid or LicenseStatus.OfflineGrace;
}

/// <summary>
/// Lizenzprüfung: fragt den Server regelmäßig, ob der Schlüssel gilt. Bei Verbindungsabbruch läuft die Anwendung mit der
/// zuletzt signierten Freigabe höchstens 3 Tage weiter. Die Signatur (RSA/SHA-256) und ein Zeitrückstell-Schutz
/// verhindern, dass die lokale Freigabe verlängert wird.
/// </summary>
public static class LicenseService
{
    private const int ClockToleranceSeconds = 600;

    /// <summary>Fest eingebaute Offline-Lizenz: bei allen Installationen gleich, wird automatisch eingetragen.</summary>
    public const string OfflineLicenseKey = "U19-OFFLINE-72H";

    /// <summary>Höchstdauer der Offline-Lizenz: 3 Tage.</summary>
    public const int OfflineLicenseSeconds = 3 * 24 * 3600;

    private static string? _machineId;

    /// <summary>Stabile Kennung dieses Rechners (SHA-256 der Windows-MachineGuid).</summary>
    public static string MachineId => _machineId ??= ComputeMachineId();

    public static string MachineName => Environment.MachineName;

    private static string ComputeMachineId()
    {
        string guid;
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            guid = key?.GetValue("MachineGuid") as string ?? "";
        }
        catch (Exception)
        {
            guid = "";
        }
        if (string.IsNullOrEmpty(guid)) guid = Environment.MachineName + "|" + Environment.UserDomainName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("U19|" + guid))).ToLowerInvariant();
    }

    /// <summary>Vereinheitlichte Schreibweise wie auf dem Server (Großbuchstaben, keine Leerzeichen).</summary>
    public static string NormalizeKey(string key) => new string((key ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    public static string KeyHash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeKey(key)))).ToLowerInvariant();

    /// <summary>Hat der Text das Format eines Lizenzschlüssels (U19-XXXX-XXXX-XXXX-XXXX)?</summary>
    public static bool LooksLikeKey(string key) =>
        System.Text.RegularExpressions.Regex.IsMatch(NormalizeKey(key), @"^U19-[A-HJ-NP-Z2-9]{4}(-[A-HJ-NP-Z2-9]{4}){3}$");

    /// <summary>Schlüssel gekürzt anzeigen: U19-ABCD-****-****-WXYZ.</summary>
    public static string MaskKey(string key)
    {
        var parts = NormalizeKey(key).Split('-');
        return parts.Length == 5 ? $"{parts[0]}-{parts[1]}-****-****-{parts[4]}" : "****";
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Text, der signiert ist – muss zu license_lease_payload() im Server passen.</summary>
    public static string Payload(LicenseLease l) => $"U19L1|{l.KeyHash}|{l.MachineId}|{l.IssuedAt}|{l.ValidUntil}";

    public static bool VerifySignature(LicenseLease lease, string publicKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(Encoding.UTF8.GetBytes(Payload(lease)), Convert.FromBase64String(lease.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Prüft den hinterlegten Schlüssel beim Server. Ist der Server nicht erreichbar, gilt die letzte signierte Freigabe.
    /// </summary>
    public static async Task<LicenseCheck> CheckAsync(AppSettings s, ApiClient api, CancellationToken ct = default)
    {
        var key = NormalizeKey(s.LicenseKey);
        if (key.Length == 0)
        {
            return new LicenseCheck(LicenseStatus.NoKey, "Es ist noch kein Lizenzschlüssel hinterlegt.");
        }

        LicenseResponse response;
        try
        {
            response = await api.ValidateLicenseAsync(key, MachineId, MachineName, UpdateService.CurrentVersion.ToString(), ct);
        }
        catch (ApiException ex) when (ex.Status is null || (int)ex.Status.Value >= 500 || ex.Status == System.Net.HttpStatusCode.Unauthorized || ex.Status == System.Net.HttpStatusCode.Forbidden)
        {
            // keine Verbindung / Serverfehler / API-Schlüssel gerade nicht nutzbar -> Offline-Freigabe
            return Offline(s, key, ex.Message);
        }
        catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
        {
            return new LicenseCheck(LicenseStatus.Denied, "Der Server unterstützt die Lizenzprüfung noch nicht. Bitte die Web-Anwendung aktualisieren.");
        }

        if (!response.Valid)
        {
            // Der Server hat ausdrücklich abgelehnt: Freigabe verwerfen
            s.LicenseLease = null;
            s.Save();
            return new LicenseCheck(LicenseStatus.Denied, string.IsNullOrWhiteSpace(response.Message) ? "Die Lizenz ist ungültig." : response.Message);
        }

        // Signatur sofort prüfen, sonst wäre die spätere Offline-Nutzung wertlos
        if (response.Lease is null || string.IsNullOrEmpty(response.PublicKey) || !VerifySignature(response.Lease, response.PublicKey)
            || response.Lease.KeyHash != KeyHash(key) || response.Lease.MachineId != MachineId)
        {
            return new LicenseCheck(LicenseStatus.Denied, "Die Antwort des Servers konnte nicht bestätigt werden (ungültige Signatur).");
        }

        s.LicensePublicKey = response.PublicKey;
        s.LicenseLease = response.Lease;
        s.LicenseOfflineSince = 0; // Verbindung steht wieder: Offline-Lizenz beginnt bei Bedarf neu
        s.LicenseName = response.Name;
        s.LicenseLastSeen = Math.Max(Math.Max(s.LicenseLastSeen, Now()), response.ServerTime);
        s.Save();
        return new LicenseCheck(LicenseStatus.Valid, string.IsNullOrEmpty(response.Name) ? "Lizenz gültig." : $"Lizenz gültig ({response.Name}).",
            DateTimeOffset.FromUnixTimeSeconds(response.Lease.ValidUntil));
    }

    /// <summary>Gültigkeit der gespeicherten Offline-Freigabe (ohne Serverkontakt).</summary>
    private static LicenseCheck Offline(AppSettings s, string key, string reason)
    {
        var expired = new Func<string, LicenseCheck>(why => new LicenseCheck(LicenseStatus.OfflineExpired,
            $"Keine Verbindung zum Lizenzserver ({reason}).\n{why}"));

        var lease = s.LicenseLease;
        if (lease is null)
        {
            // Noch nie vom Server bestätigt: die fest eingebaute Offline-Lizenz gilt automatisch, höchstens 3 Tage
            // ab dem ersten Verbindungsabbruch (wird nur durch eine erfolgreiche Serverprüfung zurückgesetzt).
            var current = Now();
            if (s.LicenseOfflineSince <= 0 || s.LicenseOfflineSince > current + ClockToleranceSeconds)
            {
                s.LicenseOfflineSince = current;
            }
            if (current < s.LicenseLastSeen - ClockToleranceSeconds)
            {
                return expired("Die Systemuhr wurde zurückgestellt. Bitte die Uhrzeit korrigieren und mit dem Server verbinden.");
            }
            var offlineUntil = s.LicenseOfflineSince + OfflineLicenseSeconds;
            if (current > offlineUntil)
            {
                s.Save();
                return expired("Die Offline-Lizenz (höchstens 3 Tage) ist abgelaufen. Bitte mit dem Server verbinden.");
            }
            s.LicenseLastSeen = Math.Max(s.LicenseLastSeen, current);
            s.Save();
            var untilOffline = DateTimeOffset.FromUnixTimeSeconds(offlineUntil);
            return new LicenseCheck(LicenseStatus.OfflineGrace, $"Offline-Lizenz bis {untilOffline.ToLocalTime():dd.MM.yyyy HH:mm}.", untilOffline);
        }
        if (string.IsNullOrEmpty(s.LicensePublicKey))
        {
            return expired("Die gespeicherte Offline-Freigabe ist unvollständig. Bitte mit dem Server verbinden.");
        }
        if (!VerifySignature(lease, s.LicensePublicKey) || lease.KeyHash != KeyHash(key) || lease.MachineId != MachineId)
        {
            return expired("Die gespeicherte Offline-Freigabe ist ungültig. Bitte mit dem Server verbinden.");
        }

        var now = Now();
        if (now < s.LicenseLastSeen - ClockToleranceSeconds || now < lease.IssuedAt - ClockToleranceSeconds)
        {
            return expired("Die Systemuhr wurde zurückgestellt. Bitte die Uhrzeit korrigieren und mit dem Server verbinden.");
        }
        if (now > lease.ValidUntil)
        {
            return expired("Die Offline-Freigabe (höchstens 3 Tage) ist abgelaufen. Bitte mit dem Server verbinden.");
        }

        s.LicenseLastSeen = Math.Max(s.LicenseLastSeen, now);
        s.Save();
        var until = DateTimeOffset.FromUnixTimeSeconds(lease.ValidUntil);
        return new LicenseCheck(LicenseStatus.OfflineGrace, $"Offline-Betrieb bis {until.ToLocalTime():dd.MM.yyyy HH:mm}.", until);
    }
}
