using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallOutcomePresentationTests
{
    [Fact]
    public void Defeat_names_the_actor_by_its_display_name_without_formatting_an_identity_record()
    {
        DaggerfallActorDefinition rat = TestPayload.Definitions.RequireActor(new("rat"));
        PresentationState state = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcome = new(state, new Dictionary<long, DaggerfallActorDefinition> { [2000] = rat }, TestPayload.Definitions.Mobiles);

        outcome.React(new ActorDiedFact(2000, 1, DaggerfallDamageCause.PhysicalAttack, 10, 2, 1, 1));

        Assert.Equal($"Defeated Rat for 2 damage; gained {rat.Rewards.ExperienceReward} XP", state.LastOutcome);
    }

    [Fact]
    public void Combat_lines_name_an_encounter_by_its_display_name()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition nightblade = definitions.RequireActor(new("encounter-nightblade"));
        PresentationState state = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcome = new(state, new Dictionary<long, DaggerfallActorDefinition> { [2000] = nightblade },
            definitions.Mobiles, definitions.Text);

        outcome.React(new AttackHitFact(2000, DaggerfallActorIdentity.PlayerEntityId, 7, 7, 0, EnemyAttack: true, 1, 1));
        Assert.Equal("Nightblade hit you for 7 damage", state.LastOutcome);
        outcome.React(new AttackWhiffedFact(DaggerfallActorIdentity.PlayerEntityId, 2000, EnemyAttack: false, 1, 2));
        Assert.Equal("Your blow misses Nightblade", state.LastOutcome);
        outcome.React(new ActorDiedFact(2000, 1, DaggerfallDamageCause.PhysicalAttack, 10, 2, 1, 3));
        Assert.StartsWith("Defeated Nightblade for 2 damage", state.LastOutcome);
    }

    [Fact]
    public void Contact_outcomes_each_have_their_own_line_from_the_players_side()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        PresentationState state = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcome = new(state, new Dictionary<long, DaggerfallActorDefinition> { [2000] = definitions.RequireActor(new("rat")) },
            definitions.Mobiles, definitions.Text);
        const long player = DaggerfallActorIdentity.PlayerEntityId;

        // A swing at empty air is its own feedback and writes no line; one aimed at someone out of reach says so.
        outcome.React(new AttackWhiffedFact(player, null, EnemyAttack: false, 1, 1));
        Assert.Equal(string.Empty, state.LastOutcome);
        outcome.React(new AttackWhiffedFact(player, 2000, EnemyAttack: false, 1, 1));
        Assert.Equal("Your blow misses Rat", state.LastOutcome);
        outcome.React(new AttackWhiffedFact(2000, player, EnemyAttack: true, 1, 2));
        Assert.Equal("Rat's blow misses you", state.LastOutcome);
        outcome.React(new AttackNegatedFact(player, 2000, HarmNegation.Deflected, EnemyAttack: false, 1, 3));
        Assert.Equal("Rat deflects your blow", state.LastOutcome);
        outcome.React(new AttackNegatedFact(2000, player, HarmNegation.Deflected, EnemyAttack: true, 1, 4));
        Assert.Equal("You deflect Rat's blow", state.LastOutcome);
        outcome.React(new AttackHitFact(player, 2000, 6, 0, 0, EnemyAttack: false, 1, 5) { Mitigated = new(0, 6) });
        Assert.Equal("Rat's armor absorbs your blow", state.LastOutcome);
        outcome.React(new AttackHitFact(2000, player, 9, 4, 0, EnemyAttack: true, 1, 6) { Mitigated = new(2, 3) });
        Assert.Equal("Rat hit you for 4 damage; 2 deflected, 3 absorbed", state.LastOutcome);
        // A classic blow carries no mitigation and reads as it always has.
        outcome.React(new AttackHitFact(player, 2000, 7, 7, 0, EnemyAttack: false, 1, 7));
        Assert.Equal("Hit Rat for 7 damage", state.LastOutcome);
    }

    private static DaggerfallActorDefinition ArchetypeDef(DaggerfallDefinitions definitions, string archetypeId, string kind) =>
        definitions.People.Archetypes[archetypeId].ToActorDefinition(kind);

    [Fact]
    public void Named_person_hired_companion_and_convoy_guard_use_display_names_across_combat_log_lines()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition staticNpc = ArchetypeDef(definitions, DaggerfallArchetypeIds.StaticNpc, DaggerfallActorKinds.StaticNpc);
        DaggerfallActorDefinition civilian = ArchetypeDef(definitions, DaggerfallArchetypeIds.Civilian, DaggerfallActorKinds.Civilian);
        DaggerfallActorDefinition centaur = definitions.RequireActor(new("centaur"));
        DaggerfallActorDefinition hiredRoadGuardDef = staticNpc with { SpawnerId = DaggerfallPartyHireService.DefaultRoadGuardSpecId };

        DaggerfallNpcRegistry npcs = new();
        DaggerfallNpcSite charing = new(17, "Charing", string.Empty);
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, 7, 518);

        const long namedPersonId = 0x4000_0000_1001L;
        const long hiredGuardId = 0x4000_0000_1002L;
        const long convoyGuardId = 0x4000_0000_1003L;
        const long centaurId = 3000L;
        const long player = DaggerfallActorIdentity.PlayerEntityId;

        npcs.RegisterDeterministic(namedPersonId, DaggerfallNpcKind.Static, "charing/named", charing, look,
            "merchant", ["talk"], displayName: "Theodore Copperwing");
        npcs.RegisterDeterministic(hiredGuardId, DaggerfallNpcKind.Static, "charing/hire", charing, look,
            "road-guard", ["talk"], displayName: "Morgyrrya Hearthford");
        npcs.RegisterDeterministic(convoyGuardId, DaggerfallNpcKind.Civilian, "population/convoy/guard-1", charing, look,
            "convoy guard", ["talk"], displayName: "Gondistyr Woodhart");

        Dictionary<long, DaggerfallActorDefinition> actors = new()
        {
            [namedPersonId] = staticNpc,
            [hiredGuardId] = hiredRoadGuardDef,
            [convoyGuardId] = civilian,
            [centaurId] = centaur,
        };
        PresentationState state = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcome = new(state, actors, definitions.Mobiles, definitions.Text, npcs, definitions.ActorCatalog);

        // Named person (static-npc definition): player hit, enemy hit, deflect, absorb, whiff, defeat, drain.
        outcome.React(new AttackHitFact(player, namedPersonId, 8, 8, 0, EnemyAttack: false, 1, 1));
        Assert.Equal("Hit Theodore Copperwing for 8 damage", state.LastOutcome);
        outcome.React(new AttackHitFact(namedPersonId, player, 5, 5, 0, EnemyAttack: true, 1, 2));
        Assert.Equal("Theodore Copperwing hit you for 5 damage", state.LastOutcome);
        outcome.React(new VitalTransferredFact(namedPersonId, player, "health", 4, 4, 4, false, 1, 3));
        Assert.Equal("Theodore Copperwing drained 4 health from you; restored 4 health.", state.LastOutcome);
        outcome.React(new ActorDiedFact(namedPersonId, player, DaggerfallDamageCause.PhysicalAttack, 10, 6, 1, 4));
        Assert.StartsWith("Defeated Theodore Copperwing for 6 damage", state.LastOutcome);

        // Hired road guard (static-npc + hire-road-guard spawner) fighting a Centaur.
        outcome.React(new AttackHitFact(hiredGuardId, centaurId, 12, 12, 0, EnemyAttack: true, 1, 5));
        Assert.Equal("Morgyrrya Hearthford hit Centaur for 12 damage", state.LastOutcome);
        outcome.React(new AttackNegatedFact(hiredGuardId, centaurId, HarmNegation.Deflected, EnemyAttack: true, 1, 6));
        Assert.Equal("Centaur deflects Morgyrrya Hearthford's blow", state.LastOutcome);
        outcome.React(new AttackHitFact(centaurId, hiredGuardId, 9, 0, 0, EnemyAttack: true, 1, 7) { Mitigated = new(0, 9) });
        Assert.Equal("Morgyrrya Hearthford's armor absorbs Centaur's blow", state.LastOutcome);

        // Convoy guard (civilian definition) fighting a Centaur.
        outcome.React(new AttackHitFact(convoyGuardId, centaurId, 7, 7, 0, EnemyAttack: true, 1, 8));
        Assert.Equal("Gondistyr Woodhart hit Centaur for 7 damage", state.LastOutcome);
        outcome.React(new AttackWhiffedFact(convoyGuardId, centaurId, EnemyAttack: true, 1, 9));
        Assert.Equal("Gondistyr Woodhart's blow misses Centaur", state.LastOutcome);
        outcome.React(new RangedShotBlockedFact(convoyGuardId, centaurId, 1, 10));
        Assert.Equal("Gondistyr Woodhart's shot is blocked by cover", state.LastOutcome);
    }

    [Fact]
    public void Unnamed_people_and_internal_ids_fall_back_to_readable_roles_and_never_leak_slugs()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition staticNpc = ArchetypeDef(definitions, DaggerfallArchetypeIds.StaticNpc, DaggerfallActorKinds.StaticNpc);
        DaggerfallActorDefinition civilian = ArchetypeDef(definitions, DaggerfallArchetypeIds.Civilian, DaggerfallActorKinds.Civilian);
        DaggerfallActorDefinition roadGuardDef = staticNpc with { SpawnerId = DaggerfallPartyHireService.DefaultRoadGuardSpecId };
        DaggerfallActorDefinition centaur = definitions.RequireActor(new("centaur"));

        DaggerfallNpcRegistry npcs = new();
        DaggerfallNpcSite charing = new(17, "Charing", string.Empty);
        DaggerfallNpcAppearance look = new("breton", "Female", 182, 0, 11, 518);

        const long unnamedTownspersonId = 0x4000_0000_2001L;
        const long unnamedGuardId = 0x4000_0000_2002L;
        const long unnamedRoadGuardId = 0x4000_0000_2003L;
        const long unnamedConvoyGuardId = 0x4000_0000_2004L;
        const long bareCivilianActorId = 4001L;
        const long unknownActorId = 9999L;
        const long centaurId = 4002L;

        npcs.RegisterDeterministic(unnamedTownspersonId, DaggerfallNpcKind.Civilian, "charing/civ", charing, look, "civilian", ["talk"]);
        npcs.RegisterDeterministic(unnamedGuardId, DaggerfallNpcKind.Static, "charing/guard", charing, look, "guard", ["talk"]);
        npcs.RegisterDeterministic(unnamedRoadGuardId, DaggerfallNpcKind.Static, "charing/road-guard", charing, look, "person", ["talk"]);
        npcs.RegisterDeterministic(unnamedConvoyGuardId, DaggerfallNpcKind.Civilian, "population/convoy/guard-2", charing, look, "convoy guard", ["talk"]);

        Dictionary<long, DaggerfallActorDefinition> actors = new()
        {
            [unnamedTownspersonId] = civilian,
            [unnamedGuardId] = staticNpc,
            [unnamedRoadGuardId] = roadGuardDef,
            [unnamedConvoyGuardId] = roadGuardDef,
            [bareCivilianActorId] = civilian,
            [centaurId] = centaur,
        };
        PresentationState state = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcome = new(state, actors, definitions.Mobiles, definitions.Text, npcs, definitions.ActorCatalog);

        outcome.React(new AttackHitFact(unnamedTownspersonId, centaurId, 4, 4, 0, EnemyAttack: true, 1, 1));
        Assert.Equal("Townsperson hit Centaur for 4 damage", state.LastOutcome);

        outcome.React(new AttackHitFact(unnamedGuardId, centaurId, 6, 6, 0, EnemyAttack: true, 1, 2));
        Assert.Equal("Guard hit Centaur for 6 damage", state.LastOutcome);

        outcome.React(new AttackHitFact(unnamedRoadGuardId, centaurId, 9, 9, 0, EnemyAttack: true, 1, 3));
        Assert.Equal("Road Guard hit Centaur for 9 damage", state.LastOutcome);

        outcome.React(new AttackHitFact(unnamedConvoyGuardId, centaurId, 8, 8, 0, EnemyAttack: true, 1, 4));
        Assert.Equal("Convoy Guard hit Centaur for 8 damage", state.LastOutcome);

        outcome.React(new AttackHitFact(bareCivilianActorId, centaurId, 3, 3, 0, EnemyAttack: true, 1, 5));
        Assert.Equal("Townsperson hit Centaur for 3 damage", state.LastOutcome);

        outcome.React(new AttackNegatedFact(unknownActorId, centaurId, HarmNegation.Deflected, EnemyAttack: true, 1, 6));
        Assert.Equal("Centaur deflects Someone's blow", state.LastOutcome);

        outcome.React(new ActorTransformedFact(1, DaggerfallActorIdentity.PlayerEntityId, centaurId,
            DaggerfallWabbajackOutcome.Transformed, "giant-scorpion", 1, 7));
        Assert.Equal("Wabbajack transformed the target into Giant Scorpion", state.LastOutcome);

        foreach (string line in state.Messages)
        {
            Assert.DoesNotContain("static-npc", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("civilian", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clutter-person", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hire-road-guard", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("actor ", line, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Convoy_guard_staged_by_road_envoys_resolves_its_display_name_through_session_actor_naming()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorLedger ledger = new();
        DaggerfallNpcRegistry npcs = new(actors: null, ledger);
        DaggerfallSiteRecord charing = definitions.Locations.Records.First(site => site.Id == new DaggerfallSiteId(17, 4));

        long namedGuardId = DaggerfallRoadEnvoys.Stage(definitions, ledger, worldSocialSeed: 7UL,
            (_, _) => "Alfric Hawkhart", charing, "population/convoy/test/guard-1", "convoy-guard", "convoy guard", "Convoy Guard", 518);
        long fallbackGuardId = DaggerfallRoadEnvoys.Stage(definitions, ledger, worldSocialSeed: 7UL,
            generateName: null, charing, "population/convoy/test/guard-2", "convoy-guard", "convoy guard", "Convoy Guard", 518);

        DaggerfallActorDefinition armedConvoyGuard = ArchetypeDef(definitions, DaggerfallArchetypeIds.Civilian, DaggerfallActorKinds.Civilian) with
        {
            SpawnerId = DaggerfallPartyHireService.DefaultRoadGuardSpecId,
        };
        Dictionary<long, DaggerfallActorDefinition> actors = new()
        {
            [namedGuardId] = armedConvoyGuard,
            [fallbackGuardId] = armedConvoyGuard,
            [5000L] = definitions.RequireActor(new("centaur")),
        };
        PresentationState state = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcome = new(state, actors, definitions.Mobiles, definitions.Text, npcs, definitions.ActorCatalog);

        outcome.React(new AttackHitFact(namedGuardId, 5000L, 11, 11, 0, EnemyAttack: true, 1, 1));
        Assert.Equal("Alfric Hawkhart hit Centaur for 11 damage", state.LastOutcome);

        outcome.React(new AttackNegatedFact(fallbackGuardId, 5000L, HarmNegation.Deflected, EnemyAttack: true, 1, 2));
        Assert.Equal("Centaur deflects Convoy Guard's blow", state.LastOutcome);
    }
}
