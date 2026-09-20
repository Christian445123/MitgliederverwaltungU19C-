using MitgliederverwaltungU19.Forms;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
