using System.Text.Json;
using System.Text.Json.Nodes;

namespace MitgliederverwaltungU19.Models;

/// <summary>Ein Mitglied als Feldname → Textwert (Datum yyyy-MM-dd, Bool "true"/"false").</summary>
public sealed class Member
{
    public int Id { get; set; }
    public Dictionary<string, string?> Values { get; } = new();
    public string? ConfirmedAt { get; set; }

    /// <summary>Welche Dokumente vorhanden sind (ecard, pass, nada, rechte).</summary>
    public Dictionary<string, bool> Documents { get; } = new();

    public string Get(string key) => Values.TryGetValue(key, out var v) ? v ?? "" : "";

    public string FullName => $"{Get("nachname")}, {Get("vorname")}";

    public static Member FromJson(JsonElement e)
    {
        var m = new Member { Id = e.GetProperty("id").GetInt32() };
        foreach (var f in Fields.All)
        {
            if (!e.TryGetProperty(f.Key, out var p) || p.ValueKind == JsonValueKind.Null)
            {
                m.Values[f.Key] = null;
                continue;
            }
            m.Values[f.Key] = p.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => p.GetRawText(),
                _ => p.GetString(),
            };
        }
        if (e.TryGetProperty("dokumente", out var docs) && docs.ValueKind == JsonValueKind.Object)
        {
            foreach (var d in docs.EnumerateObject())
            {
                m.Documents[d.Name] = d.Value.ValueKind == JsonValueKind.True;
            }
        }
        if (e.TryGetProperty("bestaetigt_am", out var c) && c.ValueKind == JsonValueKind.String)
        {
            m.ConfirmedAt = c.GetString();
        }
        return m;
    }

    /// <summary>JSON für POST/PUT. Leere Werte werden als null gesendet und leeren das Feld.</summary>
    public static JsonObject ToJson(IReadOnlyDictionary<string, string?> values)
    {
        var o = new JsonObject();
        foreach (var f in Fields.All)
        {
            var v = values.TryGetValue(f.Key, out var s) ? s : null;
            if (f.Type == FieldType.Bool)
            {
                o[f.Key] = v == "true";
            }
            else if (f.Type == FieldType.Kader)
            {
                o[f.Key] = v == "nicht_im_kader" ? "nicht_im_kader" : "kader";
            }
            else if (f.Type == FieldType.Status)
            {
                o[f.Key] = string.IsNullOrWhiteSpace(v) ? "aktiv" : v;
            }
            else
            {
                o[f.Key] = string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
        }
        return o;
    }
}
