using System.Numerics;
using Rusty.Engine;

namespace RustyDagger.Game;

/// <summary>Owns the HUD contract and Dagger's retained world appearance facts.</summary>
public sealed class DaggerPresentation
{
    private readonly IEngineContext _engine;
    private readonly UiStreamHandle _hud;
    private readonly AppearanceHandle? _world;
    private readonly Dictionary<long, AppearanceHandle> _sprites = [];
    private ulong _sequence;

    public DaggerPresentation(IEngineContext engine, PrivateersHoldInputs content)
    {
        _engine = engine;
        _hud = engine.Ui.OpenStream(new UiStreamRequest("dagger.hud", "dagger.ui.snapshot.v1"));
        if (content.StaticMeshContentPath is { } meshPath)
            _world = engine.Appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(
                meshPath,
                new Color(.72f, .7f, .65f, 1f)));
        foreach (var actor in content.Project.Actors.Values.Where(actor => actor.Sprite is not null))
            _sprites[actor.EntityId] = CreateSprite(actor.Sprite!);
    }

    public void Publish(DaggerGameState state)
    {
        var player = state.Player;
        var builder = new UiValueBuilder();
        var playerValue = builder.Object(
            ("health", builder.Number(player.Health)), ("maximumHealth", builder.Number(player.Definition.MaximumHealth)),
            ("stamina", builder.Number(player.Stamina)), ("maximumStamina", builder.Number(player.Definition.MaximumStamina)),
            ("magicka", builder.Number(player.Magicka)), ("maximumMagicka", builder.Number(player.Definition.MaximumMagicka)),
            ("experience", builder.Number(player.Experience)));
        var encounter = EncounterService.ActiveEncounter(state);
        var activeEncounter = encounter is null ? builder.Null() : builder.Object(("name", builder.String(encounter.Name)), ("objective", builder.String(encounter.Objective)));
        var root = builder.Object(("player", playerValue), ("activeEncounter", activeEncounter), ("lastOutcome", builder.String(state.LastOutcome)));
        _engine.Ui.PublishProjection(new UiProjection(_hud, ++_sequence, builder.Build(root)));

        var facts = new List<AppearanceFact>();
        if (_world is { } world)
            facts.Add(new AppearanceFact(1, IdentityTransform(), world, Visible: 1, Reserved: 0));
        facts.AddRange(state.Actors.Values
            .Where(actor => !actor.IsDead && _sprites.ContainsKey(actor.EntityId))
            .Select(actor => new AppearanceFact(
                checked((ulong)actor.EntityId),
                new Transform(actor.Position.ToVector(), Quaternion.Identity, Vector3.One),
                _sprites[actor.EntityId],
                Visible: 1,
                Reserved: 0)));
        _engine.Appearance.PublishSnapshot([.. facts]);
    }

    private AppearanceHandle CreateSprite(AuthoredSprite sprite)
    {
        var texture = _engine.Appearance.OpenResource(new RenderResourceRequest(sprite.TexturePath));
        return _engine.Appearance.CreateSprite(new SpriteAppearanceRequest(
            texture.Handle,
            sprite.UvMin,
            sprite.UvMax,
            sprite.Pivot,
            sprite.Size,
            sprite.BillboardMode,
            RenderOrder: 0,
            new Color(1, 1, 1, 1)));
    }

    private static Transform IdentityTransform() => new(Vector3.Zero, Quaternion.Identity, Vector3.One);
}
