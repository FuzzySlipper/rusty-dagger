using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11224: both factory paths draw an actor in the look content gives it, and only the documented fallback picks a
/// look from what kind of actor it is.
/// </summary>
public sealed class ActorLookFactoryTests
{
    private static readonly DaggerfallDefinitions Definitions = TestPayload.Definitions;

    [Theory]
    [InlineData("Male", "male", "npc-470")]
    [InlineData("Female", "female", "npc-469")]
    public void A_registry_person_from_a_spec_with_a_look_draws_in_it_as_its_gender(string gender, string spriteKey, string character)
    {
        ActorVisualSpec visual = Materialize(Npc(DaggerfallNpcKind.Static, gender), "hire-orc-sellsword");
        Assert.Equal((ActorVisualKind.DirectionalMobile, (int?)143, spriteKey, character),
            (visual.Kind, visual.MobileId, visual.Gender, visual.CrowdCharacterHint));
    }

    [Fact]
    public void Without_a_look_a_townsperson_walks_as_its_splat_and_anyone_else_stands_as_its_billboard()
    {
        ActorVisualSpec walker = Materialize(Npc(DaggerfallNpcKind.Civilian, "Female"), spawnerId: null);
        Assert.Equal((ActorVisualKind.SplatCharacter, (int?)210, (int?)4), (walker.Kind, walker.Archive, walker.Record));
        ActorVisualSpec guard = Materialize(Npc(DaggerfallNpcKind.Static, "Male"), "hire-road-guard");
        Assert.Equal((ActorVisualKind.Billboard, (int?)210, (int?)4, (string?)null), (guard.Kind, guard.Archive, guard.Record, guard.CrowdCharacterHint));
    }

    [Fact]
    public void A_look_overrides_the_fallback_whatever_kind_of_person_wears_it()
    {
        DaggerfallNpc walker = Npc(DaggerfallNpcKind.Civilian, "Male");
        Assert.Equal(ActorVisualKind.Billboard, DaggerfallActorFactory.NpcVisual(walker, Look(DaggerfallActorLookKind.Billboard), "male", null).Kind);
        DaggerfallNpc standing = Npc(DaggerfallNpcKind.Static, "Male");
        ActorVisualSpec townsperson = DaggerfallActorFactory.NpcVisual(standing,
            Look(DaggerfallActorLookKind.Townsperson, splats: new() { [ActorGender.Male] = "383-A" }), "male", null);
        Assert.Equal((ActorVisualKind.SplatCharacter, "383-A", (int?)210), (townsperson.Kind, townsperson.CrowdCharacterHint, townsperson.Archive));
        // A townsperson look naming no character keeps a cast individual's own asset as the one it draws as.
        Assert.Equal("npc-022", DaggerfallActorFactory.NpcVisual(standing, Look(DaggerfallActorLookKind.Townsperson), "male", "npc-022").CrowdCharacterHint);
    }

    [Fact]
    public void A_definition_actor_draws_in_its_look_and_takes_its_gendered_splat_when_its_gender_resolves()
    {
        DaggerfallActorDefinition hire = Definitions.RequireActor(new DaggerfallActorId("hire-orc-sellsword"));
        using ActorsState actors = new();
        DaggerfallMechanicsState mechanics = new();
        ActorState actor = DaggerfallActorFactory.CreateNonPlayerActor(actors, 4_000_101, hire,
            mechanics.CreateStats(hire, new DaggerfallVitalValues(30, 0, 0)), new ActorPose(new WorldPoint(0f, 0f, 0f), 0f));
        ActorRenderComponent render = actor.Actor.Get<ActorRenderComponent>();
        Assert.Equal((ActorVisualKind.DirectionalMobile, (int?)143, (string?)null), (render.Spec.Kind, render.Spec.MobileId, render.Spec.CrowdCharacterHint));

        Color livery = new(0.5F, 0.25F, 0.25F, 1F);
        render.SetVisual(render.Spec with { Tint = livery, LiveryKey = "test" });
        DaggerfallActorFactory.SetGender(actor.Actor, hire, "female");
        Assert.Equal("female", actor.Actor.Get<DaggerfallActorGender>().Value);
        Assert.Equal(("female", "npc-469", (Color?)livery, "test"), (render.Spec.Gender, render.Spec.CrowdCharacterHint, render.Spec.Tint, render.Spec.LiveryKey));

        // A bound enemy without a look draws as its own classic mobile, as the fallback always did.
        DaggerfallActorDefinition enemy = Definitions.Actors.Values.First(definition => definition.MobileId is not null && definition.Look is null);
        Assert.Equal((ActorVisualKind.DirectionalMobile, enemy.MobileId), (DaggerfallActorFactory.DefinitionVisual(enemy).Kind, DaggerfallActorFactory.DefinitionVisual(enemy).MobileId));
    }

    private static ActorVisualSpec Materialize(DaggerfallNpc npc, string? spawnerId)
    {
        using ActorsState actors = new();
        ArchetypeProfile profile = DaggerfallActorFactory.ResolveNpcArchetype(Definitions, npc, spawnerId: spawnerId);
        ActorState actor = DaggerfallActorFactory.CreateCivilianActor(new DaggerfallMechanicsState(), actors, new InventoryStore(), npc,
            new ActorPose(new WorldPoint(0f, 0f, 0f), 0f), profile, Definitions, out _, spawnerId);
        return actor.Actor.Get<ActorRenderComponent>().Spec;
    }

    private static DaggerfallActorLook Look(DaggerfallActorLookKind kind, int? mobile = null, Dictionary<ActorGender, string>? splats = null) =>
        new(kind, mobile, splats ?? []);

    private static DaggerfallNpc Npc(DaggerfallNpcKind kind, string gender) => new(
        4_000_100,
        kind,
        "test/look",
        new DaggerfallNpcSite(17, "Daggerfall", string.Empty),
        new DaggerfallNpcAppearance("Breton", gender, 210, 4, 3, 0),
        "person",
        ["talk"],
        DaggerfallNpcPresence.Active,
        null, null, null);
}
