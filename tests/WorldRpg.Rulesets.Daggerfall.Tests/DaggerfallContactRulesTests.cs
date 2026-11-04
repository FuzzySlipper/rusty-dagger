using System.Numerics;
using System.Reflection;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;
using Fixture = WorldRpg.Rulesets.Daggerfall.Tests.DaggerCombatDamagePolicyTests.DamagePolicyFixture;
using PreparedPhysicalAttack = WorldRpg.Rulesets.Daggerfall.Modules.Combat.DaggerCombatRules.DaggerfallPreparedAttack;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The contact rules at the rules boundary: no to-hit roll, harm reaches whoever the swing
/// volume or arrow physically meets, and the target deflects or absorbs it by channel.
/// </summary>
public sealed class DaggerfallContactRulesTests
{
    private const long Player = DaggerfallActorIdentity.PlayerEntityId;
    private static DaggerfallContactTuning Tuning => DaggerfallTuning.Defaults.Contact;

    [Fact]
    public void The_contact_tuning_is_read_from_the_default_profile_and_the_classic_bundle_selects_no_variant()
    {
        ProductContent content = FullContent(TestData.RepositoryRoot);
        ResolvedGameComposition classic = Resolve(content, new GameBundleId("daggerfall.classic"), []).RequireComposition();

        Assert.Empty(classic.Identity.Variants);
        DaggerfallContactTuning tuning = DaggerfallTuning.Read(classic.Tuning.Payload.Span).Contact;
        Assert.Equal(new DaggerfallChannelDeflection(DaggerfallDeflectionMode.Partial, .5d), tuning.DeflectionFor(DaggerfallHarmChannels.Blunt));
        Assert.Equal(DaggerfallDeflectionMode.Full, tuning.DeflectionFor(DaggerfallHarmChannels.Edged)!.Mode);
    }

    [Fact]
    public void An_enemy_blow_that_connects_lands_without_a_to_hit_roll_uses_Dodging_and_is_absorbed_by_the_armour_it_strikes()
    {
        (AttackHitFact chest, Fixture chestFixture) = EnemyBlow(bodyRoll: 8);
        (AttackHitFact feet, Fixture feetFixture) = EnemyBlow(bodyRoll: 19);
        using (chestFixture)
        using (feetFixture)
        {
            Assert.Equal((3, 6), (chest.StruckBody, feet.StruckBody));
            // The player wears only an iron cuirass: the chest absorbs, bare feet do not.
            int absorbed = (int)Math.Round(TestPayload.Definitions.ArmorValuesByMaterial["iron"] * Tuning.AbsorptionPerArmorPoint
                * Tuning.AbsorptionMultiplier(DaggerfallHarmChannels.Edged), MidpointRounding.AwayFromZero);
            Assert.True(absorbed > 0);
            Assert.Equal(feet.CalculatedDamage - absorbed, chest.CalculatedDamage);
            // The hit carries what the armour took, for presentation; bare feet took the whole blow.
            Assert.Equal((0, absorbed), (chest.Mitigated.Deflected, chest.Mitigated.Absorbed));
            Assert.Equal(default, feet.Mitigated);
            Assert.Single(chestFixture.SkillUses, use => use.Skill == DaggerfallMechanicsIds.Dodging.Value);
        }
    }

    private static (AttackHitFact Hit, Fixture Fixture) EnemyBlow(int bodyRoll)
    {
        Fixture fixture = new(random: KeyedRandom.Create(bodyRoll).Service);
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        fixture.Spatial.OverlapContactsResult = _ => Touching(fixture.Actors.Player.Actor.Entity.Value);
        AttackRequest swing = new(2, Player, 5, 9, .125d, Delayed: true);
        fixture.StartSwing(swing, out bool admitted);
        Assert.True(admitted);
        // The strike's frame comes before the wind-up ends; the blow lands once it has.
        IReadOnlyList<IProductFact> facts = [.. fixture.DeliverImpact(swing), .. fixture.AdvanceFlight(5, 9, steps: 5)];
        Assert.DoesNotContain(facts, fact => fact is AttackWhiffedFact);
        return (Assert.Single(facts.OfType<AttackHitFact>()), fixture);
    }

