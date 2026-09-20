namespace MitgliederverwaltungU19.Services;

/// <summary>
/// Erkennt Personen mit einer Staff-Position (Trainer, Betreuer, Funktionäre), die versehentlich unter den Spielern
/// geführt werden. Gleiche Regeln wie auf dem Server (Roster): HC, OC, DC, TM, ST, „TM ASS“, „ASS TM“, AC sowie alles mit
/// „Coach“, „Trainer“, „Betreuer“, „Manager“, „Physio“, „Arzt“, „Funktionär“ oder „Teamleiter“.
/// </summary>
public static class StaffPositions
{
    private static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "HC", "OC", "DC", "TM", "ST", "TM ASS", "ASS TM", "AC", "ASS COACH", "ASSISTANT COACH",
    };

    private static readonly string[] Words = { "COACH", "TRAINER", "BETREUER", "MANAGER", "PHYSIO", "ARZT", "FUNKTIONÄR", "FUNKTIONAER", "TEAMLEITER", "CHEF DE MISSION" };

    public static bool IsStaff(string? position)
    {
        var text = string.Join(' ', (position ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        if (text.Length == 0) return false;
        return Codes.Contains(text) || Words.Any(text.Contains);
    }
}
