using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Kit.Actors;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Privateer's Hold's mesh and sprite choices, published as renderer-neutral Engine appearance facts.</summary>
internal sealed class PrivateersHoldAppearance : IDisposable
{
    private readonly IAppearanceService _appearance;
    private Appearance? _world;
    private readonly Dictionary<long, Appearance> _sprites = [];
    private bool _disposed;

    internal PrivateersHoldAppearance(IAppearanceService appearance, PrivateersHoldInputs content)
    {
        _appearance = appearance;
        try
        {
            if (content.StaticMeshContentPath is { } meshPath)
                _world = appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(meshPath, new Color(.72f, .7f, .65f, 1f)));
            foreach (AuthoredActor actor in content.Project.Actors.Values.Where(actor => actor.Sprite is not null))
                _sprites[actor.EntityId] = CreateSprite(actor.Sprite!);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Publish(ActorsState actors)
    {
        List<AppearanceFact> facts = [];
        if (_disposed) return;
        if (_world is { } world) facts.Add(new AppearanceFact(1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), world, Visible: true, RenderLayer.Scene));
        facts.AddRange(actors.All.Values.Where(actor => !actor.IsDefeated && _sprites.ContainsKey(actor.EntityId)).Select(actor => new AppearanceFact(checked((ulong)actor.EntityId), new Transform(actor.Position.ToVector(), Quaternion.Identity, Vector3.One), _sprites[actor.EntityId], Visible: true, RenderLayer.Scene)));
        _appearance.PublishSnapshot([.. facts]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        try { _appearance.PublishSnapshot(ReadOnlySpan<AppearanceFact>.Empty); }
        catch (Exception exception) { failures = [exception]; }
        foreach (Appearance sprite in _sprites.Values)
        {
            try { sprite.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        _sprites.Clear();
        if (_world is { } world)
        {
            _world = null;
            try { world.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private Appearance CreateSprite(AuthoredSprite sprite)
    {
        RenderResourceInfo texture = _appearance.OpenResource(new RenderResourceRequest(sprite.TexturePath));
        return _appearance.CreateSprite(new SpriteAppearanceRequest(texture.Handle, sprite.UvMin, sprite.UvMax, sprite.Pivot, sprite.Size, (BillboardMode)sprite.BillboardMode, SpriteSizeMode.World, RenderOrder: 0, SpriteDepthPolicy.Default, new Color(1, 1, 1, 1)));
    }
}