    [Fact]
    public void A_timed_player_swing_lands_one_blow_on_the_body_its_sweep_met_asks_no_volume_and_one_that_met_nothing_whiffs()
    {
        KeyedRandom random = KeyedRandom.Create(8);
        using Fixture fixture = new(random: random.Service);
        ActorState bystander = fixture.SpawnBrigand(3, new WorldPoint(1f, 0f, .4f));
        // Were the volume still asked, it would answer both bodies.
        fixture.Spatial.OverlapContactsResult = _ => Touching(fixture.Actors.Get(2).Actor.Entity.Value, bystander.Actor.Entity.Value);
        AttackRequest swing = new(Player, null, 3, 5, .125d, Delayed: true);

        Assert.Null(Assert.Single(fixture.StartSwing(swing, out bool admitted).OfType<PlayerAttackStartedFact>()).TargetId);
        Assert.True(admitted);
        fixture.Swept(2);
        IReadOnlyList<IProductFact> facts = fixture.DeliverImpact(swing);

        AttackHitFact hit = Assert.Single(facts.OfType<AttackHitFact>());
        Assert.Equal((Player, 2L), (hit.AttackerId, hit.TargetId));
        Assert.Empty(fixture.Spatial.OverlapContactsRequests);
        Assert.Contains(fixture.SkillUses, use => use.Reason == DaggerfallSkillUseReason.WeaponHit);

        AttackRequest whiff = swing with { SimulationStep = 500 };
        fixture.StartSwing(whiff, out _);
        AttackWhiffedFact whiffed = Assert.Single(fixture.DeliverImpact(whiff).OfType<AttackWhiffedFact>());
        Assert.Equal((Player, (long?)null, false), (whiffed.AttackerId, whiffed.TargetId, whiffed.EnemyAttack));
    }

    [Fact]
    public void An_enemy_swing_and_an_undelayed_player_swing_reach_every_actor_their_volume_touches()
    {
        using Fixture fixture = new(random: KeyedRandom.Create(8).Service);
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        ActorState bystander = fixture.SpawnBrigand(3, new WorldPoint(1f, 0f, .4f));
        fixture.Spatial.OverlapContactsResult = _ => Touching(fixture.Actors.Player.Actor.Entity.Value, bystander.Actor.Entity.Value);
        AttackRequest enemy = new(2, Player, 5, 9, .125d, Delayed: true);
        fixture.StartSwing(enemy, out _);

        IReadOnlyList<IProductFact> facts = [.. fixture.DeliverImpact(enemy), .. fixture.AdvanceFlight(5, 9, steps: 5)];

        Assert.Equal([Player, 3L], facts.OfType<AttackHitFact>().Select(hit => hit.TargetId));
        SpatialContactQueryRequest volume = Assert.Single(fixture.Spatial.OverlapContactsRequests);
        Assert.Equal(SpatialShapeKind.Box, volume.Shape.Kind);
        Assert.Equal(new Vector3(1f, 1.62f, 0f), volume.Start.Translation);

        fixture.Spatial.OverlapContactsResult = _ => Touching(fixture.Actors.Get(2).Actor.Entity.Value, bystander.Actor.Entity.Value);
        IReadOnlyList<IProductFact> explicitSwing = fixture.StartSwing(new AttackRequest(Player, 2, 7, 1, .125d, Delayed: false), out _);
        Assert.Equal([2L, 3L], explicitSwing.OfType<AttackHitFact>().Select(hit => hit.TargetId));
    }

