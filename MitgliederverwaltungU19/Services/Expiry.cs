using System.Globalization;
using MitgliederverwaltungU19.Models;

namespace MitgliederverwaltungU19.Services;

/// <summary>
/// Ok = in Ordnung, Notice = gelb, Urgent = blau, Soon = orange (Reisepass), Expired = rot.
/// </summary>
public enum ExpiryState { Ok, Notice, Urgent, Soon, Expired }

public sealed record ExpiryItem(Member Member, string Document, DateTime Date, ExpiryState State, int Days)
{
    public string Describe() => Days < 0
        ? $"abgelaufen seit {Math.Abs(Days)} {(Math.Abs(Days) == 1 ? "Tag" : "Tagen")}"
        : Days == 0 ? "läuft heute ab"
        : Days > 60 ? $"läuft in ca. {(int)Math.Round(Days / 30.0)} Monaten ab"
        : $"läuft in {Days} {(Days == 1 ? "Tag" : "Tagen")} ab";
}

/// <summary>
/// Ablauf-Erinnerungen (gleiche Regeln wie die Web-Anwendung), nur für aktive Mitglieder:
///   NADA-Zertifikat: ab 1 Monat vorher GELB, ab 7 Tagen vorher BLAU, am Ablauftag und danach ROT.
///   Reisepass:       ab 6 Monaten vorher ORANGE, nach dem Ablauftag ROT.
/// </summary>
public static class Expiry
{
    public const int NadaWarnMonths = 1;
    public const int NadaUrgentDays = 7;
    public const int PassWarnMonths = 6;

    public static (ExpiryState State, int Days, DateTime? Date) Check(
        string? value, DateTime limit, DateTime today, int? urgentDays = null, bool redOnDay = false)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return (ExpiryState.Ok, 0, null);
        }
        date = date.Date;
        if (date > limit) return (ExpiryState.Ok, 0, date);

        var days = (int)(date - today).TotalDays;
        ExpiryState state;
        if (days < 0 || (redOnDay && days == 0)) state = ExpiryState.Expired;
        else if (urgentDays is { } u) state = days <= u ? ExpiryState.Urgent : ExpiryState.Notice;
        else state = ExpiryState.Soon;
        return (state, days, date);
    }

    public static (ExpiryState State, int Days, DateTime? Date) Nada(Member m, DateTime? today = null)
    {
        var t = (today ?? DateTime.Today).Date;
        return Check(m.Get("nada_gueltig_bis"), t.AddMonths(NadaWarnMonths), t, NadaUrgentDays, redOnDay: true);
    }

    public static (ExpiryState State, int Days, DateTime? Date) Pass(Member m, DateTime? today = null)
    {
        var t = (today ?? DateTime.Today).Date;
        return Check(m.Get("reisepass_gueltig_bis"), t.AddMonths(PassWarnMonths), t);
    }

    /// <summary>Hintergrund- und Schriftfarbe je Stufe (gelb / blau / orange / rot).</summary>
    public static (Color Back, Color Fore) Colors(ExpiryState state) => state switch
    {
        ExpiryState.Expired => (Color.FromArgb(0xFE, 0xE2, 0xE2), Color.FromArgb(0xB9, 0x1C, 0x1C)),
        ExpiryState.Urgent => (Color.FromArgb(0xDB, 0xEA, 0xFE), Color.FromArgb(0x1E, 0x40, 0xAF)),
        ExpiryState.Notice => (Color.FromArgb(0xFE, 0xF9, 0xC3), Color.FromArgb(0x85, 0x4D, 0x0E)),
        ExpiryState.Soon => (Theme.AccentSoft, Theme.AccentDark),
        _ => (Color.White, Color.Black),
    };

    /// <summary>Alle aktiven Mitglieder mit abgelaufenem oder bald ablaufendem Dokument (überfällige zuerst).</summary>
    public static List<ExpiryItem> Find(IEnumerable<Member> members)
    {
        var items = new List<ExpiryItem>();
        foreach (var m in members.Where(m => m.Get("status") != "inaktiv"))
        {
            var nada = Nada(m);
            if (nada.State != ExpiryState.Ok && nada.Date is { } nd)
                items.Add(new ExpiryItem(m, "NADA-Zertifikat", nd, nada.State, nada.Days));
            var pass = Pass(m);
            if (pass.State != ExpiryState.Ok && pass.Date is { } pd)
                items.Add(new ExpiryItem(m, "Reisepass", pd, pass.State, pass.Days));
        }
        return items.OrderBy(i => i.Days).ThenBy(i => i.Member.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
