using System.Numerics;
using Rusty.Engine;
using RustyDagger.Game.Content;
using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.Combat;
using RustyDagger.Game.Modules.Encounters;

namespace RustyDagger.Game.Modules.Presentation;

/// <summary>Owns the HUD contract and Dagger's retained world appearance facts.</summary>
internal sealed class DaggerPresentation
{
    private readonly IUiService _ui;
    private readonly IAppearanceService _appearance;
    private readonly UiStreamHandle _hud;
    private readonly AppearanceHandle? _world;
    private readonly Dictionary<long, AppearanceHandle> _sprites = [];
    private ulong _sequence;

    internal DaggerPresentation(IUiService ui, IAppearanceService appearance, PrivateersHoldInputs content)
    {
        _ui = ui;
        _appearance = appearance;
        _hud = ui.OpenStream(new UiStreamRequest("dagger.hud", "dagger.ui.snapshot.v1"));
        if (content.StaticMeshContentPath is { } meshPath)
            _world = appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(
                meshPath,
                new Color(.72f, .7f, .65f, 1f)));
        foreach (var actor in content.Project.Actors.Values.Where(actor => actor.Sprite is not null))
            _sprites[actor.EntityId] = CreateSprite(actor.Sprite!);
    }

    internal void Publish(DaggerfallState state, PresentationState presentation)
    {
        PlayerActorState player = state.Actors.Player;
        var builder = new UiValueBuilder();
        var playerValue = builder.Object(
            ("health", builder.Number(player.Health)), ("maximumHealth", builder.Number(player.Definition.MaximumHealth)),
            ("stamina", builder.Number(player.Stamina)), ("maximumStamina", builder.Number(player.Definition.MaximumStamina)),
            ("magicka", builder.Number(player.Magicka)), ("maximumMagicka", builder.Number(player.Definition.MaximumMagicka)),
            ("experience", builder.Number(state.Progression.Experience)));
        var encounter = EncounterSystem.ActiveEncounter(state);
        var activeEncounter = encounter is null ? builder.Null() : builder.Object(("name", builder.String(encounter.Name)), ("objective", builder.String(encounter.Objective)));
        var root = builder.Object(("player", playerValue), ("activeEncounter", activeEncounter), ("lastOutcome", builder.String(presentation.LastOutcome)));
        _ui.PublishProjection(new UiProjection(_hud, ++_sequence, builder.Build(root)));

        var facts = new List<AppearanceFact>();
        if (_world is { } world)
            facts.Add(new AppearanceFact(1, IdentityTransform(), world, Visible: 1, Reserved: 0));
        facts.AddRange(state.Actors.All.Values
            .Where(actor => !actor.IsDead && _sprites.ContainsKey(actor.EntityId))
            .Select(actor => new AppearanceFact(
                checked((ulong)actor.EntityId),
                new Transform(actor.Position.ToVector(), Quaternion.Identity, Vector3.One),
                _sprites[actor.EntityId],
                Visible: 1,
                Reserved: 0)));
        _appearance.PublishSnapshot([.. facts]);
    }

    private AppearanceHandle CreateSprite(AuthoredSprite sprite)
    {
        var texture = _appearance.OpenResource(new RenderResourceRequest(sprite.TexturePath));
        return _appearance.CreateSprite(new SpriteAppearanceRequest(
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
