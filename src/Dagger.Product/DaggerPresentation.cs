using Rusty.Engine.Native;

namespace RustyDagger.Product;

/// <summary>Owns the HUD contract and authored enemy sprite facts. World mesh publication awaits Engine resource admission.</summary>
public sealed class DaggerPresentation
{
    private readonly EngineApi _engine;
    private readonly NativeUiStreamHandle _hud;
    private readonly Dictionary<long, NativeAppearanceHandle> _sprites = [];
    private ulong _sequence;

    public DaggerPresentation(EngineApi engine, ProjectFacts project)
    {
        _engine = engine;
        _hud = engine.Ui.OpenStream("dagger.hud", "dagger.ui.snapshot.v1");
        foreach (var actor in project.Actors.Values.Where(actor => actor.Sprite is not null))
            _sprites[actor.EntityId] = CreateSprite(actor.Sprite!);
    }

    public void Publish(DaggerGameState state)
    {
        var player = state.Player;
        var builder = new StructuredValueBuilder();
        var playerValue = builder.Object(
            ("health", builder.Number(player.Health)), ("maximumHealth", builder.Number(player.Definition.MaximumHealth)),
            ("stamina", builder.Number(player.Stamina)), ("maximumStamina", builder.Number(player.Definition.MaximumStamina)),
            ("magicka", builder.Number(player.Magicka)), ("maximumMagicka", builder.Number(player.Definition.MaximumMagicka)),
            ("experience", builder.Number(player.Experience)));
        var encounter = EncounterService.ActiveEncounter(state);
        var activeEncounter = encounter is null ? builder.Null() : builder.Object(("name", builder.String(encounter.Name)), ("objective", builder.String(encounter.Objective)));
        var root = builder.Object(("player", playerValue), ("activeEncounter", activeEncounter), ("lastOutcome", builder.String(state.LastOutcome)));
        _engine.Ui.PublishProjection(_hud, ++_sequence, builder.Build(root));

        var facts = state.Actors.Values.Where(actor => !actor.IsDead && _sprites.ContainsKey(actor.EntityId)).Select(actor => new NativeAppearanceFact
        {
            object_id = checked((ulong)actor.EntityId),
            transform = new NativeTransform { translation = actor.Position.ToNative(), rotation = new NativeQuat { w = 1 }, scale = new NativeVec3 { x = 1, y = 1, z = 1 } },
            appearance = _sprites[actor.EntityId], visible = 1,
        }).ToArray();
        _engine.Appearance.PublishSnapshot(facts);
    }

    private NativeAppearanceHandle CreateSprite(AuthoredSprite sprite)
    {
        var texture = _engine.Appearance.OpenResource(sprite.TexturePath);
        return _engine.Appearance.CreateSprite(new NativeSpriteAppearanceRequest
        {
            texture = texture.handle, uv_min = sprite.UvMin, uv_max = sprite.UvMax, pivot = sprite.Pivot, size = sprite.Size,
            billboard = sprite.BillboardMode, tint = new NativeColor { r = 1, g = 1, b = 1, a = 1 },
        });
    }
}
