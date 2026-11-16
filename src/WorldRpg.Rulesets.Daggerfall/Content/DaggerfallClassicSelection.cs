using System.Collections.ObjectModel;
using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The session's classic held-item presentation. The base payload's <c>classicPresentation</c> section names what no
/// item template states, the art bare hands are drawn with and the viewmodel's render order; each weapon item is drawn
/// with the classic weapon art its template names (<see cref="DaggerfallItemTemplateDefinition.WeaponMediaId"/>, the
/// id the classic weapon atlas is published under), as the donor chooses a weapon's first-person art by its template.
/// It is joined to the product-wide classic presentation every location carries, so it holds wherever a game starts.
/// </summary>
internal sealed record DaggerfallClassicSelection(string UnarmedVisual, int ViewmodelRenderOrder)
{
    private static readonly string[] Fields = ["unarmedVisual", "viewmodel"];

    internal static DaggerfallClassicSelection? Read(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("classicPresentation", out JsonElement value)) return null;
        JsonElement presentation = DaggerfallBaseContent.Object(value, "classicPresentation", diagnostics);
        DaggerfallBaseContent.RejectDuplicateProperties(presentation, "classicPresentation", diagnostics);
        // A weapon's art is its template's, so a list of art per item here would load and be drawn by nothing.
        foreach (JsonProperty field in presentation.EnumerateObject().Where(field => !Fields.Contains(field.Name, StringComparer.Ordinal)))
            diagnostics.Add($"classicPresentation sets '{field.Name}', which it does not carry: a weapon item draws the art its template names.");
        JsonElement style = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(presentation, "viewmodel", diagnostics), "classic viewmodel", diagnostics);
        DaggerfallBaseContent.RejectDuplicateProperties(style, "classic viewmodel", diagnostics);
        return new(DaggerfallBaseContent.Text(presentation, "unarmedVisual", diagnostics), DaggerfallBaseContent.Integer(style, "renderOrder", diagnostics));
    }

    /// <summary>
    /// The classic presentation with this selection: every weapon item, authored or made from a template (in a material,
    /// enchanted), mapped to the admitted weapon art its template names, and bare hands and the viewmodel style set.
    /// </summary>
    internal NormalizedClassicPresentation Select(NormalizedClassicPresentation classic, DaggerfallDefinitions definitions, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, string> visuals = new(StringComparer.Ordinal);
        // Authored items first, as an item id resolves (DaggerfallDefinitions.TryResolveItem).
        foreach (DaggerfallItemDefinition item in definitions.Items.Values.Concat(definitions.TemplateItems.Values).Where(item => item.Weapon is not null))
        {
            DaggerfallItemTemplateDefinition? template = definitions.SourceTemplateFor(item);
            string resource = template?.WeaponMediaId ?? string.Empty;
            if (!classic.Weapons.TryGetValue(resource, out NormalizedClassicWeapon? art))
            {
                diagnostics.Add($"Weapon '{item.Id.Value}' draws no classic weapon art: " + (template is null ? "it is made from no item template."
                    : resource.Length == 0 ? $"its template {template.Index} names none." : $"its template {template.Index} names '{resource}', which is not admitted weaponSprite media."));
                continue;
            }
            visuals.TryAdd(item.Id.Value, resource);
            // A bow looses on its one fixed strike at the bow hit frame; art that ends that strike sooner
            // would spend the arrow and never release it.
            if (item.Weapon is { IsBow: true } && art.Actions.TryGetValue(NormalizedClassicWeapon.PlayedStrike, out NormalizedClassicWeaponAction? loosed)
                && loosed.PlayedFrameCount <= NormalizedClassicWeapon.BowWeaponHitFrame)
                diagnostics.Add($"Bow '{item.Id.Value}' selects classic weapon '{resource}', whose '{NormalizedClassicWeapon.PlayedStrike}' plays {loosed.PlayedFrameCount} frames and never reaches the bow hit frame {NormalizedClassicWeapon.BowWeaponHitFrame}.");
        }
        if (!classic.Weapons.ContainsKey(UnarmedVisual)) diagnostics.Add("Unarmed presentation must select admitted weaponSprite media.");
        return classic with
        {
            CompatibleItemVisuals = new ReadOnlyDictionary<string, string>(visuals), UnarmedVisual = UnarmedVisual,
            Viewmodel = new ClassicViewmodelStyle(ViewmodelRenderOrder),
        };
    }
}
