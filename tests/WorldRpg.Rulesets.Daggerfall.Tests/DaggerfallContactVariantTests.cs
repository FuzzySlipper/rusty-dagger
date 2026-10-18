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
using WorldRpg.Rulesets.Daggerfall.Variants;
using Xunit;
using Fixture = WorldRpg.Rulesets.Daggerfall.Tests.DaggerCombatDamagePolicyTests.DamagePolicyFixture;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The contact + mitigation variant at the rules boundary: no to-hit roll, harm reaches whoever the swing
/// volume or arrow physically meets, and the target deflects or absorbs it by channel.
/// </summary>
public sealed class DaggerfallContactVariantTests
{
    private const long Player = DaggerfallActorIdentity.PlayerEntityId;
    private static DaggerfallRulesVariant Contact => DaggerfallVariants.All.Single(variant => variant.Manifest.Id == DaggerfallVariants.Contact);
    private static DaggerfallContactTuning Tuning => DaggerfallContactTuning.Read(File.ReadAllBytes(TuningPath));
    private static string TuningPath => Path.Combine(TestData.RepositoryRoot, "content/worldrpg/variant-tuning-payloads/daggerfall.contact.json");

    [Fact]
    public void The_contact_bundle_selects_the_variant_for_its_physical_and_touch_seams_and_classic_selects_none()
    {
        ProductContent content = TestSessions.FullContent(TestData.RepositoryRoot);
        RulesVariantManifest[] compiled = [.. DaggerfallVariants.All.Select(variant => variant.Manifest)];

        ResolvedGameComposition contact = GameCompositionResolver.Resolve(content, new GameBundleId("daggerfall.contact"), compiled).RequireComposition();
        ResolvedGameComposition classic = GameCompositionResolver.Resolve(content, new GameBundleId("daggerfall.classic"), compiled).RequireComposition();

        Assert.Equal([DaggerfallVariants.Contact], contact.Identity.Variants);
        Assert.Empty(classic.Identity.Variants);
        Assert.Equal(classic.ContentPacks.Select(pack => pack.Id), contact.ContentPacks.Select(pack => pack.Id));
        DaggerfallSeamSelection selection = DaggerfallSeamSelection.From(contact.Variants, DaggerfallVariants.All);
        Assert.Equal((DaggerfallVariants.Contact, DaggerfallVariants.Contact), (selection.PhysicalDeliveryVariant!.Value, selection.PhysicalMitigationVariant!.Value));
        Assert.Equal(DaggerfallVariants.Contact, selection.TouchDeliveryVariant);
        DaggerfallContactTuning tuning = DaggerfallContactTuning.Read(contact.Variants.Single().Tuning.Payload.Span);
        Assert.Equal(new DaggerfallChannelDeflection(DaggerfallDeflectionMode.Partial, .5d), tuning.DeflectionFor(DaggerfallHarmChannels.Blunt));
        Assert.Equal(DaggerfallDeflectionMode.Full, tuning.DeflectionFor(DaggerfallHarmChannels.Edged)!.Mode);
    }

