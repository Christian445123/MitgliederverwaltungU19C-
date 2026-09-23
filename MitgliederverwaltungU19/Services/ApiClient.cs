using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MitgliederverwaltungU19.Models;

namespace MitgliederverwaltungU19.Services;

public sealed class ApiException : Exception
{
    public HttpStatusCode? Status { get; }

    /// <summary>Fehlercode des Servers (z. B. session_expired, invalid_credentials, must_change_password) oder leer.</summary>
    public string Code { get; }

    public ApiException(string message, HttpStatusCode? status = null, string code = "") : base(message)
    {
        Status = status;
        Code = code;
    }
}

/// <summary>Angemeldeter Web-Benutzer mit seinen Rechten (wie im Web-Panel unter Rollen und Rechte).</summary>
public sealed record UserInfo(int Id, string Username, string Role, IReadOnlySet<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission);

    public static UserInfo FromJson(JsonElement e)
    {
        var perms = new HashSet<string>();
        if (e.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in p.EnumerateArray())
            {
                if (x.GetString() is { } s) perms.Add(s);
            }
        }
        return new UserInfo(
            e.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            e.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "",
            e.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "",
            perms);
    }
}

/// <summary>Ergebnis der Anmeldung: Sitzungs-Token (nur jetzt sichtbar), Ablauf und Benutzer.</summary>
public sealed record LoginResult(string Token, string ExpiresAt, UserInfo User);

public sealed record PingResult(string TokenName, bool CanWrite, UserInfo? User = null)
{
    /// <summary>Darf der angemeldete Benutzer das? Ohne Benutzer (nur API-Schlüssel) gilt der Schlüssel.</summary>
    public bool Can(string permission) => User is null || User.Can(permission);
}

public sealed class ImportRow
{
    public int Line { get; set; }
    public string Action { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class ImportColumn
{
    public string Header { get; set; } = "";
    public string? Field { get; set; }
    public int Index { get; set; }
    public string? Key { get; set; }
    public string State { get; set; } = "";
    public int Values { get; set; }
}

public sealed class ImportField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class ImportCounts
{
    public int Create { get; set; }
    public int Update { get; set; }
    public int Skip { get; set; }
    public int Error { get; set; }
    public int Warning { get; set; }
}

public sealed class ImportResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public List<string> Failed { get; set; } = new();
}

public sealed class ImportResponse
{
    public ImportCounts Counts { get; set; } = new();
    public List<string> UnknownColumns { get; set; } = new();
    public List<ImportRow> Rows { get; set; } = new();
    public List<ImportColumn> Columns { get; set; } = new();
    public int HeaderRow { get; set; }
    public List<ImportField> Fields { get; set; } = new();
    public List<string> MissingRequired { get; set; } = new();
    public ImportResult? Result { get; set; }
}

/// <summary>Zugriff auf die REST-API der Web-Anwendung (Ordner /api).</summary>
public sealed class ApiClient : IDisposable
{
    private static readonly JsonSerializerOptions ImportJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;

    public ApiClient(AppSettings settings)
    {
        var baseUri = new Uri(NormalizeBaseUrl(settings.BaseUrl) + "/");
        var inner = new HttpClientHandler { SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13 };
        _http = new HttpClient(new TransportCryptoHandler(settings.Token, new Uri(baseUri, "index.php"), inner))
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(60),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Akzeptiert Adresse mit oder ohne "/api" am Ende.</summary>
    public static string NormalizeBaseUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[7..]; // unverschlüsselte Verbindungen gibt es nicht
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }
        return url.EndsWith("/api", StringComparison.OrdinalIgnoreCase) ? url : url + "/api";
    }

