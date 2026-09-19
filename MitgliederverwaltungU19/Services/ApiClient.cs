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
    public ApiException(string message, HttpStatusCode? status = null) : base(message) => Status = status;
}

public sealed record PingResult(string TokenName, bool CanWrite);

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
        return new PingResult(root.GetProperty("token").GetString() ?? "", root.GetProperty("write").GetBoolean());
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
    public async Task<(byte[] Data, string Extension)> DownloadDocumentAsync(int memberId, string type, CancellationToken ct = default)
    {
        using var response = await SendAsync(() => _http.GetAsync(Endpoint($"members/{memberId}/documents/{type}"), ct));
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

    public async Task UploadDocumentAsync(int memberId, string type, string filePath, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(filePath, ct)), "file", Path.GetFileName(filePath));
        using var response = await SendAsync(() => _http.PostAsync(Endpoint($"members/{memberId}/documents/{type}"), form, ct));
        EnsureSuccess(response, await response.Content.ReadAsStringAsync(ct));
    }

    public async Task DeleteDocumentAsync(int memberId, string type, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"members/{memberId}/documents/{type}", null, ct);
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
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                message = err.GetString() ?? message;
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
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            message = "API-Schlüssel ungültig oder widerrufen.";
        }
        throw new ApiException(message, response.StatusCode);
    }

    public void Dispose() => _http.Dispose();
}
