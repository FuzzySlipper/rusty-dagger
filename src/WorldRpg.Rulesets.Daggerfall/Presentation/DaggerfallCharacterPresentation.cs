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
    CharacterEquipmentPresentation[] Equipment);

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
            Equipment());
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
