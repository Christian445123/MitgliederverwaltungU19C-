namespace MitgliederverwaltungU19.Models;

/// <summary>Eine Berechtigung (Schritt) mit Beschriftung und Gruppe, wie im Web-Panel unter Rollen.</summary>
public sealed record PermissionDef(string Key, string Label, string Group);

public sealed record RoleInfo(int Id, string Key, string Name, string Description, bool IsSystem, int Users, IReadOnlySet<string> Permissions)
{
    public bool IsAdministrator => Key == "administrator";
    public override string ToString() => Name;
}

public sealed record UserRow(int Id, string Username, bool IsAdmin, int? RoleId, string RoleName, int Overrides, bool MustChangePassword, string CreatedAt);

/// <summary>Benutzer mit Einzelrechten: Berechtigung → "allow" (erlaubt) oder "deny" (verboten).</summary>
public sealed record UserDetail(int Id, string Username, bool IsAdmin, int? RoleId, IReadOnlyDictionary<string, string> Overrides);

/// <summary>Feld-Recht: was Spieler (persönlicher Link) und Bearbeiter bei einem Feld dürfen.</summary>
public sealed class FieldPerm
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Group { get; set; } = "";
    public bool AdminOnly { get; set; }
    public bool Core { get; set; }

    /// <summary>"edit" (ansehen und ändern), "view" (nur ansehen) oder "hidden" (ausgeblendet).</summary>
    public string Player { get; set; } = "edit";

    /// <summary>Bearbeiter (Rolle „Bearbeiter“) sehen das Feld.</summary>
    public bool Editor { get; set; } = true;
}

public sealed record CampInfo(int Id, string Name, int Members);

public sealed class LogFilter
{
    public string Source { get; set; } = "";
    public string Level { get; set; } = "";
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Query { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed record LogEntry(long Id, string Time, string Source, string Level, string Action, string Actor, string Message, string Ip, string Method, string Path, string Status, string Details);

public sealed record LogPage(int Total, int Page, int Pages, List<LogEntry> Entries, int Requests24h, int Errors24h, int Warnings24h, int FailedLogins24h, List<string> Actions);

/// <summary>Persönlicher Zugangslink eines Mitglieds; Password ist nur nach dem Erzeugen/Versenden gefüllt.</summary>
public sealed record LinkInfo(string Name, string Email, string Link, string? VerifiedAt, string? Password, string Message);

/// <summary>Ergebnis eines Massenmail-Versands je Person: status = sent, no_email oder error.</summary>
public sealed record SendLinkResult(int Id, string Name, string Status, string Message);

/// <summary>Registrierungslink für neue Mitglieder oder Staff (Bereich „Neue Mitglieder“), siehe admin/registrations.php.
/// LinkType: "player" (Spieler, Standard) oder "staff".</summary>
public sealed record RegistrationLink(int Id, string Token, string Url, string? Label, string LinkType, bool Active, string? CreatedBy, string CreatedAt, string? ExpiresAt, int UseCount, string? LastUsedAt)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? "(ohne Bezeichnung)" : Label;
    public bool IsStaff => LinkType == "staff";
}

/// <summary>Ausstehende Staff-Anmeldung über den Staff-Einladungslink (staff.status = "neu").</summary>
public sealed record PendingStaff(int Id, string NameVorname, string? Position, string? Email, string? Telefon, string? CreatedAt)
{
    public string DisplayName => string.IsNullOrWhiteSpace(NameVorname) ? "(ohne Namen)" : NameVorname;
}