    public async Task<PingResult> PingAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "ping", null, ct);
        var root = doc.RootElement;
        UserInfo? user = root.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? UserInfo.FromJson(u) : null;
        return new PingResult(root.GetProperty("token").GetString() ?? "", root.GetProperty("write").GetBoolean(), user);
    }

    /// <summary>Sitzungs-Token des angemeldeten Benutzers; wird bei allen Anfragen als X-User-Token mitgeschickt.</summary>
    public string SessionToken
    {
        get => _sessionToken;
        set
        {
            _sessionToken = value ?? "";
            _http.DefaultRequestHeaders.Remove("X-User-Token");
            if (_sessionToken.Length > 0) _http.DefaultRequestHeaders.Add("X-User-Token", _sessionToken);
        }
    }
    private string _sessionToken = "";

    /// <summary>Meldet mit den Benutzerdaten des Web-Panels an (Benutzername und Passwort).</summary>
    public async Task<LoginResult> LoginAsync(string username, string password, bool remember, string machineName, CancellationToken ct = default)
    {
        var body = new JsonObject { ["username"] = username, ["password"] = password, ["remember"] = remember, ["machine_name"] = machineName };
        using var doc = await SendJsonAsync(HttpMethod.Post, "auth/login", body, ct);
        var root = doc.RootElement;
        return new LoginResult(
            root.GetProperty("token").GetString() ?? "",
            root.TryGetProperty("expires_at", out var ex) ? ex.GetString() ?? "" : "",
            UserInfo.FromJson(root.GetProperty("user")));
    }

    /// <summary>Legt das erste eigene Passwort fest (für im Web-Panel neu angelegte Benutzer, die es ändern müssen).</summary>
    public async Task FirstPasswordAsync(string username, string password, string newPassword, CancellationToken ct = default)
    {
        var body = new JsonObject { ["username"] = username, ["password"] = password, ["new_password"] = newPassword };
        using var _ = await SendJsonAsync(HttpMethod.Post, "auth/first-password", body, ct);
    }


    /// <summary>Beendet die Sitzung auf dem Server (Fehler werden ignoriert).</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            using var _ = await SendJsonAsync(HttpMethod.Post, "auth/logout", null, ct);
        }
        catch (ApiException)
        {
        }
        SessionToken = "";
    }

    /// <summary>Lädt alle Mitglieder (seitenweise).</summary>
    public async Task<List<Member>> ListAllAsync(CancellationToken ct = default)
    {
        var result = new List<Member>();
        var offset = 0;
        while (true)
        {
            using var doc = await SendJsonAsync(HttpMethod.Get, $"members?limit=500&offset={offset}", null, ct);
            if (!doc.RootElement.TryGetProperty("data", out var data) || !doc.RootElement.TryGetProperty("total", out var totalEl))
            {
                throw new ApiException("Die API auf dem Server ist veraltet (kennt \"?path=\" nicht). Bitte die aktuelle Version der Web-Anwendung auf dem Server einspielen (git pull).");
            }
            var total = totalEl.GetInt32();
            foreach (var e in data.EnumerateArray())
            {
                result.Add(Member.FromJson(e));
            }
            offset += data.GetArrayLength();
            if (data.GetArrayLength() == 0 || offset >= total) break;
        }
        return result;
    }

    public async Task<Member> SaveAsync(int? id, JsonObject payload, CancellationToken ct = default)
    {
        using var doc = id is null
            ? await SendJsonAsync(HttpMethod.Post, "members", payload, ct)
            : await SendJsonAsync(HttpMethod.Put, $"members/{id}", payload, ct);
        return Member.FromJson(doc.RootElement);
    }

    /// <summary>Löscht mehrere Mitglieder. Liefert die Anzahl gelöschter Mitglieder.</summary>
    public async Task<int> DeleteManyAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var body = new JsonObject { ["ids"] = new JsonArray(ids.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()) };
        using var doc = await SendJsonAsync(HttpMethod.Post, "members/bulk-delete", body, ct);
        return doc.RootElement.GetProperty("deleted").GetInt32();
    }

    /// <summary>Löscht ALLE Mitglieder (nach ausdrücklicher Bestätigung). Liefert die Anzahl.</summary>
    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        var body = new JsonObject { ["all"] = true, ["confirm"] = "ALLE LÖSCHEN" };
        using var doc = await SendJsonAsync(HttpMethod.Post, "members/bulk-delete", body, ct);
        return doc.RootElement.GetProperty("deleted").GetInt32();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"members/{id}", null, ct);
    }

    // ── Neue Mitglieder: ausstehende Anmeldungen, Registrierungslinks, Benachrichtigungs-Adresse ──

    /// <summary>Neue, noch nicht zugewiesene Anmeldungen (Bereich „Neue Mitglieder“).</summary>
    public async Task<List<Member>> ListRegistrationsAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "registrations", null, ct);
        if (!doc.RootElement.TryGetProperty("data", out var data))
        {
            throw new ApiException("Die API auf dem Server kennt \"registrations\" noch nicht. Bitte die aktuelle Version einspielen (Änderungen einspielen / git pull).");
        }
        return data.EnumerateArray().Select(Member.FromJson).ToList();
    }

    /// <summary>Übernimmt eine Anmeldung: weist Kader ("kader") oder "nicht_im_kader" zu.</summary>
    public async Task<Member> ApproveRegistrationAsync(int id, string kader, CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post, $"registrations/{id}/approve", new JsonObject { ["target"] = kader }, ct);
        return Member.FromJson(doc.RootElement);
    }

    /// <summary>Übernimmt eine Anmeldung NICHT als Spieler, sondern als Staff. Liefert die ID der neu angelegten Staff-Person.</summary>
    public async Task<int> ApproveRegistrationAsStaffAsync(int id, CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post, $"registrations/{id}/approve", new JsonObject { ["target"] = "staff" }, ct);
        return doc.RootElement.GetProperty("staff_id").GetInt32();
    }

    /// <summary>Lehnt eine Anmeldung ab (löscht den Datensatz).</summary>
    public async Task RejectRegistrationAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Post, $"registrations/{id}/reject", null, ct);
    }

    /// <summary>Neue, noch nicht freigegebene Staff-Anmeldungen (eigener Staff-Einladungslink).</summary>
    public async Task<List<PendingStaff>> ListPendingStaffAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "registrations/staff", null, ct);
        if (!doc.RootElement.TryGetProperty("data", out var data))
        {
            throw new ApiException("Die API auf dem Server kennt \"registrations/staff\" noch nicht. Bitte die aktuelle Version einspielen (Änderungen einspielen / git pull).");
        }
        return data.EnumerateArray().Select(e => new PendingStaff(
            e.GetProperty("id").GetInt32(),
            Str(e, "name_vorname"),
            e.TryGetProperty("position", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
            e.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.String ? em.GetString() : null,
            e.TryGetProperty("telefon", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
            e.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String ? ca.GetString() : null
        )).ToList();
    }

    /// <summary>Übernimmt eine Staff-Anmeldung (setzt den Status auf aktiv).</summary>
    public async Task ApproveStaffRegistrationAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Post, $"registrations/staff/{id}/approve", null, ct);
    }

    /// <summary>Lehnt eine Staff-Anmeldung ab (löscht den Datensatz).</summary>
    public async Task RejectStaffRegistrationAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Post, $"registrations/staff/{id}/reject", null, ct);
    }

    private static RegistrationLink ParseRegistrationLink(JsonElement e) => new(
        e.GetProperty("id").GetInt32(),
        Str(e, "token"),
        Str(e, "url"),
        e.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null,
        e.TryGetProperty("link_type", out var lt) && lt.ValueKind == JsonValueKind.String && lt.GetString() == "staff" ? "staff" : "player",
        e.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True,
        e.TryGetProperty("created_by", out var cb) && cb.ValueKind == JsonValueKind.String ? cb.GetString() : null,
        Str(e, "created_at"),
        e.TryGetProperty("expires_at", out var ex) && ex.ValueKind == JsonValueKind.String ? ex.GetString() : null,
        e.TryGetProperty("use_count", out var uc) && uc.ValueKind == JsonValueKind.Number ? uc.GetInt32() : 0,
        e.TryGetProperty("last_used_at", out var lu) && lu.ValueKind == JsonValueKind.String ? lu.GetString() : null);

    public async Task<List<RegistrationLink>> ListRegistrationLinksAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "registration-links", null, ct);
        return doc.RootElement.GetProperty("data").EnumerateArray().Select(ParseRegistrationLink).ToList();
    }

    /// <summary>Erzeugt einen neuen Registrierungslink. expiresAt: "yyyy-MM-dd" oder null (unbegrenzt gültig).
    /// linkType: "player" (Standard) oder "staff".</summary>
    public async Task<RegistrationLink> CreateRegistrationLinkAsync(string label, string? expiresAt, string linkType = "player", CancellationToken ct = default)
    {
        var body = new JsonObject { ["label"] = label, ["link_type"] = linkType == "staff" ? "staff" : "player" };
        if (!string.IsNullOrWhiteSpace(expiresAt)) body["expires_at"] = expiresAt;
        using var doc = await SendJsonAsync(HttpMethod.Post, "registration-links", body, ct);
        return ParseRegistrationLink(doc.RootElement);
    }

    public async Task<RegistrationLink> SetRegistrationLinkActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Put, $"registration-links/{id}", new JsonObject { ["active"] = active }, ct);
        return ParseRegistrationLink(doc.RootElement);
    }

    public async Task DeleteRegistrationLinkAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"registration-links/{id}", null, ct);
    }

    /// <summary>Adresse, an die bei neuen bzw. doppelten Anmeldungen eine Benachrichtigung geht (leer = keine).</summary>
    public async Task<string> GetRegistrationNotifyEmailAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "registration-settings", null, ct);
        return Str(doc.RootElement, "notify_email");
    }

    public async Task SetRegistrationNotifyEmailAsync(string email, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Put, "registration-settings", new JsonObject { ["notify_email"] = email }, ct);
    }

    /// <summary>Lädt alle Staff-Mitglieder (Feldname → Textwert).</summary>
    public async Task<List<Dictionary<string, string?>>> ListStaffAsync(CancellationToken ct = default)
    {
        var result = new List<Dictionary<string, string?>>();
        var offset = 0;
        while (true)
        {
            using var doc = await SendJsonAsync(HttpMethod.Get, $"staff?limit=500&offset={offset}", null, ct);
            if (!doc.RootElement.TryGetProperty("data", out var data) || !doc.RootElement.TryGetProperty("total", out var totalEl))
            {
                throw new ApiException("Die API auf dem Server kennt \"staff\" noch nicht. Bitte die aktuelle Version einspielen (Änderungen einspielen / git pull).");
            }
            foreach (var e in data.EnumerateArray())
            {
                var row = new Dictionary<string, string?>();
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "dokumente" && p.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var d in p.Value.EnumerateObject()) row["dokument_" + d.Name] = d.Value.ValueKind == JsonValueKind.True ? "true" : "false";
                        continue;
                    }
                    row[p.Name] = p.Value.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.Object or JsonValueKind.Array => p.Value.GetRawText(),
                        JsonValueKind.Number => p.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => p.Value.GetString(),
                    };
                }
                result.Add(row);
            }
            offset += data.GetArrayLength();
            if (data.GetArrayLength() == 0 || offset >= totalEl.GetInt32()) break;
        }
        return result;
    }

    /// <summary>Speichert eine Person und liefert ihre ID.</summary>
    public async Task<int> SaveStaffAsync(int? id, JsonObject payload, CancellationToken ct = default)
    {
        using var doc = id is null
            ? await SendJsonAsync(HttpMethod.Post, "staff", payload, ct)
            : await SendJsonAsync(HttpMethod.Put, $"staff/{id}", payload, ct);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    public async Task DeleteStaffAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"staff/{id}", null, ct);
    }

    /// <summary>Lädt einen Roster (PDF/Excel) vom Server. kind: "", "-ifaf", "-bekleidung", "-vereine", "-fehlend", "-abgelaufen" oder "-staff".</summary>
    public Task<byte[]> DownloadRosterAsync(string kind, string format, IDictionary<string, string> query, CancellationToken ct = default)
    {
        var qs = string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        var path = "roster" + kind + "." + format + (qs.Length > 0 ? "?" + qs : "");
        return DownloadAsync(path, ct);
    }

    public Task<byte[]> DownloadCsvAsync(string? status, bool template, CancellationToken ct = default, bool staff = false)
    {
        var query = status is null ? "" : "?status=" + status;
        var path = template ? "template.csv" + (staff ? "?entity=staff" : "") : (staff ? "staff.csv" : "members.csv") + query;
        return DownloadAsync(path, ct);
    }

    public async Task<ImportResponse> ImportAsync(string filePath, bool updateExisting, bool commit, string? kaderDefault = null, Dictionary<int, string>? mapping = null, CancellationToken ct = default, string entity = "members")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(filePath, ct));
        form.Add(file, "file", Path.GetFileName(filePath));
        form.Add(new StringContent(updateExisting ? "1" : "0"), "update_existing");
        form.Add(new StringContent(commit ? "1" : "0"), "commit");
        form.Add(new StringContent(kaderDefault ?? ""), "kader_default");
        form.Add(new StringContent(entity), "entity");
        if (mapping is { Count: > 0 })
        {
            form.Add(new StringContent(JsonSerializer.Serialize(mapping.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value))), "mapping");
        }

        using var response = await SendAsync(() => _http.PostAsync(Endpoint("import"), form, ct));
        var body = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, body);
        return JsonSerializer.Deserialize<ImportResponse>(body, ImportJson) ?? throw new ApiException("Leere Antwort vom Server.");
    }

    /// <summary>Fragt den Server, ob der Lizenzschlüssel für dieses Gerät gilt (POST license/validate).</summary>
    public async Task<LicenseResponse> ValidateLicenseAsync(string key, string machineId, string machineName, string appVersion, CancellationToken ct = default)
    {
        var body = new JsonObject { ["key"] = key, ["machine_id"] = machineId, ["machine_name"] = machineName, ["app_version"] = appVersion };
        using var doc = await SendJsonAsync(HttpMethod.Post, "license/validate", body, ct);
        return LicenseResponse.FromJson(doc.RootElement);
    }

    /// <summary>Löst auf dem Server ein git pull aus. Liefert Erfolg und das Protokoll.</summary>
    public async Task<(bool Success, string Log)> UpdateServerAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post, "update", null, ct);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        var log = root.TryGetProperty("log", out var l) ? l.GetString() ?? "" : "";
        return (ok, log);
    }

    /// <summary>Lädt ein Dokument herunter und liefert Inhalt und Dateiendung.</summary>
    public async Task<(byte[] Data, string Extension)> DownloadDocumentAsync(int memberId, string type, CancellationToken ct = default, string kind = "members")
    {
        using var response = await SendAsync(() => _http.GetAsync(Endpoint($"{kind}/{memberId}/documents/{type}"), ct));
        if (!response.IsSuccessStatusCode)
        {
            EnsureSuccess(response, await response.Content.ReadAsStringAsync(ct));
        }
        var ext = response.Content.Headers.ContentType?.MediaType switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            _ => ".bin",
        };
        return (await response.Content.ReadAsByteArrayAsync(ct), ext);
    }

    // ── Benutzer, Rollen und Rechte (Recht "users.manage", nur mit Benutzeranmeldung) ──

    public async Task<List<PermissionDef>> GetPermissionsAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "admin/permissions", null, ct);
        return doc.RootElement.GetProperty("permissions").EnumerateArray()
            .Select(p => new PermissionDef(p.GetProperty("key").GetString() ?? "", p.GetProperty("label").GetString() ?? "", p.GetProperty("group").GetString() ?? ""))
            .ToList();
    }

    private static List<RoleInfo> ParseRoles(JsonElement roles) => roles.EnumerateArray().Select(r => new RoleInfo(
        r.GetProperty("id").GetInt32(),
        r.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : "",
        r.GetProperty("name").GetString() ?? "",
        r.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
        r.GetProperty("is_system").GetBoolean(),
        r.GetProperty("users").GetInt32(),
        r.GetProperty("permissions").EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet())).ToList();

    public async Task<List<RoleInfo>> GetRolesAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "admin/roles", null, ct);
        return ParseRoles(doc.RootElement.GetProperty("roles"));
    }

    public async Task SaveRoleAsync(int? id, string name, string description, IEnumerable<string> permissions, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["permissions"] = new JsonArray(permissions.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
        };
        using var _ = id is null
            ? await SendJsonAsync(HttpMethod.Post, "admin/roles", body, ct)
            : await SendJsonAsync(HttpMethod.Put, $"admin/roles/{id}", body, ct);
    }

    public async Task DeleteRoleAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"admin/roles/{id}", null, ct);
    }

    public async Task<(List<UserRow> Users, int YouId, bool YouAreAdmin)> GetUsersAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "admin/users", null, ct);
        var root = doc.RootElement;
        var users = root.GetProperty("users").EnumerateArray().Select(u => new UserRow(
            u.GetProperty("id").GetInt32(),
            u.GetProperty("username").GetString() ?? "",
            u.GetProperty("is_admin").GetBoolean(),
            u.TryGetProperty("role_id", out var rid) && rid.ValueKind == JsonValueKind.Number ? rid.GetInt32() : null,
            u.GetProperty("role_name").GetString() ?? "",
            u.GetProperty("overrides").GetInt32(),
            u.GetProperty("must_change_password").GetBoolean(),
            u.GetProperty("created_at").GetString() ?? "")).ToList();
        return (users, root.GetProperty("you").GetInt32(), root.GetProperty("you_are_admin").GetBoolean());
    }

    public async Task<UserDetail> GetUserAsync(int id, CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"admin/users/{id}", null, ct);
        var r = doc.RootElement;
        var overrides = new Dictionary<string, string>();
        if (r.TryGetProperty("overrides", out var o) && o.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in o.EnumerateObject()) overrides[p.Name] = p.Value.GetString() ?? "";
        }
        return new UserDetail(
            r.GetProperty("id").GetInt32(),
            r.GetProperty("username").GetString() ?? "",
            r.GetProperty("is_admin").GetBoolean(),
            r.TryGetProperty("role_id", out var rid) && rid.ValueKind == JsonValueKind.Number ? rid.GetInt32() : null,
            overrides);
    }

    public async Task SaveUserAsync(int? id, string username, string password, int roleId, IReadOnlyDictionary<string, string> overrides, CancellationToken ct = default)
    {
        var ov = new JsonObject();
        foreach (var (permission, choice) in overrides) ov[permission] = choice;
        var body = new JsonObject { ["username"] = username, ["password"] = password, ["role_id"] = roleId, ["overrides"] = ov };
        using var _ = id is null
            ? await SendJsonAsync(HttpMethod.Post, "admin/users", body, ct)
            : await SendJsonAsync(HttpMethod.Put, $"admin/users/{id}", body, ct);
    }

    public async Task DeleteUserAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"admin/users/{id}", null, ct);
    }

    // ── Verwaltung: Feld-Rechte, Camps, Protokoll, Passwort, Zugangslinks ──

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Null ? "" : v.ToString()) : "";

    public async Task<List<FieldPerm>> GetFieldPermissionsAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "manage/field-permissions", null, ct);
        return doc.RootElement.GetProperty("fields").EnumerateArray().Select(f => new FieldPerm
        {
            Key = Str(f, "key"),
            Label = Str(f, "label"),
            Group = Str(f, "group"),
            AdminOnly = f.GetProperty("admin_only").GetBoolean(),
            Core = f.GetProperty("core").GetBoolean(),
            Player = Str(f, "player"),
            Editor = f.GetProperty("editor").GetBoolean(),
        }).ToList();
    }

    public async Task SaveFieldPermissionsAsync(IEnumerable<FieldPerm> fields, CancellationToken ct = default)
    {
        var map = new JsonObject();
        foreach (var f in fields)
        {
            map[f.Key] = new JsonObject { ["player"] = f.Player, ["editor"] = f.Editor };
        }
        using var _ = await SendJsonAsync(HttpMethod.Put, "manage/field-permissions", new JsonObject { ["fields"] = map }, ct);
    }

    public async Task ResetFieldPermissionsAsync(CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Post, "manage/field-permissions/reset", new JsonObject(), ct);
    }

    private static List<CampInfo> ParseCamps(JsonElement camps) => camps.EnumerateArray()
        .Select(c => new CampInfo(c.GetProperty("id").GetInt32(), Str(c, "name"), c.TryGetProperty("members", out var m) ? m.GetInt32() : 0)).ToList();

    /// <summary>Camps zum Ankreuzen im Mitgliedsformular (lesen).</summary>
    public async Task<List<CampInfo>> GetCampListAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "camps", null, ct);
        return ParseCamps(doc.RootElement.GetProperty("camps"));
    }

    /// <summary>Auswahlliste aller Camps (fest + weitere) für Filter/Massenzuweisung: Wert (z.B. "camp_1"/"c12") + Beschriftung.</summary>
    public async Task<List<(string Value, string Label)>> GetCampOptionsAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "camps", null, ct);
        var list = new List<(string, string)>();
        if (doc.RootElement.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in options.EnumerateObject()) list.Add((p.Name, p.Value.GetString() ?? p.Name));
        }
        return list;
    }

    /// <summary>Weist ein Camp mehreren Mitgliedern oder Staff-Personen auf einmal zu bzw. entfernt es wieder.</summary>
    public async Task<int> AssignCampAsync(string entity, IEnumerable<int> ids, string camp, bool add, CancellationToken ct = default)
    {
        var arr = new JsonArray();
        foreach (var id in ids) arr.Add(id);
        var body = new JsonObject { ["ids"] = arr, ["camp"] = camp, ["action"] = add ? "add" : "remove" };
        using var doc = await SendJsonAsync(HttpMethod.Post, $"{entity}/camp-assign", body, ct);
        return doc.RootElement.TryGetProperty("changed", out var n) && n.TryGetInt32(out var count) ? count : 0;
    }

    public async Task<(List<string> Fixed, List<CampInfo> Camps)> GetCampsAdminAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "manage/camps", null, ct);
        var fixedNames = doc.RootElement.GetProperty("fixed").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        return (fixedNames, ParseCamps(doc.RootElement.GetProperty("camps")));
    }

    public async Task CreateCampAsync(string name, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Post, "manage/camps", new JsonObject { ["name"] = name }, ct);
    }

    public async Task RenameCampAsync(int id, string name, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Put, $"manage/camps/{id}", new JsonObject { ["name"] = name }, ct);
    }

    public async Task DeleteCampAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"manage/camps/{id}", null, ct);
    }

    private static string? StrOrNull(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<ApiTokenInfo> ParseApiTokens(JsonElement tokens) => tokens.EnumerateArray()
        .Select(t => new ApiTokenInfo(
            t.GetProperty("id").GetInt32(), Str(t, "name"),
            t.TryGetProperty("can_write", out var w) && (w.ValueKind == JsonValueKind.True || (w.ValueKind == JsonValueKind.Number && w.GetInt32() == 1)),
            Str(t, "created_at"), StrOrNull(t, "created_by_name"), StrOrNull(t, "last_used_at"))).ToList();

    /// <summary>API-Zugänge für PC-Anwendungen (Excel, PowerShell, eigene Tools) auflisten.</summary>
    public async Task<List<ApiTokenInfo>> GetApiTokensAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "manage/api-tokens", null, ct);
        return ParseApiTokens(doc.RootElement.GetProperty("tokens"));
    }

    /// <summary>Legt einen neuen API-Zugang an. Liefert den Klartext-Schlüssel (nur jetzt verfügbar, wird nicht gespeichert).</summary>
    public async Task<string> CreateApiTokenAsync(string name, bool canWrite, CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post, "manage/api-tokens", new JsonObject { ["name"] = name, ["can_write"] = canWrite }, ct);
        return Str(doc.RootElement, "token");
    }

    public async Task DeleteApiTokenAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"manage/api-tokens/{id}", null, ct);
    }

    private static RegistrationFieldSet ParseRegistrationFieldSet(JsonElement e)
    {
        var registry = new List<(string, string)>();
        if (e.TryGetProperty("registry", out var reg) && reg.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in reg.EnumerateObject()) registry.Add((p.Name, p.Value.GetString() ?? p.Name));
        }
        var required = new HashSet<string>();
        if (e.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in req.EnumerateArray()) if (r.GetString() is { } s) required.Add(s);
        }
        return new RegistrationFieldSet(registry, required);
    }

    /// <summary>Pflichtfelder bei der Selbstanmeldung (Spieler und Staff) lesen.</summary>
    public async Task<(RegistrationFieldSet Player, RegistrationFieldSet Staff)> GetRegistrationFieldsAsync(CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "manage/registration-fields", null, ct);
        return (ParseRegistrationFieldSet(doc.RootElement.GetProperty("player")), ParseRegistrationFieldSet(doc.RootElement.GetProperty("staff")));
    }

    /// <summary>Speichert die Pflichtfelder für "player" oder "staff".</summary>
    public async Task SaveRegistrationFieldsAsync(string type, IEnumerable<string> requiredKeys, CancellationToken ct = default)
    {
        var arr = new JsonArray();
        foreach (var k in requiredKeys) arr.Add(k);
        using var _ = await SendJsonAsync(HttpMethod.Put, "manage/registration-fields", new JsonObject { ["type"] = type, ["required"] = arr }, ct);
    }

    private static string LogQuery(LogFilter f, int? page)
    {
        var parts = new List<string>();
        void Add(string key, string value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add(key + "=" + Uri.EscapeDataString(value.Trim())); }
        Add("source", f.Source); Add("level", f.Level); Add("action", f.Action); Add("actor", f.Actor); Add("q", f.Query); Add("from", f.From); Add("to", f.To);
        if (page is { } p) parts.Add("page=" + p);
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    public async Task<LogPage> GetLogsAsync(LogFilter filter, int page, CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, "manage/logs" + LogQuery(filter, page), null, ct);
        var r = doc.RootElement;
        var entries = r.GetProperty("entries").EnumerateArray().Select(e => new LogEntry(
            e.TryGetProperty("id", out var id) ? long.Parse(id.ToString()) : 0,
            Str(e, "created_at"), Str(e, "source"), Str(e, "level"), Str(e, "action"), Str(e, "actor"), Str(e, "message"),
            Str(e, "ip"), Str(e, "http_method"), Str(e, "path"), Str(e, "status_code"), Str(e, "details"))).ToList();
        var stats = r.GetProperty("stats");
        int S(string n) => stats.TryGetProperty(n, out var v) ? v.GetInt32() : 0;
        return new LogPage(r.GetProperty("total").GetInt32(), r.GetProperty("page").GetInt32(), r.GetProperty("pages").GetInt32(), entries,
            S("requests"), S("errors"), S("warnings"), S("failed_logins"),
            r.GetProperty("actions").EnumerateArray().Select(a => a.GetString() ?? "").ToList());
    }

    public Task<byte[]> DownloadLogsCsvAsync(LogFilter filter, CancellationToken ct = default) =>
        DownloadAsync("manage/logs.csv" + LogQuery(filter, null), ct);

    public async Task<int> PurgeLogsAsync(int days, CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post, "manage/logs/purge", new JsonObject { ["days"] = days }, ct);
        return doc.RootElement.GetProperty("deleted").GetInt32();
    }

    public async Task ChangePasswordAsync(string current, string newPassword, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Post, "auth/password", new JsonObject { ["current_password"] = current, ["new_password"] = newPassword }, ct);
    }

    /// <summary>Persönlicher Link einer Person. entity: "members" oder "staff"; action: "", "regenerate_link", "regenerate_password", "send_email" oder "reset_verification".</summary>
    public async Task<LinkInfo> GetLinkAsync(string entity, int id, string action = "", CancellationToken ct = default)
    {
        using var doc = action.Length == 0
            ? await SendJsonAsync(HttpMethod.Get, $"{entity}/{id}/link", null, ct)
            : await SendJsonAsync(HttpMethod.Post, $"{entity}/{id}/link", new JsonObject { ["action"] = action }, ct);
        var r = doc.RootElement;
        string? Nullable(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return new LinkInfo(Str(r, "name"), Str(r, "email"), Str(r, "link"), Nullable("verified_at"), Nullable("password"), Str(r, "message"));
    }

    public Task<LinkInfo> GetMemberLinkAsync(int memberId, string action = "", CancellationToken ct = default) => GetLinkAsync("members", memberId, action, ct);

    /// <summary>Auskunft (Art. 15/20 DSGVO): alle gespeicherten Daten einer Person als formatiertes JSON (Recht „Datenschutz verwalten“).</summary>
    public async Task<string> GetDsgvoExportAsync(string entity, int id, CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"{entity}/{id}/dsgvo", null, ct);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Setzt die Bestätigung der Personen zurück (entity: "members" oder "staff"). Liefert die Anzahl.</summary>
    public async Task<int> ResetVerificationAsync(string entity, IEnumerable<int> ids, CancellationToken ct = default)
    {
        var arr = new JsonArray();
        foreach (var id in ids) arr.Add(id);
        using var doc = await SendJsonAsync(HttpMethod.Post, $"{entity}/verification/reset", new JsonObject { ["ids"] = arr }, ct);
        return doc.RootElement.TryGetProperty("reset", out var n) && n.TryGetInt32(out var count) ? count : 0;
    }

    /// <summary>Sendet Link und neuen Zugangscode per E-Mail (Massenmail; der Server erlaubt höchstens 10 pro Aufruf).</summary>
    public async Task<List<SendLinkResult>> SendLinksAsync(string entity, IEnumerable<int> ids, string? note = null, CancellationToken ct = default)
    {
        var arr = new JsonArray();
        foreach (var id in ids) arr.Add(id);
        var body = new JsonObject { ["ids"] = arr };
        if (!string.IsNullOrWhiteSpace(note)) body["note"] = note;
        using var doc = await SendJsonAsync(HttpMethod.Post, $"{entity}/send-links", body, ct);
        var list = new List<SendLinkResult>();
        if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in results.EnumerateArray())
            {
                list.Add(new SendLinkResult(e.TryGetProperty("id", out var i) && i.TryGetInt32(out var idv) ? idv : 0, Str(e, "name"), Str(e, "status"), Str(e, "message")));
            }
        }
        return list;
    }

    /// <summary>Setzt oder entfernt die "Fehlt"-Markierung eines Pflichtdokuments (nada, pass, ecard, rechte).</summary>
    public async Task SetDocumentFlagAsync(int memberId, string type, bool missing, CancellationToken ct = default)
    {
        var body = new JsonObject { [type] = missing };
        using var _ = await SendJsonAsync(HttpMethod.Post, $"members/{memberId}/document-flags", body, ct);
    }

    public async Task UploadDocumentAsync(int memberId, string type, string filePath, CancellationToken ct = default, string kind = "members")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(filePath, ct)), "file", Path.GetFileName(filePath));
        using var response = await SendAsync(() => _http.PostAsync(Endpoint($"{kind}/{memberId}/documents/{type}"), form, ct));
        EnsureSuccess(response, await response.Content.ReadAsStringAsync(ct));
    }

    public async Task DeleteDocumentAsync(int memberId, string type, CancellationToken ct = default, string kind = "members")
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"{kind}/{memberId}/documents/{type}", null, ct);
    }

    /// <summary>
    /// Baut die Anfrage-Adresse ohne Server-Umleitung: "members/5?x=1" wird zu
    /// "index.php?path=members/5&amp;x=1" (funktioniert auf nginx und Apache).
    /// </summary>
    private static string Endpoint(string path)
    {
        var q = path.IndexOf('?');
        var route = q < 0 ? path : path[..q];
        var query = q < 0 ? "" : "&" + path[(q + 1)..];
        return "index.php?path=" + Uri.EscapeDataString(route).Replace("%2F", "/") + query;
    }

    private async Task<byte[]> DownloadAsync(string path, CancellationToken ct)
    {
        using var response = await SendAsync(() => _http.GetAsync(Endpoint(path), ct));
        if (!response.IsSuccessStatusCode)
        {
            EnsureSuccess(response, await response.Content.ReadAsStringAsync(ct));
        }
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, Endpoint(path));
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        using var response = await SendAsync(() => _http.SendAsync(request, ct));
        var text = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, text);
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new ApiException("Unerwartete Antwort vom Server (kein JSON). Stimmt die API-Adresse?");
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send();
        }
        catch (TaskCanceledException)
        {
            throw new ApiException("Zeitüberschreitung bei der Verbindung zum Server.");
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException("Server nicht erreichbar: " + ex.Message);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return;

        var message = $"Fehler {(int)response.StatusCode} {response.ReasonPhrase}";
        var code = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                message = err.GetString() ?? message;
            }
            if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
            {
                code = c.GetString() ?? "";
            }
            if (doc.RootElement.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                message += "\n• " + string.Join("\n• ", details.EnumerateArray().Select(d => d.GetString()));
            }
        }
        catch (JsonException)
        {
            // kein JSON -> Standardtext
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized && code.Length == 0)
        {
            message = "API-Schlüssel ungültig oder widerrufen.";
        }
        throw new ApiException(message, response.StatusCode, code);
    }

    public void Dispose() => _http.Dispose();
}
