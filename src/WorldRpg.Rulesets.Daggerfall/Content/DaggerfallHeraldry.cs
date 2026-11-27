using System.Globalization;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>The heraldic colours: two metals (or, argent) and five colours.</summary>
internal enum DaggerfallTincture { Or, Argent, Gules, Azure, Vert, Sable, Purpure }

/// <summary>How a shield's field is divided between its two tinctures.</summary>
internal enum DaggerfallFieldDivision { Plain, PerPale, PerFess, PerBend, PerSaltire, Quarterly }

/// <summary>
/// One coat of arms: its field (with a second tincture where the field is divided), its charge and the charge's tincture.
/// A template charge is an ordinary or a simple figure any shield can bear; a pictorial charge is drawn art, named by
/// <paramref name="Pictorial"/> (the words a blazon gives it), with where it comes from in <paramref name="Source"/>.
/// </summary>
internal sealed record DaggerfallArms(string Id, DaggerfallTincture Field, DaggerfallFieldDivision Division,
    DaggerfallTincture? Second, string Charge, DaggerfallTincture ChargeTincture, string? Pictorial, string? Source)
{
    /// <summary>The board seat whose arms these are, or null for a region's.</summary>
    internal string? Seat => Id.StartsWith("seat:", StringComparison.Ordinal) ? Id["seat:".Length..] : null;

    /// <summary>The template charges and the words a blazon gives them.</summary>
    internal static IReadOnlyDictionary<string, string> TemplateCharges { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["pale"] = "a pale", ["fess"] = "a fess", ["bend"] = "a bend", ["chevron"] = "a chevron", ["cross"] = "a cross",
        ["saltire"] = "a saltire", ["chief"] = "a chief", ["bordure"] = "a bordure", ["pile"] = "a pile",
        ["roundel"] = "a roundel", ["annulet"] = "an annulet", ["lozenge"] = "a lozenge", ["mullet"] = "a mullet of eight points",
        ["crescent"] = "a crescent", ["tower"] = "a tower",
    };

    /// <summary>The arms in words: the field, then the charge in its tincture, as <c>Vert, a dragon rampant gules</c>.</summary>
    internal string Blazon
    {
        get
        {
            string shield = Division == DaggerfallFieldDivision.Plain ? Word(Field)
                : $"{DivisionWord(Division)} {Word(Field).ToLowerInvariant()} and {Word(Second!.Value).ToLowerInvariant()}";
            return $"{shield}, {Pictorial ?? TemplateCharges[Charge]} {Word(ChargeTincture).ToLowerInvariant()}";
        }
    }

    /// <summary>
    /// The tincture a livery takes from the arms: the field's, or the charge's where the field is a metal, so the colour
    /// is a colour rather than gold or white.
    /// </summary>
    internal DaggerfallTincture Livery => IsMetal(Field) ? ChargeTincture : Field;

    internal static bool IsMetal(DaggerfallTincture tincture) => tincture is DaggerfallTincture.Or or DaggerfallTincture.Argent;

    internal static string Word(DaggerfallTincture tincture) => tincture.ToString();

    private static string DivisionWord(DaggerfallFieldDivision division) => division switch
    {
        DaggerfallFieldDivision.PerPale => "Per pale",
        DaggerfallFieldDivision.PerFess => "Per fess",
        DaggerfallFieldDivision.PerBend => "Per bend",
        DaggerfallFieldDivision.PerSaltire => "Per saltire",
        _ => "Quarterly",
    };
}

