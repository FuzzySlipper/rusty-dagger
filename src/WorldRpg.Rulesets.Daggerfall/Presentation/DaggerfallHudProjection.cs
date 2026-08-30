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

    internal void Publish(PlayerActorState player, ProgressionState progression, PresentationState presentation)
    {
        UiValueBuilder builder = new();
        uint[] rows = resources.Select(resource => ResourceRow(builder, player, resource)).ToArray();
        (string Key, uint Value)[] fields =
        [
            ("resources", builder.Array(rows)),
            ("experience", builder.Number(progression.Experience)),
            ("lastOutcome", builder.String(presentation.LastOutcome)),
        ];
        if (compositionIdentity is not null)
            fields = [.. fields, ("composition", Composition(builder, compositionIdentity))];
        uint root = builder.Object(fields);
        ui.PublishProjection(new UiProjection(_hud, ++_sequence, builder.Build(root)));
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
