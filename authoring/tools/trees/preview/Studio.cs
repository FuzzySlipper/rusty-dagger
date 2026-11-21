using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace FloraPreview;

/// <summary>One 16:9 orthographic frame. Each quadrant has the same scale and ground line.</summary>
public sealed class Studio : IEngineProduct, IDebugCommandModuleSource, IDebugCommandModule
{
    private readonly IEngineContext engine;
    private readonly Camera camera;
    private readonly Light sun, fill;
    private readonly RenderResource background;
    private readonly DaggerfallExteriorTerrainAppearance terrain;
    private readonly DaggerfallFloraArt art;
    private DaggerfallFloraAsset? sprite, mesh;
    private VegetationField? field;
    private readonly List<VegetationRecord> copies = [];

    public Studio(ProductCreateContext context)
    {
        engine = context.Engine;
        using JsonDocument tuning = JsonDocument.Parse(context.Content.ReadFile("flats-tuning.json").Bytes);
        var flats = DaggerfallFlatsTuning.Read(tuning.RootElement);
        terrain = new(engine.Content, engine.Graphics, flats: flats);
        terrain.UseArt(DaggerfallArtReplacements.Read(context.Content.ReadFile("flora.json").Bytes, "daggerfall.art-replacements"), () => true);
        art = new(engine, flats);
        camera = engine.CameraView.CreateCamera(CameraFor(10));
        engine.CameraView.SetActiveCamera(camera);
        sun = Light(1, LightKind.Directional, 2.4f, Vector3.Normalize(new Vector3(.5f, -.8f, -.6f)));
        fill = Light(2, LightKind.Ambient, .7f, Vector3.UnitY);
        background = engine.Graphics.OpenResource(new("background.png", TextureFilter.Nearest, TextureWrap.Clamp)).Handle;
        engine.CameraView.SetSkyBackground(background);
        engine.CameraView.SetToneMapping(new(ToneMappingOperator.Rolloff, 1));
    }

    private Light Light(ulong id, LightKind kind, float power, Vector3 direction) =>
        engine.Graphics.CreateLight(new(id, false, 0, new(kind, Vector3.One, power, true, Vector3.Zero, direction, false, 0, 0, 0, 0, LightShadowIntent.Disabled)));

    private static CameraDescriptor CameraFor(float height) => new(
        new CameraPose(new(0, 0, 100), 0, 0), CameraBasisMode.Explicit, new(-Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
        new(CameraProjectionKind.Orthographic, 0, height, .01, 500), new(0, 0, 1, 1));

    public void RegisterDebugCommands(IDebugCommandModuleRegistrar registrar) => registrar.Register(this);

    [DebugCommand("flora.draw")]
    public string Draw(int archive, int record, float tileHeight, float centreY)
    {
        var look = terrain.FloraLook(archive, record) ?? throw new ArgumentException($"No flora sprite for {archive}-{record}.");
        Clear();
        sprite = art.Sprite(look);
        if (look.Tree is { } tree) mesh = art.Tree(tree, look);
        engine.CameraView.UpdateCamera(new(camera, CameraFor(2 * tileHeight)));
        float x = tileHeight * 8 / 9, y = tileHeight / 2;
        // One tile large enough to contain every quadrant. Place copies explicitly so they
        // run through the same vegetation draw path as the forest, with no random scatter or LOD.
        float halfTile = Math.Max(256, tileHeight * 4);
        field = engine.Vegetation.Create(new VegetationFieldCreateRequest(default, 2 * halfTile, 1,
            VegetationFocus.At(halfTile, 0, halfTile)) with { Origin = new(-halfTile, 0, -halfTile) });
        void Layer(uint id, Appearance appearance, bool shadows) =>
            engine.Vegetation.SetLayer(new VegetationLayerRequest(field, id, appearance, default, 0, halfTile)
                with { CastsShadows = shadows, MaximumInstances = 4 });
        Layer(1, sprite.Look, false);
        VegetationRecord Tile(uint id, float left, float up, float yaw) =>
            new(id, new(halfTile + left, up - centreY, halfTile), float.DegreesToRadians(yaw), 1);
        copies.Clear();
        copies.Add(Tile(1, -x, y, 0));
        if (mesh is not null)
        {
            Layer(2, mesh.Look, true);
            copies.Add(Tile(2, x, y, 0));
            copies.Add(Tile(2, -x, -y, 90));
            copies.Add(Tile(2, x, -y, 225));
        }
        return $"{archive}-{record}";
    }

    // A winter record can have its sprite ready while the renderer is still compiling snow.
    [DebugCommand("flora.ready")]
    public string Ready()
    {
        foreach (var tile in engine.Vegetation.ReadRequests(new(field!, 16)).Span)
        {
            VegetationRecord[] records = tile.X == 0 && tile.Z == 0 ? [.. copies] : [];
            engine.Vegetation.CommitTile(new(field!, tile.X, tile.Z, tile.Revision, new float[tile.HeightSide * tile.HeightSide],
                ReadOnlyMemory<uint>.Empty, ReadOnlyMemory<float>.Empty, records));
        }
        var work = engine.Graphics.ReadResourcePreparation();
        if (work.FailedResources > 0) return "A renderer resource failed preparation.";
        return work.CompletedBatch >= work.LastBatch && work.PendingResources == 0 && engine.Vegetation.Read(field!).Instances == (ulong)copies.Count ? "ready" : "preparing";
    }

    private void Clear()
    {
        field?.Dispose();
        field = null;
        sprite?.Dispose();
        sprite = null;
        mesh?.Dispose();
        mesh = null;
    }

    public void Start() { }
    public ProductUpdateResult Update(ProductUpdate update) => ProductUpdateResult.None;
    public void Pause() { }
    public void Resume() { }
    public void Restart() { }
    public void Shutdown() { }
    public void Dispose()
    {
        Clear();
        art.ReleaseSnowShader();
        terrain.Dispose();
        engine.CameraView.ClearSkyBackground(default);
        background.Dispose();
        fill.Dispose();
        sun.Dispose();
        camera.Dispose();
    }
}
