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

    internal void Publish(PlayerActorState player, ProgressionState progression, PresentationState presentation, InventoryPresentation? inventory = null)
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
        if (compositionIdentity is not null)
            fields = [.. fields, ("composition", Composition(builder, compositionIdentity))];
        uint root = builder.Object(fields);
        ui.PublishProjection(new UiProjection(_hud, ++_sequence, builder.Build(root)));
    }

    private static uint Inventory(UiValueBuilder builder, InventoryPresentation value)
    {
        uint[] items = value.Items.Select(item => builder.Object(
            ("key", builder.String(item.Key)), ("definition", builder.String(item.Definition)),
            ("label", builder.String(item.Label)), ("quantity", builder.String(item.Quantity)),
            ("weight", builder.Number(item.Weight)), ("value", builder.Number(item.Value)),
            ("details", builder.String(item.Details)), ("icon", item.Icon is null ? builder.Null() : builder.String(item.Icon)),
            ("gridSlot", item.GridSlot is int slot ? builder.Number(slot) : builder.Null()),
            ("equippedSlots", builder.Array(item.EquippedSlots.Select(builder.String).ToArray())),
            ("compatibleSlots", builder.Array(item.CompatibleSlots.Select(builder.String).ToArray())))).ToArray();
        uint[] slots = value.Slots.Select(slot => builder.Object(
            ("id", builder.String(slot.Id)), ("label", builder.String(slot.Label)),
            ("itemKey", slot.ItemKey is null ? builder.Null() : builder.String(slot.ItemKey)))).ToArray();
        return builder.Object(("revision", builder.String(value.Revision)), ("message", builder.String(value.Message)),
            ("items", builder.Array(items)), ("slots", builder.Array(slots)));
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
