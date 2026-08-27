using System.Numerics;
using Rusty.Engine;
using RustyDagger.Game.Content;
using RustyDagger.Game.Modules.Actors;

namespace RustyDagger.Game.Daggerfall.Presentation;

/// <summary>Privateer's Hold's mesh and sprite choices, published as renderer-neutral Engine appearance facts.</summary>
internal sealed class PrivateersHoldAppearance
{
    private readonly IAppearanceService _appearance;
    private readonly AppearanceHandle? _world;
    private readonly Dictionary<long, AppearanceHandle> _sprites = [];

    internal PrivateersHoldAppearance(IAppearanceService appearance, PrivateersHoldInputs content)
    {
        _appearance = appearance;
        if (content.StaticMeshContentPath is { } meshPath)
            _world = appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(meshPath, new Color(.72f, .7f, .65f, 1f)));
        foreach (AuthoredActor actor in content.Project.Actors.Values.Where(actor => actor.Sprite is not null))
            _sprites[actor.EntityId] = CreateSprite(actor.Sprite!);
    }

    internal void Publish(ActorsState actors)
    {
        List<AppearanceFact> facts = [];
        if (_world is { } world) facts.Add(new AppearanceFact(1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), world, Visible: 1, Reserved: 0));
        facts.AddRange(actors.All.Values.Where(actor => !actor.IsDefeated && _sprites.ContainsKey(actor.EntityId)).Select(actor => new AppearanceFact(checked((ulong)actor.EntityId), new Transform(actor.Position.ToVector(), Quaternion.Identity, Vector3.One), _sprites[actor.EntityId], Visible: 1, Reserved: 0)));
        _appearance.PublishSnapshot([.. facts]);
    }

    private AppearanceHandle CreateSprite(AuthoredSprite sprite)
    {
        RenderResourceInfo texture = _appearance.OpenResource(new RenderResourceRequest(sprite.TexturePath));
        return _appearance.CreateSprite(new SpriteAppearanceRequest(texture.Handle, sprite.UvMin, sprite.UvMax, sprite.Pivot, sprite.Size, sprite.BillboardMode, RenderOrder: 0, new Color(1, 1, 1, 1)));
    }
}