    [Fact]
    public void An_enemy_blow_that_connects_lands_without_a_to_hit_roll_uses_Dodging_and_is_absorbed_by_the_armour_it_strikes()
    {
        (AttackHitFact chest, KeyedRandom chestRandom, Fixture chestFixture) = EnemyBlow(bodyRoll: 8);
        (AttackHitFact feet, _, Fixture feetFixture) = EnemyBlow(bodyRoll: 19);
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
            Assert.DoesNotContain(chestRandom.Keys, key => key.EndsWith($":salt:{CombatRandomKey.HitSalt}", StringComparison.Ordinal));
            Assert.Contains(chestFixture.SkillUses, use => use.Skill == DaggerfallMechanicsIds.Dodging.Value);
        }
    }

    private static (AttackHitFact Hit, KeyedRandom Random, Fixture Fixture) EnemyBlow(int bodyRoll)
    {
        KeyedRandom random = KeyedRandom.Create(bodyRoll);
        Fixture fixture = new(random: random.Service, variant: Contact);
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        fixture.Spatial.OverlapContactsResult = _ => Touching(fixture.Actors.Player.Actor.Entity.Value);
        AttackRequest swing = new(2, Player, 5, 9, .125d, Delayed: true);
        fixture.StartSwing(swing, out bool admitted);
        Assert.True(admitted);
        IReadOnlyList<IProductFact> facts = fixture.DeliverImpact(swing);
        Assert.DoesNotContain(facts, fact => fact is AttackMissedFact);
        return (Assert.Single(facts.OfType<AttackHitFact>()), random, fixture);
    }

    [Fact]
    public void A_player_swing_reaches_every_actor_its_volume_touches_and_a_whiff_reports_it_met_nothing()
    {
        KeyedRandom random = KeyedRandom.Create(8);
        using Fixture fixture = new(random: random.Service, variant: Contact);
        fixture.Spatial.OverlapContactsResult = _ => Touching(fixture.Actors.Get(2).Actor.Entity.Value);
        AttackRequest swing = new(Player, null, 3, 5, .125d, Delayed: true);

        Assert.True(Assert.Single(fixture.StartSwing(swing, out bool admitted).OfType<PlayerAttackStartedFact>()).WaitsWithoutTarget);
        Assert.True(admitted);
        IReadOnlyList<IProductFact> facts = fixture.DeliverImpact(swing);

        AttackHitFact hit = Assert.Single(facts.OfType<AttackHitFact>());
        Assert.Equal((Player, 2L), (hit.AttackerId, hit.TargetId));
        SpatialContactQueryRequest volume = Assert.Single(fixture.Spatial.OverlapContactsRequests);
        Assert.Equal(SpatialShapeKind.Box, volume.Shape.Kind);
        Assert.Equal(new Vector3(0f, 1.65f, 0f), volume.Start.Translation);
        Assert.Contains(fixture.SkillUses, use => use.Reason == DaggerfallSkillUseReason.WeaponHit);

        fixture.Spatial.OverlapContactsResult = _ => Touching();
        AttackRequest whiff = swing with { SimulationStep = 500 };
        fixture.StartSwing(whiff, out _);
        AttackWhiffedFact whiffed = Assert.Single(fixture.DeliverImpact(whiff).OfType<AttackWhiffedFact>());
        Assert.Equal((Player, (long?)null, false), (whiffed.AttackerId, whiffed.TargetId, whiffed.EnemyAttack));
    }

    [Fact]
    public void Every_connect_trains_the_weapon_skill_once_whether_it_lands_or_is_deflected_and_a_whiff_trains_nothing()
    {
        static int WeaponHits(Fixture fixture) => fixture.SkillUses.Count(use => use.Reason == DaggerfallSkillUseReason.WeaponHit);
        AttackRequest swing = new(Player, null, 3, 5, .125d, Delayed: true);

        using Fixture landing = new(random: KeyedRandom.Create(8).Service, variant: Contact);
        landing.Spatial.OverlapContactsResult = _ => Touching(landing.Actors.Get(2).Actor.Entity.Value);
        landing.StartSwing(swing, out _);
        Assert.Single(landing.DeliverImpact(swing).OfType<AttackHitFact>());
        Assert.Equal(1, WeaponHits(landing));

        // The lowest draw on every percentile deflects the edged blow whole.
        using Fixture deflected = new(random: RandomMinimum.Create(), variant: Contact);
        deflected.Spatial.OverlapContactsResult = _ => Touching(deflected.Actors.Get(2).Actor.Entity.Value);
        deflected.StartSwing(swing, out _);
        IReadOnlyList<IProductFact> turned = deflected.DeliverImpact(swing);
        Assert.Empty(turned.OfType<AttackHitFact>());
        AttackNegatedFact negated = Assert.Single(turned.OfType<AttackNegatedFact>());
        Assert.Equal((Player, 2L, HarmNegation.Deflected), (negated.AttackerId, negated.TargetId, negated.Negation));
        Assert.Equal(1, WeaponHits(deflected));

        using Fixture whiff = new(random: KeyedRandom.Create(8).Service, variant: Contact);
        whiff.StartSwing(swing, out _);
        whiff.DeliverImpact(swing);
        Assert.Equal(0, WeaponHits(whiff));
    }

    [Fact]
    public void A_swing_aimed_at_an_actor_defeated_before_its_hit_frame_still_reaches_whoever_its_volume_touches_there()
    {
        using Fixture fixture = new(random: KeyedRandom.Create(8).Service, variant: Contact);
        ActorState bystander = fixture.SpawnBrigand(3, new WorldPoint(1f, 0f, .4f));
        AttackRequest swing = new(Player, 2, 3, 5, .125d, Delayed: true);
        fixture.StartSwing(swing, out bool admitted);
        Assert.True(admitted);

        fixture.Actors.Get(2).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0);
        fixture.Spatial.OverlapContactsResult = _ => Touching(bystander.Actor.Entity.Value);

        Assert.Equal(3L, Assert.Single(fixture.DeliverImpact(swing).OfType<AttackHitFact>()).TargetId);
        Assert.Single(fixture.Spatial.OverlapContactsRequests);
    }

    [Fact]
    public void Deflection_negates_an_edged_blow_whole_turns_aside_part_of_a_blunt_one_and_the_material_requirement_still_holds()
    {
        using Fixture fixture = new(variant: Contact);
        DaggerfallSeamContext context = fixture.VariantContext(Contact);
        DaggerfallActorDefinition player = TestPayload.Definitions.RequireActor(new DaggerfallActorId("player"));
        CombatParticipants participants = new(fixture.Actors.Get(2).Actor, fixture.Actors.Player.Actor, "attack");
        DaggerfallPhysicalHarm chest = new(2, Player, player, "long-blade", "iron", 3, 1, 1);

        DaggerfallContactMitigation certain = new(context with { Session = context.Session with { Random = RandomMinimum.Create() } }, Tuning, new FixedGuard());
        NegationEvent edged = new(participants, DaggerfallHarmChannels.Edged);
        certain.Negate(edged, chest);
        Assert.Equal(HarmNegation.Deflected, edged.Negation);
        MitigationEvent blunt = new(participants, DaggerfallHarmChannels.Blunt, 10);
        certain.Mitigate(blunt, chest with { Skill = "blunt-weapon" });
        Assert.Equal(5, blunt.Deflected);
        Assert.Equal(certain.Absorption(chest, DaggerfallHarmChannels.Blunt), blunt.Absorbed);

        DaggerfallContactMitigation never = new(context with { Session = context.Session with { Random = RandomMaximum.Create() } }, Tuning, new FixedGuard());
        NegationEvent landed = new(participants, DaggerfallHarmChannels.Edged);
        never.Negate(landed, chest);
        Assert.Equal(HarmNegation.None, landed.Negation);

        NegationEvent immune = new(participants, DaggerfallHarmChannels.Edged);
        never.Negate(immune, chest with { Target = player with { MinimumMaterial = "silver" } });
        Assert.Equal(HarmNegation.Immune, immune.Negation);
    }

    [Fact]
    public void Worn_armor_enchantments_shift_what_the_players_armor_absorbs_under_the_variant()
    {
        using Fixture fixture = new(variant: Contact);
        DaggerfallContactMitigation mitigation = new(fixture.VariantContext(Contact), Tuning, new FixedGuard());
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
        using Fixture fixture = new(armed: false, random: random.Service, variant: Contact);
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
    public void A_game_under_the_contact_variant_composes_its_owners_and_saves_and_restores()
    {
        DaggerfallSiteProfile inputs = TestSessions.ReadInputs(TestData.RepositoryRoot);
        DaggerfallSessionComposition composition = TestSessions.ContactComposition(inputs);
        List<string> releases = [];
        ContentFake content = new(releases);
        TestSessions.PopulateContent(content, inputs);
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
        Assert.Equal([DaggerfallVariants.Contact], composition.Identity!.Variants);
    }

    [Fact]
    public void Holding_block_raises_a_guard_only_with_a_drawn_weapon_and_no_swing_and_it_costs_stamina_until_the_pool_empties()
    {
        using Fixture fixture = new(variant: Contact);
        DaggerfallContactGuard guard = new(fixture.VariantContext(Contact), Tuning);
        Track stamina = fixture.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));
        stamina.SetCurrent(10d);
        FactBuffer<IProductFact> facts = new();

        guard.Step(.5d, canGuard: true, swinging: false, 1, 1, facts);
        Assert.False(guard.Raised); // Nothing is held yet.
        guard.Observe([TestSessions.Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, intent: "block")]);
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
        guard.Observe([TestSessions.Input(InputEventKind.MappedDigital, InputEdge.Released, x: 0, intent: "block")]);
        guard.Step(.5d, canGuard: true, swinging: false, 1, 10, facts);
        Assert.False(guard.Raised);
        guard.Observe([TestSessions.Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, intent: "block")]);
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
        using Fixture fixture = new(variant: Contact);
        DaggerfallSeamContext context = fixture.VariantContext(Contact);
        FixedGuard guard = new();
        DaggerfallContactMitigation mitigation = new(context, Tuning, guard);
        int lowered = mitigation.DeflectionChance(Player), enemy = mitigation.DeflectionChance(2);
        guard.Raised = true;
        Assert.Equal(Math.Min(lowered + (int)Tuning.GuardDeflectionBonus, (int)Tuning.GuardMaximumChance), mitigation.DeflectionChance(Player));
        Assert.Equal(enemy, mitigation.DeflectionChance(2));
    }

    [Fact]
    public void The_contact_variant_composes_one_guard_its_mitigation_reads()
    {
        using Fixture fixture = new(variant: Contact);
        DaggerfallSeamOwners owners = fixture.Owners!;
        Assert.IsType<DaggerfallContactGuard>(owners.PlayerGuard);
        Assert.Equal([DaggerfallRulesSeams.PhysicalMitigation, DaggerfallRulesSeams.PhysicalDelivery, DaggerfallRulesSeams.PlayerGuard, DaggerfallRulesSeams.TouchDelivery], owners.Seams);
    }

    private sealed class FixedGuard : IDaggerfallPlayerGuard
    {
        public bool Raised { get; set; }
        public bool WantsTime => false;
        public void Observe(ReadOnlySpan<ProductInputEvent> inputs) { }
        public void Step(double seconds, bool canGuard, bool swinging, ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts) { }
        public void Neutralize(ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts) { }
    }

    [Fact]
    public void A_touch_reaches_every_actor_its_volume_contacts_but_none_behind_the_world_and_one_that_meets_no_one_is_still_released()
    {
        using Fixture fixture = new(variant: Contact);
        IDaggerfallTouchDelivery touch = fixture.Owners!.TouchDelivery!(fixture.TouchOperations);
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
        using Fixture fixture = new(variant: Contact);
        IDaggerfallTouchDelivery touch = fixture.Owners!.TouchDelivery!(fixture.TouchOperations);
        DaggerfallTouchCast cast = new(2, new Vector3(1f, 1.62f, 0f), -Vector3.UnitX, Admitted: Player);

        fixture.Spatial.OverlapContactsResult = _ => Touching();
        Assert.Empty(touch.Reach(cast).Targets);
        fixture.Spatial.OverlapContactsResult = _ => Touching(fixture.Actors.Player.Actor.Entity.Value);
        Assert.Equal([Player], touch.Reach(cast).Targets);
    }

    [Fact]
    public void Classic_touch_picks_the_first_actor_a_short_capsule_meets_refuses_when_none_and_trusts_an_enemys_admitted_target()
    {
        using Fixture fixture = new();
        IDaggerfallTouchDelivery touch = DaggerfallSeamSelection.Classic.Build(fixture.SeamInputs).TouchDelivery(fixture.TouchOperations);
        Assert.IsType<DaggerfallClassicTouchDelivery>(touch);
        DaggerfallTouchCast cast = new(Player, new Vector3(0f, 1.65f, 0f), Vector3.UnitX, Admitted: null);

        fixture.Spatial.CapsuleCastHit = _ => default(SpatialHit) with { Present = true, Kind = SpatialHitKind.Entity, Entity = fixture.Actors.Get(2).Actor.Entity.Value };
        DaggerfallTouchReach picked = touch.Reach(cast);
        Assert.True(picked.Releases);
        Assert.Equal([2L], picked.Targets);
        SpatialCapsuleQueryRequest sweep = Assert.Single(fixture.Spatial.CapsuleCastRequests);
        Assert.Equal((.25d, 3f), (sweep.Radius, sweep.Translation.Length()));

        fixture.Spatial.CapsuleCastHit = _ => default;
        Assert.False(touch.Reach(cast).Releases);

        Assert.Equal([Player], touch.Reach(new(2, new Vector3(1f, 1.62f, 0f), -Vector3.UnitX, Admitted: Player)).Targets);
        Assert.Equal(2, fixture.Spatial.CapsuleCastRequests.Count);
        Assert.Empty(fixture.Spatial.OverlapContactsRequests);
    }

    [Fact]
    public void A_player_touch_under_the_contact_variant_that_meets_no_one_is_paid_for_and_misses()
    {
        DaggerfallSiteProfile inputs = TestSessions.ReadInputs(TestData.RepositoryRoot);
        DaggerfallSessionComposition composition = TestSessions.ContactComposition(inputs);
        List<string> releases = [];
        ContentFake content = new(releases);
        TestSessions.PopulateContent(content, inputs);
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

    /// <summary>A contact result touching the given entities, in order.</summary>
    private static SpatialContactResult Touching(params ulong[] entities) => new(
        entities.Select(entity => new SpatialContact(SpatialHitKind.Entity, entity, 0, 0, 0, 0, 0, Vector3.Zero, Vector3.Zero, 0d, .1d, false)).ToArray(),
        false, false, 1, 1);

    /// <summary>The seam context a variant's owners are built from, with the repository's contact tuning.</summary>
    internal static DaggerfallSeamContext Context(DaggerfallRulesVariant variant, DaggerfallSeamInputs inputs)
    {
        byte[] tuning = File.ReadAllBytes(TuningPath);
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(Content(
            ("worldrpg/bundles/test.bundle.json", Encoding.UTF8.GetBytes($$"""{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"daggerfall","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"},"variants":["{{variant.Manifest.Id.Value}}"]}""")),
            ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"daggerfall","role":"daggerfall.base","dependencies":[],"payload":"payload/base.json"}"""u8.ToArray()),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"daggerfall","payload":"payload/tuning.json"}"""u8.ToArray()),
            ("worldrpg/tuning/variant.tuning.json", Encoding.UTF8.GetBytes($$"""{"kind":"worldrpg.tuning-profile","id":"{{variant.Manifest.Tuning.Value}}","ruleset":"daggerfall","payload":"payload/variant.json"}""")),
            ("payload/base.json", "{}"u8.ToArray()), ("payload/tuning.json", "{}"u8.ToArray()), ("payload/variant.json", tuning)),
            new GameBundleId("test.bundle"), [variant.Manifest]).RequireComposition();
        return new DaggerfallSeamContext(inputs, composition.Variants.Single().Tuning);
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
