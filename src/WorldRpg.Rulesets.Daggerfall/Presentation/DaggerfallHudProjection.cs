using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Actors;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Presentation;
using WorldRpg.Rulesets.Daggerfall.Modules.Progression;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Daggerfall's ordered HUD resource selection and wire projection.</summary>
internal sealed class DaggerfallHudProjection(IUiService ui, IMechanicsService mechanics, IReadOnlyList<DaggerfallHudResourceDefinition> resources)
{
    private readonly UiStreamHandle _hud = ui.OpenStream(new UiStreamRequest("dagger.hud", "dagger.ui.snapshot.v1"));
    private ulong _sequence;

    internal void Publish(PlayerActorState player, ProgressionState progression, EncounterTarget? encounter, PresentationState presentation)
    {
        UiValueBuilder builder = new();
        uint[] rows = resources.Select(resource => ResourceRow(builder, player, resource)).ToArray();
        uint activeEncounter = encounter is null
            ? builder.Null()
            : builder.Object(("name", builder.String(encounter.Name)), ("objective", builder.String(encounter.Objective)));
        uint root = builder.Object(
            ("resources", builder.Array(rows)),
            ("experience", builder.Number(progression.Experience)),
            ("activeEncounter", activeEncounter),
            ("lastOutcome", builder.String(presentation.LastOutcome)));
        ui.PublishProjection(new UiProjection(_hud, ++_sequence, builder.Build(root)));
    }

    private uint ResourceRow(UiValueBuilder builder, PlayerActorState player, DaggerfallHudResourceDefinition resource)
    {
        MechanicsTrackReadLeaseReceipt value = mechanics.ReadTrack(new MechanicsTrackReadRequest(player.Mechanics, resource.Track.Value, "hud_projection"));
        return builder.Object(("id", builder.String(resource.Id)), ("label", builder.String(resource.Label)), ("current", builder.Number(value.Current)), ("maximum", builder.Number(value.Maximum)));
    }
}
