using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A remodelled ARCH3D model in a site: every placement of it, a moving model of it and a city gate state of it draws its
/// remodel at the classic's pose while refreshed art is drawn and the classic otherwise, switched live; the remodel's classic
/// texture slots draw the location's own materials for them, and a texture the pack ships draws a material of its own.
/// </summary>
public sealed class ModelRemodelPresentationTests
{
    private static readonly Transform Trough = new(new Vector3(4, 0, 6), Quaternion.Identity, Vector3.One);
    private static readonly Transform OtherTrough = new(new Vector3(9, 0, 2), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1F), Vector3.One);
    private static readonly Transform Bench = new(new Vector3(1, 0, 1), Quaternion.Identity, Vector3.One);

    private static readonly DaggerfallDungeonActionModelDefinition Lever = new("action/lever", null, null, "lever", 0, 0,
        new DaggerfallDoorVisual("mesh/61027.rstatmsh", Hash, [new DaggerfallMeshMaterialBinding(0, 0)]),
        new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), -Vector3.One, Vector3.One, [], []) { ModelId = "61027" };

    private static readonly Transform GatePose = new(new Vector3(30, 0, -12), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2), Vector3.One);

    private static DaggerfallCityGateState GateState(string model) =>
        new(new DaggerfallDoorVisual($"mesh/{model}.rstatmsh", Hash, [new DaggerfallMeshMaterialBinding(0, 0)]), -Vector3.One, Vector3.One,
            [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [new Triangle(0, 1, 2)]) { ModelId = model };

    private static readonly DaggerfallCityGateDefinition Gate = new("gate/wallaa11-rmb/0", GatePose, GateState("446"), GateState("447"));

    private static DaggerfallSiteProfile Profile()
    {
        DaggerfallSiteGeometry geometry = new(1, [new DaggerfallSiteSpatialPart("block", "spatial/block.rspatial", Hash)],
        [
            new DaggerfallSiteMesh("mesh/41209.rstatmsh", Hash, Trough, [new(0, 0), new(1, 1)]) { ModelId = "41209" },
            new DaggerfallSiteMesh("mesh/41209.rstatmsh", Hash, OtherTrough, [new(0, 0), new(1, 1)]) { ModelId = "41209" },
            new DaggerfallSiteMesh("mesh/41106.rstatmsh", Hash, Bench, [new(0, 0)]) { ModelId = "41106" },
        ])
        {
            Remodels = new Dictionary<string, DaggerfallSiteRemodel>(StringComparer.Ordinal)
            {
                ["41209"] = new("remodel/41209.rstatmsh", Hash, [new(0, 1), new(1, 0)],
                    [new DaggerfallRemodelTexture(2, "remodel/41209-0.png", Hash, false), new DaggerfallRemodelTexture(3, "remodel/41209-1.png", Hash, true)]),
                ["61027"] = new("remodel/61027.rstatmsh", Hash, [new(0, 0)], []),
                ["446"] = new("remodel/446.rstatmsh", Hash, [new(0, 0)], []),
                ["447"] = new("remodel/447.rstatmsh", Hash, [new(0, 0)], []),
            },
        };
        return new DaggerfallSiteProfile(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()), geometry,
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0),
            [new NormalizedMaterial(0, "texture/67-0.png", Hash, "material/texture-67-0"), new NormalizedMaterial(1, "texture/85-0.png", Hash, "material/texture-85-0")],
            new Dictionary<long, NormalizedActorSprite>(), profileKind: DaggerfallWorldProfileKind.Dungeon) { CityGates = [Gate] };
    }

    private static (DaggerfallSiteAppearance Presentation, AppearanceFake Graphics) Compose(Func<bool> refreshed, Func<Transform> leverPose,
        Func<bool>? gatesOpen = null)
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        foreach (string path in new[] { "mesh/41209.rstatmsh", "mesh/41106.rstatmsh", "mesh/61027.rstatmsh", "remodel/41209.rstatmsh", "remodel/61027.rstatmsh",
                     "remodel/41209-0.png", "remodel/41209-1.png", "texture/67-0.png", "texture/85-0.png",
                     "mesh/446.rstatmsh", "mesh/447.rstatmsh", "remodel/446.rstatmsh", "remodel/447.rstatmsh" })
            content.Add(path, Hash);
        AppearanceFake graphics = new(releases);
        DaggerfallLocationParts parts = new(() => 0, () => [], gatesOpen ?? (() => false), () => [(Gate, new EntityId(7), Gate.Open.Visual, GatePose)],
            () => [(Lever, new EntityId(5))], (string action, out Transform pose) => { pose = leverPose(); return action == Lever.ActionId; });
        DaggerfallSiteAppearance presentation = new(content, graphics, Profile(), parts: parts);
        presentation.UseEffectArt(PresentationRecorder.Create().Service, AnimationRecorder.Create().Service,
            DaggerfallArtReplacements.Read("""{"artReplacements": {"images": []}}"""u8.ToArray(), "test.art"), refreshed);
        return (presentation, graphics);
    }

    [Fact]
    public void Every_placement_of_a_remodelled_model_draws_its_remodel_in_refreshed_art_and_the_classic_once_classic_art_is_chosen()
    {
        bool refreshed = true;
        var (presentation, graphics) = Compose(() => refreshed, () => new(Vector3.Zero, Quaternion.Identity, Vector3.One));
        using DaggerfallSiteAppearance owned = presentation;
        using var actors = EmptyActors();
        Appearance classic = graphics.StaticMeshByPath["mesh/41209.rstatmsh"], remodel = graphics.StaticMeshByPath["remodel/41209.rstatmsh"];
        Appearance bench = graphics.StaticMeshByPath["mesh/41106.rstatmsh"];
        // One remodel serves every placement, created once beside the classic.
        Assert.Single(graphics.StaticMeshPaths, path => path == "remodel/41209.rstatmsh");

        presentation.Publish(actors);
        AppearanceFact[] drawn = graphics.Snapshots[^1];
        Assert.Equal([Trough.Translation, OtherTrough.Translation], drawn.Where(fact => fact.Appearance == remodel).Select(fact => fact.Transform.Translation));
        Assert.Equal(OtherTrough.Rotation, Assert.Single(drawn, fact => fact.Transform.Translation == OtherTrough.Translation).Transform.Rotation);
        Assert.DoesNotContain(drawn, fact => fact.Appearance == classic);
        // A model the pack does not remodel stays classic.
        Assert.Single(drawn, fact => fact.Appearance == bench);

        refreshed = false;
        presentation.Publish(actors);
        drawn = graphics.Snapshots[^1];
        Assert.Equal(2, drawn.Count(fact => fact.Appearance == classic));
        Assert.DoesNotContain(drawn, fact => fact.Appearance == remodel);

        refreshed = true;
        presentation.Publish(actors);
        Assert.Equal(2, graphics.Snapshots[^1].Count(fact => fact.Appearance == remodel));
    }

    [Fact]
    public void A_remodel_draws_the_locations_materials_at_its_classic_slots_and_its_own_textures_opaque_or_cut_out_at_the_rest()
    {
        var (presentation, graphics) = Compose(() => true, () => new(Vector3.Zero, Quaternion.Identity, Vector3.One));
        using DaggerfallSiteAppearance owned = presentation;
        // The classic binds profile slot 0 at its slot 0 and slot 1 at its slot 1; the remodel the other way round.
        MeshMaterialBinding[] classic = graphics.StaticMeshMaterials[graphics.StaticMeshByPath["mesh/41209.rstatmsh"]];
        MeshMaterialBinding[] remodel = graphics.StaticMeshMaterials[graphics.StaticMeshByPath["remodel/41209.rstatmsh"]];
        Assert.Equal(classic[1].Material, Assert.Single(remodel, binding => binding.MaterialSlot == 0).Material);
        Assert.Equal(classic[0].Material, Assert.Single(remodel, binding => binding.MaterialSlot == 1).Material);
        Material own = Assert.Single(remodel, binding => binding.MaterialSlot == 2).Material;
        Assert.DoesNotContain(own, classic.Select(binding => binding.Material));
        Assert.Contains("remodel/41209-0.png", graphics.OpenedResourcePaths);
        Assert.Equal(MaterialAlphaMode.Opaque, graphics.MaterialRequests[own].AlphaMode);
        // The cut-out layer is drawn where its alpha is at least half, as the ground cover and flora are.
        MaterialRequest cutout = graphics.MaterialRequests[Assert.Single(remodel, binding => binding.MaterialSlot == 3).Material];
        Assert.Equal((MaterialAlphaMode.Mask, .5F), (cutout.AlphaMode, cutout.AlphaCutoff));
    }

    [Fact]
    public void A_moving_model_draws_its_remodel_where_its_motion_stands_it()
    {
        bool refreshed = true;
        Transform pose = new(new Vector3(2, 1, 3), Quaternion.Identity, Vector3.One);
        var (presentation, graphics) = Compose(() => refreshed, () => pose);
        using DaggerfallSiteAppearance owned = presentation;
        using var actors = EmptyActors();
        Appearance classic = graphics.StaticMeshByPath["mesh/61027.rstatmsh"], remodel = graphics.StaticMeshByPath["remodel/61027.rstatmsh"];

        presentation.Publish(actors);
        Assert.Equal(pose.Translation, Assert.Single(graphics.Snapshots[^1], fact => fact.Appearance == remodel).Transform.Translation);
        Assert.DoesNotContain(graphics.Snapshots[^1], fact => fact.Appearance == classic);

        // It moves, and the remodel goes with it under the same identity.
        var identity = Assert.Single(graphics.Snapshots[^1], fact => fact.Appearance == remodel).ObjectId;
        pose = new(new Vector3(2, 2.5F, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitX, .5F), Vector3.One);
        presentation.Publish(actors);
        AppearanceFact moved = Assert.Single(graphics.Snapshots[^1], fact => fact.Appearance == remodel);
        Assert.Equal(pose.Translation, moved.Transform.Translation);
        Assert.Equal(pose.Rotation, moved.Transform.Rotation);

        refreshed = false;
        presentation.Publish(actors);
        AppearanceFact still = Assert.Single(graphics.Snapshots[^1], fact => fact.Appearance == classic);
        Assert.Equal(identity, still.ObjectId);
        Assert.Equal(pose.Translation, still.Transform.Translation);
    }

    [Fact]
    public void A_city_gate_draws_the_remodel_of_its_open_or_closed_model_at_its_pose_while_refreshed_art_is_drawn()
    {
        bool refreshed = true, open = true;
        var (presentation, graphics) = Compose(() => refreshed, () => new(Vector3.Zero, Quaternion.Identity, Vector3.One), () => open);
        using DaggerfallSiteAppearance owned = presentation;
        using var actors = EmptyActors();
        Appearance[] classic = [graphics.StaticMeshByPath["mesh/446.rstatmsh"], graphics.StaticMeshByPath["mesh/447.rstatmsh"]];
        Appearance[] remodel = [graphics.StaticMeshByPath["remodel/446.rstatmsh"], graphics.StaticMeshByPath["remodel/447.rstatmsh"]];
        AppearanceFact Gate() => Assert.Single(graphics.Snapshots[^1], fact => classic.Contains(fact.Appearance) || remodel.Contains(fact.Appearance));

        presentation.Publish(actors);
        AppearanceFact drawn = Gate();
        Assert.Equal(remodel[0], drawn.Appearance);
        Assert.Equal((GatePose.Translation, GatePose.Rotation), (drawn.Transform.Translation, drawn.Transform.Rotation));
        var identity = drawn.ObjectId;

        // It closes at dusk and draws the closed model's remodel under the same identity.
        open = false;
        presentation.Publish(actors);
        Assert.Equal((remodel[1], identity), (Gate().Appearance, Gate().ObjectId));

        refreshed = false;
        presentation.Publish(actors);
        Assert.Equal((classic[1], identity), (Gate().Appearance, Gate().ObjectId));
        open = true;
        presentation.Publish(actors);
        Assert.Equal(classic[0], Gate().Appearance);
    }
}
