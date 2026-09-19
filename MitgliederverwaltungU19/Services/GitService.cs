using System.Diagnostics;
using System.Text;

namespace MitgliederverwaltungU19.Services;

/// <summary>Ruft das installierte Git auf (Status, Commit, Push) – ohne Shell, Argumente werden einzeln übergeben.</summary>
public sealed class GitService
{
    private readonly string _repo;

    public GitService(string repoPath) => _repo = repoPath;

    public static bool IsRepository(string path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(Path.Combine(path, ".git"));

    public async Task<(int Code, string Output)> RunAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Git konnte nicht gestartet werden.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Git wurde nicht gefunden. Bitte Git for Windows installieren (https://git-scm.com).");
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("Git hat nicht rechtzeitig geantwortet (Zeitüberschreitung).");
            }
            var text = (await stdout + await stderr).Trim();
            return (process.ExitCode, text);
        }
    }

    /// <summary>Geänderte/neue Dateien (git status --short).</summary>
    public async Task<string> StatusAsync()
    {
        var (code, output) = await RunAsync("status", "--short");
        if (code != 0) throw new InvalidOperationException(output);
        return output;
    }

    /// <summary>Alles hinzufügen, mit Kommentar committen, pushen. Meldungen gehen an <paramref name="log"/>.</summary>
    public async Task<bool> CommitAndPushAsync(string message, Action<string> log)
    {
        log("> git add -A");
        var (c1, o1) = await RunAsync("add", "-A");
        if (o1.Length > 0) log(o1);
        if (c1 != 0) return false;

        log($"> git commit -m \"{message}\"");
        var (c2, o2) = await RunAsync("commit", "-m", message);
        if (o2.Length > 0) log(o2);
        if (c2 != 0 && !o2.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase)
                    && !o2.Contains("nichts zu committen", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        log("> git push");
        var (c3, o3) = await RunAsync("push");
        if (o3.Length > 0) log(o3);
        if (c3 != 0)
        {
            log("Push fehlgeschlagen. Ist der Remote-Stand neuer? Dann zuerst in GitHub Desktop „Fetch/Pull“ ausführen.");
            return false;
        }
        return true;
    }
}
