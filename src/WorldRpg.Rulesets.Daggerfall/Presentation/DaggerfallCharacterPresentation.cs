using System.Globalization;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Read-only player-sheet projection from the current Mechanics, progression, and equipment state.</summary>
internal sealed record CharacterStatPresentation(string Id, string Label, long Value);
internal sealed record CharacterResourcePresentation(string Id, string Label, long Current, long Maximum);
internal sealed record CharacterProgressionPresentation(int Level, int Experience);
internal sealed record CharacterEquipmentPresentation(string Label, string[] Slots, string Details);
internal sealed record CharacterSheetPresentation(
    string Name,
    CharacterStatPresentation[] Attributes,
    CharacterStatPresentation[] Skills,
    CharacterResourcePresentation[] Resources,
    CharacterProgressionPresentation Progression,
    CharacterEquipmentPresentation[] Equipment,
    CharacterIdentityPresentation? Identity = null);

/// <summary>One presentation layer a character is drawn from, by the identity a consumer resolves.</summary>
/// <param name="Layer">The layer's role, as the publication names it.</param>
/// <param name="MediaId">The published identity of the canvas.</param>
internal sealed record CharacterMediaIdentity(string Layer, string MediaId);

/// <summary>
/// The media identities the character's race and career resolve to, published so a sheet draws from
/// the same records everything else resolves rather than reconstructing file names.
/// </summary>
/// <param name="Race">The race identity the layers belong to, or empty when the actor names none.</param>
/// <param name="DonorRaceId">The donor's race value for that race, or zero.</param>
/// <param name="Portrait">The career's portrait identity, or empty when the career has none or none is named.</param>
/// <param name="Media">Every layer the race publishes, in the order the publication names them.</param>
internal sealed record CharacterIdentityPresentation(string Race, int DonorRaceId, string Portrait, CharacterMediaIdentity[] Media)
{
    /// <summary>
    /// Resolves an actor's declared race and career through the published presentation set.
    /// </summary>
    /// <remarks>
    /// An actor that declares no race has no paper doll, and this says so by returning nothing rather
    /// than by choosing a race: the identities are authored facts, and a default would draw every
    /// unstated character as whatever the default happened to be.
    /// </remarks>
    /// <summary>The published name of a layer, which is what the section keys it by.</summary>
    private static string LayerName(DaggerfallCharacterLayerDefinition layer) => layer.Kind switch
    {
        DaggerfallCharacterLayerKind.Background => "background",
        DaggerfallCharacterLayerKind.BodyUnclothed => $"body.{Gender(layer.Gender)}.unclothed",
        DaggerfallCharacterLayerKind.BodyClothed => $"body.{Gender(layer.Gender)}.clothed",
        _ => $"head.{Gender(layer.Gender)}.{layer.HeadIndex}",
    };

    private static string Gender(DaggerfallCharacterGender? gender) => gender == DaggerfallCharacterGender.Female ? "female" : "male";

    internal static CharacterIdentityPresentation? From(DaggerfallDefinitions definitions, DaggerfallActorDefinition? actor)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (actor?.Race is not { Length: > 0 } race)
        {
            return null;
        }

        DaggerfallRaceLayers layers = definitions.CharacterPresentation.RequireRace(race);
        string portrait = actor.Career is { Length: > 0 } career && definitions.CharacterPresentation.Careers.ContainsKey(career)
            ? definitions.CharacterPresentation.RequirePortrait(career).MediaId
            : string.Empty;
        return new CharacterIdentityPresentation(
            race,
            layers.DonorRaceId,
            portrait,
            [.. layers.Layers.Select(layer => new CharacterMediaIdentity(LayerName(layer), layer.MediaId))]);
    }
}

/// <summary>
/// Daggerfall's character-sheet policy. Mechanics supplies live stat and
/// resource values; definitions retain the ordered vocabulary and item meaning.
/// </summary>
internal sealed class DaggerfallCharacterPresentation(
    DaggerfallDefinitions definitions,
    DaggerfallActorDefinition playerDefinition,
    MechanicsEquipmentCoordinator equipment)
{
    internal CharacterSheetPresentation Read(PlayerActorState player, ProgressionState progression)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(progression);
        return new CharacterSheetPresentation(
            "Player",
            Stats(player, definitions.Vocabulary.Attributes),
            Stats(player, definitions.Vocabulary.Skills),
            definitions.HudResources.Select(resource => Resource(player, resource)).ToArray(),
            new CharacterProgressionPresentation(progression.Level, progression.Experience),
            Equipment(),
            CharacterIdentityPresentation.From(definitions, playerDefinition));
    }

    private CharacterStatPresentation[] Stats(PlayerActorState player, IReadOnlyList<DaggerfallStatId> ids) => ids
        .Where(id => playerDefinition.Stats.Values.ContainsKey(id))
        .Select(id => new CharacterStatPresentation(id.Value, Label(id.Value), player.Mechanics.ReadStat(StatId.Parse(id.Value)).Value.Raw))
        .ToArray();

    private static CharacterResourcePresentation Resource(PlayerActorState player, DaggerfallHudResourceDefinition resource)
    {
        ActorTrackRead value = player.Mechanics.ReadTrack(TrackId.Parse(resource.Track.Value));
        return new CharacterResourcePresentation(resource.Id, resource.Label, value.Current.Raw, value.Bounds.Maximum.Raw);
    }

    private CharacterEquipmentPresentation[] Equipment() => equipment.Read().Assignments
        .GroupBy(assignment => assignment.Item.EntityId)
        .Select(group =>
        {
            DaggerfallItemDefinition item = definitions.Items[new DaggerfallItemId(group.First().Item.Definition.Value)];
            return new CharacterEquipmentPresentation(
                Label(item.Id.Value),
                group.Select(assignment => Label(assignment.Slot.Value)).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                Details(item));
        })
        .OrderBy(item => item.Label, StringComparer.Ordinal)
        .ToArray();

    private static string Details(DaggerfallItemDefinition item) => item.Weapon is { } weapon
        ? $"Attack {weapon.MinimumDamage}–{weapon.MaximumDamage}; {Label(weapon.Material)}; {Label(weapon.Skill)}"
        : item.Armor is { } armor ? $"Armor material: {Label(armor.Material)}"
        : item.Shield is { } shield ? $"Shield armor: {shield.Armor.ToString(CultureInfo.InvariantCulture)}"
        : "Equipment";

    private static string Label(string id) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' '));
}
