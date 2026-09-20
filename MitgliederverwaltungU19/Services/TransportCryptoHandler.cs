using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MitgliederverwaltungU19.Services;

/// <summary>
/// Verschlüsselt die gesamte API-Kommunikation zusätzlich zu HTTPS (AES-256-GCM, Schlüssel = "Verschlüsselungsschlüssel"
/// aus dem Webpanel). Jede Anfrage wird als POST mit einem verschlüsselten Paket gesendet, das Methode, Pfad, Suchbegriffe,
/// Zugangsdaten und Inhalt enthält; die Antwort kommt ebenfalls verschlüsselt zurück und wird hier wieder in eine normale
/// HttpResponseMessage umgewandelt. Das Format ist in api/transport.php beschrieben.
/// </summary>
public sealed class TransportCryptoHandler : DelegatingHandler
{
    private static readonly byte[] AadRequest = Encoding.ASCII.GetBytes("U19-REQ");
    private static readonly byte[] AadResponse = Encoding.ASCII.GetBytes("U19-RES");

    private readonly byte[] _key;
    private readonly Uri _endpoint;

    /// <param name="keyBase64">Verschlüsselungsschlüssel (32 Byte, Base64)</param>
    /// <param name="endpoint">Adresse von api/index.php</param>
    public TransportCryptoHandler(string keyBase64, Uri endpoint, HttpMessageHandler inner) : base(inner)
    {
        _endpoint = endpoint;
        try
        {
            _key = Convert.FromBase64String(keyBase64.Trim());
        }
        catch (FormatException)
        {
            _key = Array.Empty<byte>();
        }
    }

    public static bool IsValidKey(string keyBase64)
    {
        try
        {
            return Convert.FromBase64String((keyBase64 ?? "").Trim()).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_key.Length != 32)
        {
            throw new ApiException("Der Verschlüsselungsschlüssel fehlt oder ist ungültig. Bitte in den Einstellungen eintragen (Webpanel → API-Zugang).");
        }

        // Pfad + Query so wiederherstellen, wie der Server sie erwartet ("members/5?x=1")
        var rawQuery = request.RequestUri!.Query.TrimStart('?');
        string route = "";
        var others = new List<string>();
        foreach (var part in rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("path=", StringComparison.Ordinal)) route = Uri.UnescapeDataString(part[5..]);
            else others.Add(part);
        }
        var uri = others.Count > 0 ? route + "?" + string.Join("&", others) : route;

        var headers = new JsonObject();
        foreach (var name in new[] { "Authorization", "X-User-Token" })
        {
            if (request.Headers.TryGetValues(name, out var values)) headers[name] = string.Join(",", values);
        }

        var post = new JsonObject();
        var files = new JsonArray();
        var payload = new MemoryStream();
        string contentType = "";
        if (request.Content is MultipartFormDataContent multipart)
        {
            foreach (var part in multipart)
            {
                var disposition = part.Headers.ContentDisposition;
                var field = disposition?.Name?.Trim('"') ?? "";
                var data = await part.ReadAsByteArrayAsync(ct);
                if (disposition?.FileName is { } fileName)
                {
                    files.Add(new JsonObject
                    {
                        ["field"] = field,
                        ["name"] = fileName.Trim('"'),
                        ["type"] = part.Headers.ContentType?.MediaType ?? "",
                        ["len"] = data.Length,
                    });
                    payload.Write(data);
                }
                else
                {
                    post[field] = Encoding.UTF8.GetString(data);
                }
            }
        }
        else if (request.Content is not null)
        {
            contentType = request.Content.Headers.ContentType?.ToString() ?? "";
            payload.Write(await request.Content.ReadAsByteArrayAsync(ct));
        }

        var rid = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var meta = new JsonObject
        {
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["rid"] = rid,
            ["method"] = request.Method.Method,
            ["uri"] = uri,
            ["headers"] = headers,
            ["ct"] = contentType,
            ["post"] = post,
            ["files"] = files,
        };
        var metaBytes = Encoding.UTF8.GetBytes(meta.ToJsonString());

        var plain = new byte[4 + metaBytes.Length + (int)payload.Length];
        plain[0] = (byte)(metaBytes.Length >> 24);
        plain[1] = (byte)(metaBytes.Length >> 16);
        plain[2] = (byte)(metaBytes.Length >> 8);
        plain[3] = (byte)metaBytes.Length;
        metaBytes.CopyTo(plain, 4);
        payload.ToArray().CopyTo(plain, 4 + metaBytes.Length);

        using var outer = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new ByteArrayContent(Encrypt(plain, AadRequest)),
        };
        outer.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        outer.Headers.Add("X-Enc", "1");
        outer.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        var response = await base.SendAsync(outer, ct);
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.Headers.Contains("X-Enc"))
        {
            // Nicht verschlüsselte Antwort (z. B. Server-Fehler vor der Entschlüsselung): unverändert weitergeben
            var raw = new HttpResponseMessage(response.StatusCode) { Content = new ByteArrayContent(body), ReasonPhrase = response.ReasonPhrase };
            raw.Content.Headers.TryAddWithoutValidation("Content-Type", response.Content.Headers.ContentType?.ToString() ?? "application/json");
            response.Dispose();
            return raw;
        }

        byte[] decrypted;
        try
        {
            decrypted = Decrypt(body, AadResponse);
        }
        catch (CryptographicException)
        {
            response.Dispose();
            throw new ApiException("Die Antwort des Servers konnte nicht entschlüsselt werden. Stimmt der Verschlüsselungsschlüssel?");
        }

        var metaLen = (decrypted[0] << 24) | (decrypted[1] << 16) | (decrypted[2] << 8) | decrypted[3];
        using var respMeta = JsonDocument.Parse(decrypted.AsMemory(4, metaLen));
        var root = respMeta.RootElement;
        if (root.GetProperty("rid").GetString() != rid)
        {
            response.Dispose();
            throw new ApiException("Ungültige Antwort des Servers (Kennung stimmt nicht überein).");
        }

        var result = new HttpResponseMessage((HttpStatusCode)root.GetProperty("status").GetInt32())
        {
            Content = new ByteArrayContent(decrypted, 4 + metaLen, decrypted.Length - 4 - metaLen),
        };
        if (root.TryGetProperty("ct", out var ct2) && ct2.GetString() is { Length: > 0 } ctText)
        {
            result.Content.Headers.TryAddWithoutValidation("Content-Type", ctText);
        }
        if (root.TryGetProperty("cd", out var cd) && cd.GetString() is { Length: > 0 } cdText)
        {
            result.Content.Headers.TryAddWithoutValidation("Content-Disposition", cdText);
        }
        response.Dispose();
        return result;
    }

    /// <summary>Nonce (12) | Tag (16) | Chiffrat</summary>
    private byte[] Encrypt(byte[] plain, byte[] aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag, aad);
        var result = new byte[28 + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, 12);
        cipher.CopyTo(result, 28);
        return result;
    }

    private byte[] Decrypt(byte[] blob, byte[] aad)
    {
        if (blob.Length < 28) throw new CryptographicException("Paket zu kurz.");
        var plain = new byte[blob.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(28), blob.AsSpan(12, 16), plain, aad);
        return plain;
    }
}
