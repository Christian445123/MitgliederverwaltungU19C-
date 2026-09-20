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
