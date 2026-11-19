using System.Numerics;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The developer combat start begins a quick-started warrior in Privateer's Hold with the career's weapon drawn, and
/// brings an enemy warrior and barbarian in close around them at once; a save made there loads like any other.
/// </summary>
public sealed class CombatStartBundleTests
{
    private const string Bundle = "daggerfall.combat-start";

    [Fact]
    public void Begin_alone_starts_an_armed_warrior_in_privateers_hold_with_two_hostile_humans_close_by_and_a_save_keeps_them()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile dungeon = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, dungeon);
        SpatialFake spatial = SpatialFake.Create(dungeon.Geometry.Spatial[0].Sha256, releases);
        spatial.KeepPosition = true;
        // Floor a metre below wherever a straight-down look asks, and nothing else in the way.
        spatial.FloorHit = request => request.Direction.Y < -.99f
            ? default(SpatialHit) with { Present = true, Converged = true, Point = request.Origin - Vector3.UnitY, Normal = Vector3.UnitY }
            : default;
        // A body overlaps another actor's clearance box, so each foe is placed clear of the one before it.
        spatial.OverlapHit = request => new SpatialHit
        {
            Present = request.Entities.ToArray().Any(collider => request.Center.X >= collider.Min.X && request.Center.X <= collider.Max.X
                && request.Center.Z >= collider.Min.Z && request.Center.Z <= collider.Max.Z),
        };
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        TestDaggerfallRuleset ruleset = new(videosEnabled: true);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId(Bundle));
        product.Start();
        Assert.Equal(Bundle, engine.PublishedNested("bundles", "current"));

        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"begin"}""")]));
        FinishLoading(product, 1);

        Assert.Equal(ProductMode.Playing, product.Mode);
        DaggerfallSession game = RunningSession(product);
        Assert.Equal(DaggerfallWorldProfileIds.Dungeon(PrivateersHold), game.Sites.ActiveProfile);
        Assert.Empty(game.State.Quests.All);
        // A warrior made by the ordinary creation rules, with the warrior's loadout broadsword in the main hand, drawn.
        Assert.Equal("class16", game.State.Character.Capture().CareerId);
        Assert.Equal("template-118-iron", Wielded(game));
        Assert.True(game.State.WeaponDrawn);

        for (ulong step = 2; step < 5; step++) product.Update(new ProductUpdate(OuterUpdate(step), []));
        Vector3 player = game.State.PlayerControl.Position!.Value.ToVector();
        // The start's foes are the last actors brought in, after the hold's own population.
        Dictionary<string, long> foes = game.DynamicActors.OrderBy(actor => actor.Key).TakeLast(2).ToDictionary(actor => actor.Value.Value, actor => actor.Key);
        Assert.Equal(["encounter-barbarian", "encounter-warrior"], foes.Keys.Order());
        Assert.True(Vector3.Distance(game.State.Actors.Get(foes["encounter-warrior"]).Position.ToVector(),
            game.State.Actors.Get(foes["encounter-barbarian"]).Position.ToVector()) > 1f, "The foes stand apart.");
        foreach (long foe in foes.Values)
        {
            Vector3 at = game.State.Actors.Get(foe).Position.ToVector();
            Assert.InRange(new Vector2(at.X - player.X, at.Z - player.Z).Length(), DaggerfallNearbySpawn.MinimumMetres - .01f, 5.01f);
            DaggerfallEnemyPerceptionMemory senses = game.State.Actors.Get(foe).Actor.Get<DaggerfallEnemyPerceptionMemory>();
            Assert.True(senses.ForcedHostile);
            // Provoked, so they come for the player at once rather than waiting to see them in the dark.
            Assert.True(senses.HasEncounteredPlayer);
        }

        // A save made here loads like any other: the same warrior, armed, and the same foes where they stood.
        ResolvedGameComposition composition = Resolve(FullContent(root), new GameBundleId(Bundle)).RequireComposition();
        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, dungeon);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, SpatialFake.Create(dungeon.Geometry.Spatial[0].Sha256, releases).Service,
            new AppearanceFake(releases));
        using IGameSession resumed = ruleset.CreateSession(new GameSessionContext(resumedEngine.Context, composition), game.CaptureSave());
        DaggerfallSession restored = Assert.IsType<DaggerfallSession>(resumed);
        Assert.Equal("class16", restored.State.Character.Capture().CareerId);
        Assert.Equal("template-118-iron", Wielded(restored));
        foreach ((string actor, long id) in foes)
        {
            Assert.Equal(actor, restored.DynamicActors[id].Value);
            Assert.Equal(game.State.Actors.Get(id).Position, restored.State.Actors.Get(id).Position);
            Assert.True(restored.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        }
    }

    /// <summary>The item definition the player holds in the main hand.</summary>
    private static string Wielded(DaggerfallSession game)
    {
        var hand = Assert.Single(game.State.Equipment.Read().Assignments, assignment => assignment.Slot.Value == "right-hand");
        return Assert.Single(game.State.Inventory.Read().UniqueItems, item => item.Entity.Value == hand.Item.EntityId).Definition.Value;
    }
}
