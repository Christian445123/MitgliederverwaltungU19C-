using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MitgliederverwaltungU19.Services;

/// <summary>Eine auf GitHub veröffentlichte, neuere Version.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Notes, string AssetName, string AssetUrl, string DownloadUrl, long Size, string ReleaseUrl);

/// <summary>
/// Programm-Updates über GitHub Releases: sucht das neueste Release, vergleicht die Version
/// (Tag "v2.2.0") mit der laufenden, lädt die .msi herunter und startet die Installation.
/// Funktioniert mit öffentlichen Repositories ohne Anmeldung und mit privaten über einen Zugriffstoken.
/// </summary>
public static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MitgliederverwaltungU19", CurrentVersion.ToString()));
        return http;
    }

    /// <summary>Version der laufenden Anwendung (Major.Minor.Build).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return Normalize(v);
        }
    }

    public static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    /// <summary>"v2.2.0" / "2.2.0-beta" -> 2.2.0, sonst null.</summary>
    public static Version? ParseTag(string tag)
    {
        var m = Regex.Match(tag ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }

    private static HttpRequestMessage Request(string url, string token, string accept)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd(accept);
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }
        return req;
    }

    /// <summary>
    /// Liefert die neuere Version oder null, wenn die laufende aktuell ist.
    /// </summary>
    /// <exception cref="InvalidOperationException">mit verständlicher Meldung bei Fehlern</exception>
    public static async Task<UpdateInfo?> CheckAsync(string repo, string token, CancellationToken ct = default)
    {
        repo = (repo ?? "").Trim();
        if (!Regex.IsMatch(repo, @"^[\w.\-]+/[\w.\-]+$"))
        {
            throw new InvalidOperationException("Das Repository muss im Format „Besitzer/Repository“ angegeben werden.");
        }

        HttpResponseMessage response;
        try
        {
            using var req = Request($"https://api.github.com/repos/{repo}/releases/latest", token, "application/vnd.github+json");
            response = await Http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("GitHub ist nicht erreichbar (" + ex.Message + "). Bitte die Internetverbindung prüfen.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                // 404 heißt entweder "noch kein Release" oder "Repository nicht sichtbar (privat/falscher Name)": unterscheiden
                string notFound = "";
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var repoVisible = false;
                    try
                    {
                        using var repoReq = Request($"https://api.github.com/repos/{repo}", token, "application/vnd.github+json");
                        using var repoResp = await Http.SendAsync(repoReq, ct);
                        repoVisible = repoResp.IsSuccessStatusCode;
                    }
                    catch (HttpRequestException)
                    {
                    }
                    notFound = repoVisible
                        ? $"Für das Repository „{repo}“ wurde noch kein Release veröffentlicht. Das Update-Angebot erscheint, sobald auf GitHub unter „Releases“ ein Release mit einer .msi-Datei und einem Tag wie v{UpdateService.CurrentVersion.Major}.{UpdateService.CurrentVersion.Minor + 1}.0 existiert."
                        : $"Das Repository „{repo}“ ist für die Anwendung nicht sichtbar. Bitte den Namen prüfen (Besitzer/Repository); ist es privat, muss unten ein GitHub-Zugriffstoken eingetragen werden.";
                }

                throw new InvalidOperationException(response.StatusCode switch
                {
                    HttpStatusCode.NotFound => notFound,
                    HttpStatusCode.Unauthorized => "Der GitHub-Zugriffstoken ist ungültig oder abgelaufen.",
                    HttpStatusCode.Forbidden => "GitHub verweigert den Zugriff (Abfragelimit erreicht oder Token ohne Berechtigung). Bitte später erneut versuchen.",
                    _ => $"GitHub-Fehler {(int)response.StatusCode}.",
                });
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var version = ParseTag(tag);
            if (version is null)
            {
                throw new InvalidOperationException($"Der Release-Name „{tag}“ enthält keine Versionsnummer (erwartet z. B. v2.2.0).");
            }
            if (version <= CurrentVersion)
            {
                return null;
            }

            // Installationsdatei: die .msi (bei mehreren die x64-Variante)
            JsonElement? best = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (!name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) continue;
                    if (best is null || name.Contains("x64", StringComparison.OrdinalIgnoreCase)) best = a;
                }
            }
            if (best is not { } asset)
            {
                throw new InvalidOperationException($"Das Release {tag} enthält keine Installationsdatei (.msi).");
            }

            return new UpdateInfo(
                version,
                tag,
                root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                asset.GetProperty("name").GetString() ?? "update.msi",
                asset.GetProperty("url").GetString() ?? "",
                asset.GetProperty("browser_download_url").GetString() ?? "",
                asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "");
        }
    }

    /// <summary>Lädt die Installationsdatei in den Temp-Ordner und liefert den Pfad.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, string token, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "U19Update");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, info.AssetName);

        // Mit Token (privates Repository) über die API-Adresse des Assets, sonst direkter Link
        var useApi = !string.IsNullOrWhiteSpace(token);
        using var req = Request(useApi ? info.AssetUrl : info.DownloadUrl, token, "application/octet-stream");
        using var response = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Der Download ist fehlgeschlagen (GitHub-Fehler {(int)response.StatusCode}).");
        }

        var total = response.Content.Headers.ContentLength ?? info.Size;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = File.Create(target))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            var lastPercent = -1;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0 && progress is not null)
                {
                    var percent = (int)(done * 100 / total);
                    if (percent != lastPercent) { lastPercent = percent; progress.Report(percent); }
                }
            }
        }

        if (info.Size > 0 && new FileInfo(target).Length != info.Size)
        {
            File.Delete(target);
            throw new InvalidOperationException("Die heruntergeladene Datei ist unvollständig. Bitte erneut versuchen.");
        }
        return target;
    }

    /// <summary>
    /// Startet die Installation und beendet danach die Anwendung. Ein kleines PowerShell-Skript wartet, bis die
    /// Anwendung geschlossen ist, installiert die .msi still im Hintergrund (ohne Fenster; Windows fragt höchstens einmal nach Administratorrechten) und öffnet danach die neue Version.
    /// </summary>
    public static void InstallAndExit(string msiPath)
    {
        // Der Installer schreibt alle Dateien nach C:\Mitgliederverwaltung (Systemlaufwerk) und ersetzt dort bei einem Update die Dateien
        var installedExe = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Mitgliederverwaltung", "MitgliederverwaltungU19.exe");
        var running = Environment.ProcessPath;
        var relaunch = running is not null && running.StartsWith(Path.GetDirectoryName(installedExe)!, StringComparison.OrdinalIgnoreCase)
            ? running
            : installedExe;

        static string Q(string s) => s.Replace("'", "''");
        var script = string.Join("\r\n", new[]
        {
            "Start-Sleep -Seconds 3",
            "try {",
            $"  $p = Start-Process msiexec.exe -ArgumentList @('/i', '\"{Q(msiPath)}\"', '/qn', '/norestart', '/l*v', '\"{Q(Path.Combine(Path.GetDirectoryName(msiPath)!, "install.log"))}\"') -Verb RunAs -Wait -PassThru",
            "} catch { }",
            $"if (Test-Path -LiteralPath '{Q(relaunch)}') {{ Start-Process -FilePath '{Q(relaunch)}' }}",
        });
        var scriptPath = Path.Combine(Path.GetDirectoryName(msiPath)!, "install-update.ps1");
        File.WriteAllText(scriptPath, script, new System.Text.UTF8Encoding(true));

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Environment.Exit(0);
    }
}
