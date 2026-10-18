using System.Numerics;
using System.Reflection;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
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
    public void The_contact_bundle_selects_the_variant_for_both_physical_seams_and_classic_selects_none()
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
    public void A_player_swing_reaches_every_actor_its_volume_touches_and_a_whiff_reports_nothing_in_reach()
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
        Assert.Contains(new AttackRejectedFact(AttackRejection.NoTargetInReach), fixture.DeliverImpact(whiff));
    }

    [Fact]
    public void Deflection_negates_an_edged_blow_whole_turns_aside_part_of_a_blunt_one_and_the_material_requirement_still_holds()
    {
        using Fixture fixture = new(variant: Contact);
        DaggerfallSeamContext context = fixture.VariantContext(Contact);
        DaggerfallActorDefinition player = TestPayload.Definitions.RequireActor(new DaggerfallActorId("player"));
        CombatParticipants participants = new(fixture.Actors.Get(2).Actor, fixture.Actors.Player.Actor, "attack");
        DaggerfallPhysicalHarm chest = new(2, Player, player, "long-blade", "iron", 3, 1, 1);

        DaggerfallContactMitigation certain = new(context with { Session = context.Session with { Random = RandomMinimum.Create() } }, Tuning);
        NegationEvent edged = new(participants, DaggerfallHarmChannels.Edged);
        certain.Negate(edged, chest);
        Assert.Equal(HarmNegation.Deflected, edged.Negation);
        MitigationEvent blunt = new(participants, DaggerfallHarmChannels.Blunt, 10);
        certain.Mitigate(blunt, chest with { Skill = "blunt-weapon" });
        Assert.Equal(5, blunt.Deflected);
        Assert.Equal(certain.Absorption(chest, DaggerfallHarmChannels.Blunt), blunt.Absorbed);

        DaggerfallContactMitigation never = new(context with { Session = context.Session with { Random = RandomMaximum.Create() } }, Tuning);
        NegationEvent landed = new(participants, DaggerfallHarmChannels.Edged);
        never.Negate(landed, chest);
        Assert.Equal(HarmNegation.None, landed.Negation);

        NegationEvent immune = new(participants, DaggerfallHarmChannels.Edged);
        never.Negate(immune, chest with { Target = player with { MinimumMaterial = "silver" } });
        Assert.Equal(HarmNegation.Immune, immune.Negation);
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
        string root = TestData.RepositoryRoot;
        ResolvedGameComposition resolved = GameCompositionResolver.Resolve(TestSessions.FullContent(root), new GameBundleId("daggerfall.contact"),
            [.. DaggerfallVariants.All.Select(variant => variant.Manifest)]).RequireComposition();
        DaggerfallSiteProfile inputs = TestSessions.ReadInputs(root);
        DaggerfallSessionComposition composition = new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults, resolved.Identity)
        {
            Seams = DaggerfallSeamSelection.From(resolved.Variants, DaggerfallVariants.All),
        };
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
        Assert.Equal([DaggerfallVariants.Contact], resolved.Identity.Variants);
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