/// <summary>
/// The heraldry of the Bay (#11144, settled with Patch on thread den-01M8DYVZ1KFKFK841V280Y7KZ9), the one home of its
/// arms and colours: <c>daggerfall.base/world/heraldry.json</c>. Each crown seat and the Empire bear arms, from the
/// classic castles' tapestries where the game has them; each board region bears its own, from a template of two
/// tinctures and a charge, but a crown's home region bears its crown's. Whose arms a region shows now is its liege's
/// (<c>DaggerfallRegionLiege</c>); this holds only the arms.
/// </summary>
internal sealed class DaggerfallHeraldry
{
    internal static DaggerfallHeraldry Empty { get; } = new(new Dictionary<DaggerfallTincture, Color>(), new Dictionary<string, DaggerfallArms>(StringComparer.Ordinal),
        new Dictionary<string, DaggerfallArms>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<DaggerfallTincture, Color> _tinctures;

    private DaggerfallHeraldry(IReadOnlyDictionary<DaggerfallTincture, Color> tinctures, IReadOnlyDictionary<string, DaggerfallArms> seats,
        IReadOnlyDictionary<string, DaggerfallArms> regions)
    {
        _tinctures = tinctures;
        Seats = seats;
        Regions = regions;
    }

    /// <summary>Each seat's arms by board seat id.</summary>
    internal IReadOnlyDictionary<string, DaggerfallArms> Seats { get; }

    /// <summary>Each board region's own arms by board region id, a crown's home region bearing its crown's.</summary>
    internal IReadOnlyDictionary<string, DaggerfallArms> Regions { get; }

    /// <summary>The colour <paramref name="tincture"/> is drawn in.</summary>
    internal Color Colour(DaggerfallTincture tincture) => _tinctures[tincture];

    /// <summary><paramref name="colour"/> (linear, as the Engine holds it) as sRGB <c>#RRGGBB</c>, white for none.</summary>
    internal static string Hex(Color? colour)
    {
        Color c = (colour ?? new Color(1f, 1f, 1f, 1f)).ToSrgb();
        static int Channel(float value) => Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{Channel(c.R):X2}{Channel(c.G):X2}{Channel(c.B):X2}");
    }

    /// <summary>
    /// Reads the section, checking that every board region of <paramref name="regions"/> has arms (a crown's home region
    /// through its crown, <paramref name="crownHomes"/>), and that no arms name a region or seat the board lacks.
    /// </summary>
    internal static DaggerfallHeraldry Read(JsonElement section, IReadOnlyCollection<string> seats, IReadOnlyCollection<string> regions,
        IReadOnlyDictionary<string, string> crownHomes, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<DaggerfallTincture, Color> tinctures = [];
        JsonElement tinctureSection = section.GetProperty("tinctures");
        foreach (DaggerfallTincture tincture in Enum.GetValues<DaggerfallTincture>())
        {
            string key = tincture.ToString().ToLowerInvariant();
            if (!tinctureSection.TryGetProperty(key, out JsonElement hex) || hex.GetString() is not { Length: 7 } text || text[0] != '#'
                || !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                diagnostics.Add($"Heraldry gives tincture '{key}' no #rrggbb colour.");
                continue;
            }
            tinctures[tincture] = Color.FromSrgbHex(rgb, 1f);
        }

        Dictionary<string, DaggerfallArms> seatArms = Arms(section.GetProperty("seats"), "seat", diagnostics);
        Dictionary<string, DaggerfallArms> regionArms = Arms(section.GetProperty("regions"), "region", diagnostics);
        foreach (string seat in seatArms.Keys.Where(seat => !seats.Contains(seat)))
            diagnostics.Add($"Heraldry gives arms to seat '{seat}', which the board lacks.");
        foreach (string region in regionArms.Keys.Where(region => !regions.Contains(region)))
            diagnostics.Add($"Heraldry gives arms to region '{region}', which the board lacks.");
        foreach ((string region, string crown) in crownHomes)
        {
            if (regionArms.ContainsKey(region))
                diagnostics.Add($"Heraldry gives crown home region '{region}' arms of its own; it bears its crown's.");
            else if (seatArms.TryGetValue(crown, out DaggerfallArms? arms))
                regionArms[region] = arms;
            else
                diagnostics.Add($"Heraldry gives crown '{crown}' no arms.");
        }
        foreach (string region in regions.Where(region => !regionArms.ContainsKey(region)))
            diagnostics.Add($"Heraldry gives board region '{region}' no arms.");
        return new(tinctures, seatArms, regionArms);
    }

    private static Dictionary<string, DaggerfallArms> Arms(JsonElement section, string kind, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, DaggerfallArms> arms = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in section.EnumerateObject())
        {
            JsonElement value = entry.Value;
            DaggerfallTincture? Tincture(string property) =>
                value.TryGetProperty(property, out JsonElement word) && Enum.TryParse(word.GetString(), true, out DaggerfallTincture parsed)
                    && Enum.IsDefined(parsed) ? parsed : null;
            DaggerfallTincture? field = Tincture("field"), charge = Tincture("chargeTincture"), second = Tincture("second");
            DaggerfallFieldDivision division = DaggerfallFieldDivision.Plain;
            if (value.TryGetProperty("division", out JsonElement divisionWord)
                && !Enum.TryParse(divisionWord.GetString()!.Replace("-", "", StringComparison.Ordinal), true, out division))
                diagnostics.Add($"Heraldry {kind} '{entry.Name}' has unknown division '{divisionWord.GetString()}'.");
            string? chargeName = value.TryGetProperty("charge", out JsonElement c) ? c.GetString() : null;
            string? pictorial = value.TryGetProperty("pictorial", out JsonElement p) ? p.GetString() : null;
            string? source = value.TryGetProperty("source", out JsonElement s) ? s.GetString() : null;
            if (field is null || charge is null || string.IsNullOrWhiteSpace(chargeName)
                || (division == DaggerfallFieldDivision.Plain) != (second is null)
                || pictorial is null && !DaggerfallArms.TemplateCharges.ContainsKey(chargeName)
                || pictorial is not null && string.IsNullOrWhiteSpace(source))
            {
                diagnostics.Add($"Heraldry {kind} '{entry.Name}' needs a field, a second tincture exactly when divided, and a template charge or a pictorial charge with its source.");
                continue;
            }
            arms[entry.Name] = new($"{kind}:{entry.Name}", field.Value, division, second, chargeName, charge.Value, pictorial, source);
        }
        return arms;
    }
}
