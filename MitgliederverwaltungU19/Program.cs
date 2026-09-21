using System.Diagnostics;
using System.Runtime.InteropServices;
using MitgliederverwaltungU19.Forms;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19;

internal static class Program
{
    private static Mutex? _instanceMutex;

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

    /// <summary>Nur eine Instanz pro Windows-Sitzung: Ein zweiter Start holt das vorhandene Fenster nach vorn und beendet sich.</summary>
    private static bool AcquireSingleInstance(bool restarted)
    {
        _instanceMutex = new Mutex(false, @"Local\AFBOE_U19_Mitgliederverwaltung");
        try
        {
            // Nach einem Neustart der Anwendung kann die alte Instanz noch kurz laufen: dann kurz warten
            if (_instanceMutex.WaitOne(restarted ? 10000 : 0)) return true;
        }
        catch (AbandonedMutexException)
        {
            return true; // die vorherige Instanz wurde hart beendet
        }

        try
        {
            var self = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(self.ProcessName))
            {
                if (p.Id == self.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(p.MainWindowHandle);
                break;
            }
        }
        catch (Exception)
        {
        }
        return false;
    }

    /// <summary>Startet die Anwendung neu (z. B. nach Abmelden oder geänderten Einstellungen) und beendet diese Instanz.</summary>
    public static void RestartApp()
    {
        if (Environment.ProcessPath is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = false });
        }
        Environment.Exit(0);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (!AcquireSingleInstance(args.Contains("--restart"))) return;
        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(Theme.Body);
        var settings = AppSettings.Load();
        SetupImport.Apply(settings); // Zugangsdaten aus dem Installer übernehmen (nur wenn neu eingegeben)

        while (true)
        {
            if (!settings.IsConfigured)
            {
                using var setup = new SettingsForm(settings, firstRun: true);
                if (setup.ShowDialog() != DialogResult.OK) return;
            }

            try
            {
                var api = new ApiClient(settings);
                // Task.Run: kein Deadlock, auch wenn bereits ein UI-Kontext existiert
                var ping = Task.Run(() => api.PingAsync()).GetAwaiter().GetResult();
                if (!RunLicenseGate(settings, api)) return;
                var signedIn = RunLoginGate(settings, api);
                if (signedIn is null) return; // Anmeldung abgebrochen
                Application.Run(new MainForm(settings, api, signedIn));
                return;
            }
            catch (Exception ex)
            {
                var retry = MessageBox.Show(
                    "Verbindung zum Server fehlgeschlagen:\n\n" + ex.Message + "\n\nEinstellungen jetzt anpassen?",
                    "Verbindungsfehler", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (retry != DialogResult.Yes) return;

                using var form = new SettingsForm(settings, firstRun: false);
                if (form.ShowDialog() != DialogResult.OK) return;
            }
        }
    }

    /// <summary>
    /// Anmeldung mit den Benutzerdaten des Web-Panels. Bei „Angemeldet bleiben“ wird die gespeicherte Sitzung verwendet,
    /// sonst (oder wenn sie abgelaufen ist) erscheint das Anmeldefenster. Liefert null, wenn abgebrochen wurde.
    /// </summary>
    private static PingResult? RunLoginGate(AppSettings settings, ApiClient api)
    {
        var message = "";
        var stored = settings.SessionToken;
        if (stored.Length > 0)
        {
            api.SessionToken = stored;
            try
            {
                var restored = Task.Run(() => api.PingAsync()).GetAwaiter().GetResult();
                if (restored.User is not null) return restored;
            }
            catch (ApiException ex)
            {
                message = ex.Code == "session_expired" ? "Die Anmeldung ist abgelaufen. Bitte neu anmelden." : ex.Message;
            }
            api.SessionToken = "";
            settings.SessionToken = "";
            settings.Save();
        }

        while (true)
        {
            using var form = new LoginForm(settings, api, message);
            if (form.ShowDialog() != DialogResult.OK) return null;
            try
            {
                return Task.Run(() => api.PingAsync()).GetAwaiter().GetResult();
            }
            catch (ApiException ex)
            {
                message = ex.Message;
                api.SessionToken = "";
            }
        }
    }

    /// <summary>
    /// Ohne gültigen Lizenzschlüssel startet die Anwendung nicht: prüft beim Server und fragt bei Bedarf nach dem Schlüssel.
    /// </summary>
    private static bool RunLicenseGate(AppSettings settings, ApiClient api)
    {
        var check = Task.Run(() => LicenseService.CheckAsync(settings, api)).GetAwaiter().GetResult();
        while (!check.Allowed)
        {
            var message = check.Status == LicenseStatus.NoKey ? "" : check.Message;
            using var form = new LicenseForm(settings, api, message, mustActivate: true);
            if (form.ShowDialog() != DialogResult.OK) return false;
            check = Task.Run(() => LicenseService.CheckAsync(settings, api)).GetAwaiter().GetResult();
        }
        return true;
    }
}
