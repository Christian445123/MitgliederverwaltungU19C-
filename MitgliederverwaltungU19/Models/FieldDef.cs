namespace MitgliederverwaltungU19.Models;

public enum FieldType { Text, Int, Date, Bool, Status, Kader, Multiline }

/// <summary>Beschreibt ein Mitgliedsfeld (Name in der API, Beschriftung, Typ, Registerkarte).</summary>
public sealed record FieldDef(string Key, string Label, FieldType Type, string Group, bool Required = false);

public static class Fields
{
    public const string GStamm = "Stammdaten";
    public const string GKontakt = "Kontakt & Adresse";
    public const string GCamps = "Camps & Zustimmung";
    public const string GDoku = "Dokumente";
    public const string GAusr = "Ausrüstung";

    /// <summary>Feldnamen entsprechen der Web-API (siehe includes/member_columns.php).</summary>
    public static readonly IReadOnlyList<FieldDef> All = new List<FieldDef>
    {
        new("nachname", "Nachname", FieldType.Text, GStamm, true),
        new("vorname", "Vorname", FieldType.Text, GStamm, true),
        new("jersey_nr", "Jersey Nr.", FieldType.Text, GStamm),
        new("sz", "Selbstzahler", FieldType.Text, GStamm),
        new("bezirk", "Bezirk", FieldType.Text, GStamm),
        new("position", "Position", FieldType.Text, GStamm),
        new("geburtsdatum", "Geburtsdatum", FieldType.Date, GStamm),
        new("verein", "Verein", FieldType.Text, GStamm),
        new("groesse_cm", "Größe (cm)", FieldType.Int, GStamm),
        new("gewicht_kg", "Gewicht (kg)", FieldType.Int, GStamm),
        new("status", "Status", FieldType.Status, GStamm),
        new("kader", "Kader", FieldType.Kader, GStamm),

        new("email", "E-Mail", FieldType.Text, GKontakt, true),
        new("telefon", "Telefon Spieler", FieldType.Text, GKontakt),
        new("plz", "PLZ", FieldType.Text, GKontakt),
        new("ort", "Ort", FieldType.Text, GKontakt),
        new("strasse", "Straße", FieldType.Text, GKontakt),
        new("erz_name", "Erziehungsberechtigte(r)", FieldType.Text, GKontakt),
        new("erz_telefon", "Telefon Erzieh", FieldType.Text, GKontakt),
        new("erz_email", "E-Mail Erziehungsber.", FieldType.Text, GKontakt),

        new("camp_1", "Camp 1", FieldType.Bool, GCamps),
        new("spanien", "Spanien", FieldType.Bool, GCamps),
        new("camp_2", "Camp 2", FieldType.Bool, GCamps),
        new("tschechien", "Tschechien", FieldType.Bool, GCamps),
        new("rechte_pflichten_akzeptiert", "Rechte & Pflichten akzeptiert", FieldType.Bool, GCamps),

        new("sozialversicherungsnummer", "Sozialversicherungsnr.", FieldType.Text, GDoku),
        new("nada_zertifikat", "NADA Zertifikat", FieldType.Text, GDoku),
        new("nada_gueltig_bis", "NADA gültig bis", FieldType.Date, GDoku),
        new("reisepass_nr", "Reisepass Nr.", FieldType.Text, GDoku),
        new("reisepass_ausgestellt_am", "Reisepass ausgestellt am", FieldType.Date, GDoku),
        new("reisepass_gueltig_bis", "Reisepass gültig bis", FieldType.Date, GDoku),
        new("geburtsland", "Geburtsland", FieldType.Text, GDoku),
        new("geburtsort", "Geburtsort", FieldType.Text, GDoku),
        new("ausstellungsbehoerde", "Ausstellungsbehörde", FieldType.Text, GDoku),

        new("essen", "Essen (Allergien/Wünsche)", FieldType.Multiline, GAusr),
        new("game_jersey_groesse", "Game Jersey Größe", FieldType.Text, GAusr),
        new("game_hosen_groesse", "Game Hosen Größe", FieldType.Text, GAusr),
        new("helm_groesse", "Helm Größe", FieldType.Text, GAusr),
        new("helm_eigener", "Eigener Helm", FieldType.Bool, GAusr),
        new("tshirt_polo_groesse", "T-Shirt & Polo Größe", FieldType.Text, GAusr),
        new("hoodie_groesse", "Hoodie Größe", FieldType.Text, GAusr),
        new("mesh_shorts_groesse", "Mesh Shorts Größe", FieldType.Text, GAusr),
        new("socken_groesse", "Socken Größe", FieldType.Text, GAusr),
        new("zimmer_nr", "Zimmer Nr", FieldType.Text, GAusr),
        new("pract_jersey_nr", "Pract. Jersey Nr.", FieldType.Text, GAusr),
        new("pract_hose_groesse", "Pract. Hose Größe", FieldType.Text, GAusr),
    };

    public static readonly string[] Groups = { GStamm, GKontakt, GCamps, GDoku, GAusr };
}

/// <summary>Dokumenttypen (Schlüssel wie in der API).</summary>
public static class DocumentTypes
{
    public static readonly IReadOnlyList<(string Key, string Label)> All = new List<(string, string)>
    {
        ("ecard", "E-Card Vorderseite"),
        ("ecard_back", "E-Card Rückseite"),
        ("pass", "Reisepass Vorderseite"),
        ("pass_back", "Reisepass Rückseite"),
        ("nada", "NADA-Zertifikat (1 Seite)"),
        ("rechte", "Rechte & Pflichten (unterschrieben)"),
    };
}