    [Fact]
    public void Every_connect_trains_the_weapon_skill_once_whether_it_lands_or_is_deflected_and_a_whiff_trains_nothing()
    {
        static int WeaponHits(Fixture fixture) => fixture.SkillUses.Count(use => use.Reason == DaggerfallSkillUseReason.WeaponHit);
        AttackRequest swing = new(Player, null, 3, 5, .125d, Delayed: true);

        using Fixture landing = new(random: KeyedRandom.Create(8).Service);
        landing.StartSwing(swing, out _);
        landing.Swept(2);
        Assert.Single(landing.DeliverImpact(swing).OfType<AttackHitFact>());
        Assert.Equal(1, WeaponHits(landing));

        // The lowest draw on every percentile deflects the edged blow whole.
        using Fixture deflected = new(random: RandomMinimum.Create());
        deflected.StartSwing(swing, out _);
        deflected.Swept(2);
        IReadOnlyList<IProductFact> turned = deflected.DeliverImpact(swing);
        Assert.Empty(turned.OfType<AttackHitFact>());
        AttackNegatedFact negated = Assert.Single(turned.OfType<AttackNegatedFact>());
        Assert.Equal((Player, 2L, HarmNegation.Deflected), (negated.AttackerId, negated.TargetId, negated.Negation));
        Assert.Equal(1, WeaponHits(deflected));

        using Fixture whiff = new(random: KeyedRandom.Create(8).Service);
        whiff.StartSwing(swing, out _);
        whiff.DeliverImpact(swing);
        Assert.Equal(0, WeaponHits(whiff));
    }

    [Fact]
    public void A_swing_aimed_at_an_actor_defeated_before_its_blow_still_lands_on_whoever_its_sweep_met()
    {
        using Fixture fixture = new(random: KeyedRandom.Create(8).Service);
        ActorState bystander = fixture.SpawnBrigand(3, new WorldPoint(1f, 0f, .4f));
        AttackRequest swing = new(Player, 2, 3, 5, .125d, Delayed: true);
        fixture.StartSwing(swing, out bool admitted);
        Assert.True(admitted);

        fixture.Actors.Get(2).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0);
        fixture.Swept(3);

