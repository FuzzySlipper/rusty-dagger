using System.Collections.ObjectModel;
using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The session's classic held-item presentation, the base payload's <c>classicPresentation</c> section: the classic
/// weapon art each weapon item is drawn with, the art bare hands are drawn with, and the viewmodel's render order.
/// It is joined to the product-wide classic presentation every location carries, so it holds wherever a game starts.
/// </summary>
internal sealed record DaggerfallClassicSelection(IReadOnlyList<(string ItemId, string Resource)> WeaponVisuals, string UnarmedVisual, int ViewmodelRenderOrder)
{
    internal static DaggerfallClassicSelection? Read(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("classicPresentation", out JsonElement value)) return null;
        JsonElement presentation = DaggerfallBaseContent.Object(value, "classicPresentation", diagnostics);
        DaggerfallBaseContent.RejectDuplicateProperties(presentation, "classicPresentation", diagnostics);
        List<(string, string)> visuals = [];
        foreach (JsonElement entry in DaggerfallBaseContent.Array(presentation, "weaponVisuals", diagnostics))
        {
            JsonElement mapping = DaggerfallBaseContent.Object(entry, "classic weapon visual", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(mapping, "classic weapon visual", diagnostics);
            visuals.Add((DaggerfallBaseContent.Text(mapping, "itemId", diagnostics), DaggerfallBaseContent.Text(mapping, "resource", diagnostics)));
        }

        JsonElement style = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(presentation, "viewmodel", diagnostics), "classic viewmodel", diagnostics);
        DaggerfallBaseContent.RejectDuplicateProperties(style, "classic viewmodel", diagnostics);
        return new(visuals.AsReadOnly(), DaggerfallBaseContent.Text(presentation, "unarmedVisual", diagnostics),
            DaggerfallBaseContent.Integer(style, "renderOrder", diagnostics));
    }

    /// <summary>
    /// The classic presentation with this selection: every weapon item mapped to admitted weapon art, the template
    /// items an authored weapon stands for given its art, and bare hands and the viewmodel style set.
    /// </summary>
    internal NormalizedClassicPresentation Select(NormalizedClassicPresentation classic, DaggerfallDefinitions definitions, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, string> mappings = new(StringComparer.Ordinal);
        foreach ((string itemId, string resource) in WeaponVisuals)
        {
            if (!classic.Weapons.ContainsKey(resource) || !definitions.Items.TryGetValue(new DaggerfallItemId(itemId), out DaggerfallItemDefinition? item) || item.Weapon is null)
                diagnostics.Add($"Classic weapon visual '{itemId}' does not select the admitted weaponSprite resource.");
            if (!mappings.TryAdd(itemId, resource)) diagnostics.Add($"Classic weapon visuals repeat item '{itemId}'.");
        }
        foreach (DaggerfallItemDefinition item in definitions.Items.Values.Where(item => item.Weapon is not null))
            if (!mappings.ContainsKey(item.Id.Value)) diagnostics.Add($"Weapon '{item.Id.Value}' has no presentation mapping.");
        // Factory-created and enchanted weapons retain a native template identity. Join that
        // template to its authored source weapon's admitted art instead of selecting empty hands.
        foreach ((string itemId, string resource) in mappings.ToArray())
        {
            int? template = DaggerfallTemplateItemDefinitions.TemplateIndexForAuthoredItem(new(itemId));
            if (template is null) continue;
            foreach (DaggerfallItemDefinition item in definitions.TemplateItems.Values.Where(item =>
                item.Weapon is not null && item.Template?.Index == template))
                mappings.TryAdd(item.Id.Value, resource);
        }
        // A bow looses on its one fixed strike at the bow hit frame; art that ends that strike sooner
        // would spend the arrow and never release it.
        foreach ((string itemId, string resource) in mappings)
        {
            DaggerfallItemId id = new(itemId);
            DaggerfallItemDefinition? item = definitions.Items.GetValueOrDefault(id) ?? definitions.TemplateItems.GetValueOrDefault(id);
            if (item?.Weapon is not { IsBow: true } || !classic.Weapons.TryGetValue(resource, out NormalizedClassicWeapon? art)
                || !art.Actions.TryGetValue(NormalizedClassicWeapon.PlayedStrike, out NormalizedClassicWeaponAction? loosed)) continue;
            if (loosed.PlayedFrameCount <= NormalizedClassicWeapon.BowWeaponHitFrame)
                diagnostics.Add($"Bow '{itemId}' selects classic weapon '{resource}', whose '{NormalizedClassicWeapon.PlayedStrike}' plays {loosed.PlayedFrameCount} frames and never reaches the bow hit frame {NormalizedClassicWeapon.BowWeaponHitFrame}.");
        }
        if (!classic.Weapons.ContainsKey(UnarmedVisual)) diagnostics.Add("Unarmed presentation must select admitted weaponSprite media.");
        return classic with
        {
            CompatibleItemVisuals = new ReadOnlyDictionary<string, string>(mappings), UnarmedVisual = UnarmedVisual,
            Viewmodel = new ClassicViewmodelStyle(ViewmodelRenderOrder),
        };
    }
}
