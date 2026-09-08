using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Daggerfall's ordered HUD resource selection and wire projection.</summary>
internal sealed class DaggerfallHudProjection(IUiService ui, IReadOnlyList<DaggerfallHudResourceDefinition> resources, ResolvedCompositionIdentity? compositionIdentity) : IDisposable
{
    private readonly UiStream _hud = ui.OpenStream(new UiStreamRequest("dagger.hud", "dagger.ui.snapshot.v1"));
    private ulong _sequence;

    internal void Publish(PlayerActorState player, ProgressionState progression, PresentationState presentation, InventoryPresentation? inventory = null, LootPresentation? loot = null, CharacterSheetPresentation? character = null)
    {
        UiValueBuilder builder = new();
        uint[] rows = resources.Select(resource => ResourceRow(builder, player, resource)).ToArray();
        (string Key, uint Value)[] fields =
        [
            ("resources", builder.Array(rows)),
            ("experience", builder.Number(progression.Experience)),
            ("lastOutcome", builder.String(presentation.LastOutcome)),
        ];
        if (inventory is not null) fields = [.. fields, ("inventory", Inventory(builder, inventory))];
        fields = [.. fields, ("loot", loot is null ? builder.Null() : Loot(builder, loot))];
        if (character is not null) fields = [.. fields, ("character", Character(builder, character))];
        if (compositionIdentity is not null)
            fields = [.. fields, ("composition", Composition(builder, compositionIdentity))];
        uint root = builder.Object(fields);
        ui.PublishProjection(new UiProjection(_hud, ++_sequence, builder.Build(root)));
    }

    private static uint Inventory(UiValueBuilder builder, InventoryPresentation value)
    {
        uint[] items = value.Items.Select(item => Item(builder, item)).ToArray();
        uint[] slots = value.Slots.Select(slot => builder.Object(
            ("id", builder.String(slot.Id)), ("label", builder.String(slot.Label)),
            ("itemKey", slot.ItemKey is null ? builder.Null() : builder.String(slot.ItemKey)))).ToArray();
        return builder.Object(("revision", builder.String(value.Revision)), ("message", builder.String(value.Message)),
            ("items", builder.Array(items)), ("slots", builder.Array(slots)));
    }

    private static uint Item(UiValueBuilder builder, InventoryItemPresentation item) => builder.Object(
        ("key", builder.String(item.Key)), ("definition", builder.String(item.Definition)),
        ("label", builder.String(item.Label)), ("quantity", builder.String(item.Quantity)),
        ("weight", builder.Number(item.Weight)), ("value", builder.Number(item.Value)),
        ("details", builder.String(item.Details)), ("icon", item.Icon is null ? builder.Null() : builder.String(item.Icon)),
        ("gridSlot", item.GridSlot is int slot ? builder.Number(slot) : builder.Null()),
        ("equippedSlots", builder.Array(item.EquippedSlots.Select(builder.String).ToArray())),
        ("compatibleSlots", builder.Array(item.CompatibleSlots.Select(builder.String).ToArray())));

    private static uint Loot(UiValueBuilder builder, LootPresentation value) => builder.Object(
        ("container", builder.String(value.Container)), ("revision", builder.String(value.Revision)),
        ("title", builder.String(value.Title)), ("items", builder.Array(value.Items.Select(item => Item(builder, item)).ToArray())),
        ("message", builder.String(value.Message)));

    private static uint Character(UiValueBuilder builder, CharacterSheetPresentation value)
    {
        uint Stat(CharacterStatPresentation stat) => builder.Object(("id", builder.String(stat.Id)),
            ("label", builder.String(stat.Label)), ("value", builder.Number(stat.Value)));
        return builder.Object(("name", builder.String(value.Name)),
            ("attributes", builder.Array(value.Attributes.Select(Stat).ToArray())),
            ("skills", builder.Array(value.Skills.Select(Stat).ToArray())),
            ("resources", builder.Array(value.Resources.Select(resource => builder.Object(
                ("id", builder.String(resource.Id)), ("label", builder.String(resource.Label)),
                ("current", builder.Number(resource.Current)), ("maximum", builder.Number(resource.Maximum)))).ToArray())),
            ("progression", builder.Object(("level", builder.Number(value.Progression.Level)), ("experience", builder.Number(value.Progression.Experience)))),
            ("equipment", builder.Array(value.Equipment.Select(item => builder.Object(("label", builder.String(item.Label)),
                ("slots", builder.Array(item.Slots.Select(builder.String).ToArray())), ("details", builder.String(item.Details)))).ToArray())));
    }

    private static uint Composition(UiValueBuilder builder, ResolvedCompositionIdentity identity) => builder.Object(
        ("bundle", builder.String(identity.Bundle.Value)),
        ("ruleset", builder.String(identity.Ruleset.Value)),
        ("contentPacks", builder.Array(identity.ContentPacks.Select(pack => builder.String(pack.Value)).ToArray())),
        ("tuning", builder.String(identity.Tuning.Value)),
        ("fingerprint", builder.String(identity.Fingerprint)),
        ("contentFingerprint", builder.String(identity.ContentFingerprint)),
        ("tuningFingerprint", builder.String(identity.TuningFingerprint)));

    private uint ResourceRow(UiValueBuilder builder, PlayerActorState player, DaggerfallHudResourceDefinition resource)
    {
        ActorTrackRead value = player.Mechanics.ReadTrack(TrackId.Parse(resource.Track.Value));
        return builder.Object(("id", builder.String(resource.Id)), ("label", builder.String(resource.Label)), ("current", builder.Number(value.Current.Raw)), ("maximum", builder.Number(value.Bounds.Maximum.Raw)));
    }

    public void Dispose() => _hud.Dispose();
}