        Assert.Equal(3L, Assert.Single(fixture.DeliverImpact(swing).OfType<AttackHitFact>()).TargetId);
    }

    [Fact]
    public void An_enemy_swing_publishes_its_wind_up_and_its_damage_waits_for_it_however_early_the_frame_comes()
    {
        using Fixture fixture = new(random: KeyedRandom.Create(8).Service);
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        fixture.Spatial.OverlapContactsResult = _ => Touching(fixture.Actors.Player.Actor.Entity.Value);
        AttackRequest swing = new(2, Player, 5, 9, .125d, Delayed: true);

        EnemyAttackTelegraphFact telegraph = Assert.Single(fixture.StartSwing(swing, out _).OfType<EnemyAttackTelegraphFact>());
        Assert.Equal((2L, Player, Tuning.EnemyWindUpSeconds, 9UL), (telegraph.AttackerId, telegraph.TargetId, telegraph.WindUpSeconds, telegraph.OriginatingSimulationStep));

        // .4s at .125s steps is four steps: the frame at step 9 waits until step 13.
        Assert.Empty(fixture.DeliverImpact(swing).OfType<AttackHitFact>());
        Assert.Empty(fixture.AdvanceFlight(5, 9, steps: 4).OfType<AttackHitFact>());
        Assert.Empty(fixture.Spatial.OverlapContactsRequests);
        Assert.Single(fixture.AdvanceFlight(5, 13, steps: 1).OfType<AttackHitFact>());
        Assert.Empty(fixture.AdvanceFlight(5, 14, steps: 2).OfType<AttackHitFact>());
    }

    [Fact]
    public void An_interrupted_attack_drops_its_winding_up_blow_so_it_never_lands()
    {
        using Fixture fixture = new(random: KeyedRandom.Create(8).Service);
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        fixture.Spatial.OverlapContactsResult = _ => Touching(fixture.Actors.Player.Actor.Entity.Value);
        AttackRequest swing = new(2, Player, 5, 9, .125d, Delayed: true);
        fixture.StartSwing(swing, out _);

        // The early frame hands the blow to its wind-up; the enemy then leaves its attack before it ends.
        Assert.Empty(fixture.DeliverImpact(swing).OfType<AttackHitFact>());
        fixture.Interrupt(2, 5);

        Assert.Empty(fixture.AdvanceFlight(5, 9, steps: 8).OfType<AttackHitFact>());
        Assert.Empty(fixture.Spatial.OverlapContactsRequests);
    }

    [Fact]
    public void Melee_enemies_engage_inside_their_reach_while_archers_engage_at_range()
    {
        IDaggerfallEnemyEngagement contact = new DaggerfallContactEngagement(Tuning);
        Assert.Equal(2.25d * Tuning.EngageReachFraction, contact.EngageReach(2.25d, ranged: false), 6);
        Assert.True(contact.EngageReach(2.25d, ranged: false) < 2.25d);
        Assert.Equal(30d, contact.EngageReach(30d, ranged: true));
    }

    [Fact]
    public void An_enemy_archer_holds_a_shot_another_body_or_the_world_would_stop()
    {
        using Fixture fixture = new();
        ulong player = fixture.Actors.Player.Actor.Entity.Value;
        ulong bystander = fixture.SpawnBrigand(3, new WorldPoint(.5f, 0f, 0f)).Actor.Entity.Value;
        SpatialHit Struck(SpatialHitKind kind, ulong entity) => default(SpatialHit) with { Present = true, Kind = kind, Entity = entity, Distance = .4d };

        fixture.Spatial.FloorHit = _ => Struck(SpatialHitKind.Entity, player);
        Assert.False(fixture.Delivery.HoldsFire(2, Player));
        fixture.Spatial.FloorHit = _ => Struck(SpatialHitKind.Entity, bystander);
        Assert.True(fixture.Delivery.HoldsFire(2, Player));
        fixture.Spatial.FloorHit = _ => Struck(SpatialHitKind.StaticMesh, 0);
        Assert.True(fixture.Delivery.HoldsFire(2, Player));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(5f)]
    public void An_enemy_archer_leads_a_moving_target_by_the_arrows_flight_time(float sideways)
    {
        using Fixture fixture = new();
        fixture.PlayerVelocity = new Vector3(0f, 0f, sideways);
        AttackRequest shot = new(2, Player, 5, 9, .125d, Delayed: true);
        PreparedPhysicalAttack arrow = new(1d, new AttackOutcome(true, true, 0), DaggerfallStrikeFeedback.Unarmed, IsRanged: true);
        Assert.True(fixture.Delivery.DeferImpact(new DeferredAttackImpact(shot, arrow), new FactBuffer<IProductFact>()));
        fixture.AdvanceFlight(5, 9, steps: 1);

        // The arrow's first segment is cast along its launch direction.
        Vector3 direction = fixture.Spatial.FloorProbes.First().Direction;
        if (sideways == 0f) Assert.Equal(0f, direction.Z, 4);
        else
        {
            // Brigand eye (1, 1.62, 0) to player eye (0, 1.65, 0): about a metre, so 1/25 s of flight.
            float distance = Vector3.Distance(new(1f, 1.62f, 0f), new(0f, 1.65f, 0f));
            Vector3 expected = Vector3.Normalize(new Vector3(0f, 1.65f, 0f) + new Vector3(0f, 0f, sideways) * (float)(distance / Tuning.ProjectileSpeed * Tuning.ArcherLeadFraction) - new Vector3(1f, 1.62f, 0f));
            Assert.Equal(expected.Z, direction.Z, 4);
            Assert.True(direction.Z > 0f);
        }
    }

    [Fact]
    public void Deflection_negates_an_edged_blow_whole_turns_aside_part_of_a_blunt_one_and_the_material_requirement_still_holds()
    {
        using Fixture fixture = new();
        DaggerfallSeamInputs inputs = fixture.SeamInputs;
        DaggerfallActorDefinition player = TestPayload.Definitions.RequireActor(new DaggerfallActorId("player"));
        CombatParticipants participants = new(fixture.Actors.Get(2).Actor, fixture.Actors.Player.Actor, "attack");
        DaggerfallPhysicalHarm chest = new(2, Player, player, "long-blade", "iron", 3, 1, 1);

        DaggerfallContactMitigation certain = new(inputs with { Random = RandomMinimum.Create() }, Tuning, new FixedGuard());
        NegationEvent edged = new(participants, DaggerfallHarmChannels.Edged);
        certain.Negate(edged, chest);
        Assert.Equal(HarmNegation.Deflected, edged.Negation);
        MitigationEvent blunt = new(participants, DaggerfallHarmChannels.Blunt, 10);
        certain.Mitigate(blunt, chest with { Skill = "blunt-weapon" });
        Assert.Equal(5, blunt.Deflected);
        Assert.Equal(certain.Absorption(chest, DaggerfallHarmChannels.Blunt), blunt.Absorbed);

        DaggerfallContactMitigation never = new(inputs with { Random = RandomMaximum.Create() }, Tuning, new FixedGuard());
        NegationEvent landed = new(participants, DaggerfallHarmChannels.Edged);
        never.Negate(landed, chest);
        Assert.Equal(HarmNegation.None, landed.Negation);

        NegationEvent immune = new(participants, DaggerfallHarmChannels.Edged);
        never.Negate(immune, chest with { Target = player with { MinimumMaterial = "silver" } });
        Assert.Equal(HarmNegation.Immune, immune.Negation);
    }

    [Fact]
    public void Worn_armor_enchantments_shift_what_the_players_armor_absorbs()
    {
        using Fixture fixture = new();
        DaggerfallContactMitigation mitigation = new(fixture.SeamInputs, Tuning, new FixedGuard());
        DaggerfallActorDefinition player = TestPayload.Definitions.RequireActor(new DaggerfallActorId("player"));
        CombatParticipants participants = new(fixture.Actors.Get(2).Actor, fixture.Actors.Player.Actor, "attack");
        DaggerfallPhysicalHarm chest = new(2, Player, player, "long-blade", "iron", 3, 1, 1);
        int Absorbed(int shift)
        {
            fixture.ArmorValueShift = shift;
            MitigationEvent blow = new(participants, DaggerfallHarmChannels.Edged, 20);
            mitigation.Mitigate(blow, chest);
            return blow.Absorbed;
        }

        int plain = Absorbed(0);
        Assert.Equal(plain + 1, Absorbed(DaggerfallHeldEnchantments.StrengthenedArmorValue));
        Assert.Equal(Math.Max(0, plain - 1), Absorbed(DaggerfallHeldEnchantments.WeakenedArmorValue));
        // An enemy's absorption reads no player enchantment.
        fixture.ArmorValueShift = DaggerfallHeldEnchantments.StrengthenedArmorValue;
        DaggerfallPhysicalHarm onEnemy = chest with { AttackerId = Player, TargetId = 2, Target = TestPayload.Definitions.RequireActor(new DaggerfallActorId("thief")) };
        int enemyStrengthened = mitigation.Absorption(onEnemy, DaggerfallHarmChannels.Edged);
        fixture.ArmorValueShift = 0;
        Assert.Equal(mitigation.Absorption(onEnemy, DaggerfallHarmChannels.Edged), enemyStrengthened);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_arrow_flies_step_by_step_and_harms_the_first_actor_it_meets_or_stops_at_the_world(bool meetsActor)
    {
        KeyedRandom random = KeyedRandom.Create(8);
        using Fixture fixture = new(armed: false, random: random.Service);
        fixture.EquipPlayerBow("iron-short-bow", 3001);
        fixture.GivePlayerArrows(1);
        ulong brigand = fixture.Actors.Get(2).Actor.Entity.Value;
        fixture.Spatial.FloorHit = request => meetsActor
            ? new SpatialHit(true, SpatialHitKind.Entity, brigand, 0, 0, 0, 0, 0, 0, default, request.Origin + request.Direction * .7f, -request.Direction, .7d, 0d, 0d, false, true)
            : new SpatialHit(true, SpatialHitKind.StaticMesh, 0, 1, 1, 1, 0, 0, 0, default, request.Origin + request.Direction, -request.Direction, 1d, 0d, 0d, false, true);
        AttackRequest shot = new(Player, 2, 3, 5, .125d, Delayed: true);
        fixture.StartSwing(shot, out bool admitted);
        Assert.True(admitted);
        Assert.Empty(fixture.DeliverImpact(shot).OfType<AttackHitFact>()); // Released into flight, not applied.

        IReadOnlyList<IProductFact> flight = fixture.AdvanceFlight(3, 6, steps: 1);
        if (meetsActor) Assert.Equal(2L, Assert.Single(flight.OfType<AttackHitFact>()).TargetId);
        else Assert.Single(flight.OfType<RangedShotBlockedFact>());
        Assert.Empty(fixture.AdvanceFlight(3, 7, steps: 2).OfType<AttackHitFact>());
    }

    [Fact]
    public void A_game_composes_the_contact_owners_and_saves_and_restores()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallSessionComposition composition = new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        var engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));

        RulesetSavePayload saved;
        using (DaggerfallSession creation = DaggerfallSession.StartNew(engine.Context, composition))
        {
            NewGameSessionTests.Commit(creation, "class13");
            using DaggerfallSession game = Assert.IsType<DaggerfallSession>(creation.CreateNewGame());
            game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(9);
            saved = game.CaptureSave();
        }
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saved);
        Assert.Equal(9, restored.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void Holding_block_raises_a_guard_only_with_a_drawn_weapon_and_no_swing_and_it_costs_stamina_until_the_pool_empties()
    {
        using Fixture fixture = new();
        DaggerfallContactGuard guard = new(fixture.SeamInputs, Tuning);
        Track stamina = fixture.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));
        stamina.SetCurrent(10d);
        FactBuffer<IProductFact> facts = new();

        guard.Step(.5d, canGuard: true, swinging: false, 1, 1, facts);
        Assert.False(guard.Raised); // Nothing is held yet.
        guard.Observe([Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, intent: "block")], []);
        guard.Step(.5d, canGuard: false, swinging: false, 1, 2, facts);
        Assert.False(guard.Raised); // No drawn weapon, no guard.
        guard.Step(.5d, canGuard: true, swinging: true, 1, 3, facts);
        Assert.False(guard.Raised); // Never raised mid-swing.
        guard.Step(.5d, canGuard: true, swinging: false, 1, 4, facts);
        Assert.True(guard.Raised);
        Assert.Equal(10d, stamina.Current); // Raising is free; holding is not.
        guard.Step(.5d, canGuard: true, swinging: false, 1, 5, facts);
        Assert.Equal(10d - Tuning.GuardStaminaPerSecond * .5d, stamina.Current, 6);
        guard.Step(.5d, canGuard: true, swinging: true, 1, 6, facts);
        Assert.False(guard.Raised); // A swing ends the block.
        guard.Step(.5d, canGuard: true, swinging: false, 1, 7, facts);
        Assert.True(guard.Raised);
        stamina.SetCurrent(.1d);
        guard.Step(.5d, canGuard: true, swinging: false, 1, 8, facts);
        Assert.False(guard.Raised); // An emptied pool drops the guard.
        Assert.Equal(0d, stamina.Current);

        stamina.SetCurrent(10d);
        guard.Step(.5d, canGuard: true, swinging: false, 1, 9, facts);
        guard.Observe([Input(InputEventKind.MappedDigital, InputEdge.Released, x: 0, intent: "block")], []);
        guard.Step(.5d, canGuard: true, swinging: false, 1, 10, facts);
        Assert.False(guard.Raised);
        guard.Observe([Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, intent: "block")], []);
        guard.Step(.5d, canGuard: true, swinging: false, 1, 11, facts);
        guard.Neutralize(1, 12, facts);
        Assert.False(guard.Raised);

        List<IProductFact> published = [];
        facts.Deliver(published.Add);
        Assert.Equal([true, false, true, false, true, false, true, false],
            published.OfType<PlayerGuardChangedFact>().Select(fact => fact.Raised));
    }

    [Fact]
    public void A_raised_guard_adds_its_deflection_bonus_to_the_player_only()
    {
        using Fixture fixture = new();
        DaggerfallSeamInputs inputs = fixture.SeamInputs;
        FixedGuard guard = new();
        DaggerfallContactMitigation mitigation = new(inputs, Tuning, guard);
        int lowered = mitigation.DeflectionChance(Player), enemy = mitigation.DeflectionChance(2);
        guard.Raised = true;
        Assert.Equal(Math.Min(lowered + (int)Tuning.GuardDeflectionBonus, (int)Tuning.GuardMaximumChance), mitigation.DeflectionChance(Player));
        Assert.Equal(enemy, mitigation.DeflectionChance(2));
    }

    [Fact]
    public void The_default_owners_share_one_guard_the_mitigation_reads()
    {
        using Fixture fixture = new();
        DaggerfallComposedSeams seams = DaggerfallSeamSelection.Default.Build(fixture.SeamInputs, Tuning);
        DaggerfallContactGuard guard = Assert.IsType<DaggerfallContactGuard>(seams.PlayerGuard);
        DaggerfallContactMitigation mitigation = Assert.IsType<DaggerfallContactMitigation>(seams.PhysicalMitigation);
        int lowered = mitigation.DeflectionChance(Player);
        FactBuffer<IProductFact> facts = new();

        guard.Observe([Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, intent: "block")], []);
        guard.Step(.5d, canGuard: true, swinging: false, 1, 1, facts);

        Assert.True(guard.Raised);
        Assert.True(mitigation.DeflectionChance(Player) > lowered);
    }

    private sealed class FixedGuard : IDaggerfallPlayerGuard
    {
        public bool Raised { get; set; }
        public bool WantsTime => false;
        public void Observe(ReadOnlySpan<ProductInputEvent> inputs, IReadOnlyList<WorldRpg.Kit.Controls.ControllerActionBinding> pad) { }
        public void Step(double seconds, bool canGuard, bool swinging, ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts) { }
        public void Neutralize(ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts) { }
    }

    [Fact]
    public void A_touch_reaches_every_actor_its_volume_contacts_but_none_behind_the_world_and_one_that_meets_no_one_is_still_released()
    {
        using Fixture fixture = new();
        IDaggerfallTouchDelivery touch = new DaggerfallContactTouch(fixture.TouchOperations, Tuning);
        ulong brigand = fixture.Actors.Get(2).Actor.Entity.Value;
        ulong second = fixture.SpawnBrigand(3, new WorldPoint(1f, 0f, .4f)).Actor.Entity.Value;
        DaggerfallTouchCast cast = new(Player, new Vector3(0f, 1.65f, 0f), Vector3.UnitX, Admitted: null);

        fixture.Spatial.OverlapContactsResult = _ => Touching(brigand);
        DaggerfallTouchReach one = touch.Reach(cast);
        Assert.True(one.Releases);
        Assert.Equal([2L], one.Targets);
        SpatialContactQueryRequest volume = Assert.Single(fixture.Spatial.OverlapContactsRequests);
        Assert.Equal(SpatialShapeKind.Box, volume.Shape.Kind);
        Assert.Equal(cast.Eye, volume.Start.Translation);

        fixture.Spatial.OverlapContactsResult = _ => Touching(brigand, second);
        Assert.Equal([2L, 3L], touch.Reach(cast).Targets);

        fixture.Spatial.OverlapContactsResult = _ => Touching();
        DaggerfallTouchReach none = touch.Reach(cast);
        Assert.True(none.Releases);
        Assert.Empty(none.Targets);

        // World geometry between the hand and the body it would touch shields that body.
        fixture.Spatial.OverlapContactsResult = _ => Touching(brigand);
        fixture.Spatial.FloorHit = _ => default(SpatialHit) with { Present = true, Kind = SpatialHitKind.StaticMesh, Distance = .2d };
        DaggerfallTouchReach occluded = touch.Reach(cast);
        Assert.True(occluded.Releases);
        Assert.Empty(occluded.Targets);
    }

    [Fact]
    public void An_enemy_touch_reaches_the_player_only_when_its_volume_does_whatever_its_behavior_admitted()
    {
        using Fixture fixture = new();
        IDaggerfallTouchDelivery touch = new DaggerfallContactTouch(fixture.TouchOperations, Tuning);
        DaggerfallTouchCast cast = new(2, new Vector3(1f, 1.62f, 0f), -Vector3.UnitX, Admitted: Player);

        fixture.Spatial.OverlapContactsResult = _ => Touching();
        Assert.Empty(touch.Reach(cast).Targets);
        fixture.Spatial.OverlapContactsResult = _ => Touching(fixture.Actors.Player.Actor.Entity.Value);
        Assert.Equal([Player], touch.Reach(cast).Targets);
    }

    [Fact]
    public void A_player_touch_that_meets_no_one_is_paid_for_and_misses()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallSessionComposition composition = new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession creation = DaggerfallSession.StartNew(engine.Context, composition);
        NewGameSessionTests.Commit(creation, "class13");
        using DaggerfallSession game = Assert.IsType<DaggerfallSession>(creation.CreateNewGame());
        Track magicka = game.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value));
        magicka.Maximum.BaseValue = 1000;
        magicka.SetCurrent(1000);
        string spell = TouchSpellThePlayerCanReady(game);
        spatial.OverlapContactsRequests.Clear();

        DaggerfallCastResult result = game.ReleaseReadySpell(Player, Vector3.UnitZ);

        Assert.Equal(DaggerfallCastOutcome.Missed, result.Outcome);
        Assert.True(magicka.Current < 1000, $"Releasing '{spell}' spent no magicka.");
        Assert.NotEmpty(spatial.OverlapContactsRequests);
        Assert.Null(game.Casting.ReadyFor(Player));
    }

    /// <summary>Teaches the player the first stock touch spell whose effects this ruleset compiles, and readies it.</summary>
    private static string TouchSpellThePlayerCanReady(DaggerfallSession game)
    {
        foreach (DaggerfallSpellDefinition spell in TestPayload.Definitions.Magic.Spells.Values.OrderBy(spell => spell.Key, StringComparer.Ordinal))
        {
            if (DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType) != DaggerfallSpellTarget.ByTouch) continue;
            game.State.Character.LearnSpell(spell.Key);
            if (game.ReadyPlayerSpell(spell.Key).Outcome == DaggerfallCastOutcome.Ready) return spell.Key;
        }
        throw new InvalidOperationException("No stock touch spell can be readied.");
    }

    private static ProductContent Content(params (string Path, byte[] Value)[] files) =>
        new(files.Select(file => new ProductContentFile(Encoding.UTF8.GetBytes(file.Path), file.Value)).ToArray());

    /// <summary>
    /// Draws the body part named by <paramref name="bodyRoll"/>, the top of every percentile (no deflection,
    /// no critical), and the maximum of anything else; records every key it was asked for.
    /// </summary>
    internal class KeyedRandom : DispatchProxy
    {
        private int _bodyRoll;
        internal IRandomService Service { get; private set; } = null!;
        internal List<string> Keys { get; } = [];
        internal static KeyedRandom Create(int bodyRoll)
        {
            IRandomService service = Create<IRandomService, KeyedRandom>();
            KeyedRandom proxy = (KeyedRandom)(object)service;
            proxy._bodyRoll = bodyRoll;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            Keys.Add(request.Key);
            return new KeyedRngReceipt(request.Minimum == 0 && request.Maximum == 19 ? _bodyRoll : request.Maximum);
        }
    }
}
