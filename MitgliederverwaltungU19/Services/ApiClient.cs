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
        _http = new HttpClient
        {
            BaseAddress = new Uri(NormalizeBaseUrl(settings.BaseUrl) + "/"),
            Timeout = TimeSpan.FromSeconds(60),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Akzeptiert Adresse mit oder ohne "/api" am Ende.</summary>
    public static string NormalizeBaseUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
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
                        row["dokument_rechte"] = p.Value.TryGetProperty("rechte", out var r) && r.ValueKind == JsonValueKind.True ? "true" : "false";
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

    /// <summary>Lädt einen Roster (PDF/Excel) vom Server. type: "", "-ifaf" oder "-bekleidung".</summary>
    public Task<byte[]> DownloadRosterAsync(string kind, string format, IDictionary<string, string> query, CancellationToken ct = default)
    {
        var qs = string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        var path = "roster" + kind + "." + format + (qs.Length > 0 ? "?" + qs : "");
        return DownloadAsync(path, ct);
    }

    public Task<byte[]> DownloadCsvAsync(string? status, bool template, CancellationToken ct = default)
    {
        var path = template ? "template.csv" : "members.csv" + (status is null ? "" : "?status=" + status);
        return DownloadAsync(path, ct);
    }

    public async Task<ImportResponse> ImportAsync(string filePath, bool updateExisting, bool commit, string? kaderDefault = null, Dictionary<int, string>? mapping = null, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(filePath, ct));
        form.Add(file, "file", Path.GetFileName(filePath));
        form.Add(new StringContent(updateExisting ? "1" : "0"), "update_existing");
        form.Add(new StringContent(commit ? "1" : "0"), "commit");
        form.Add(new StringContent(kaderDefault ?? ""), "kader_default");
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
