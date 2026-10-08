using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DungeonSpellFlightSessionTests
{
    [Fact]
    public void Dungeon_action_missile_moves_in_fixed_segments_before_common_effect_impact()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DaggerfallDungeonActionGraph graph = session.State.DungeonActions.Single().Value;

        DaggerfallDungeonActionDispatch dispatch = graph.Trigger(fixture.Action.Id, DaggerfallDungeonActionEvent.Direct);
        Assert.Equal(DaggerfallDungeonActionOutcome.AppliedWithoutChange, Assert.Single(dispatch.Executions).Outcome);
        DaggerfallLiveSpell bundle = Assert.Single(session.Casting.PendingDungeonFlights);
        Vector3 launch = bundle.ReleaseOrigin!.Value;
        Assert.True(bundle.Definitions[0].Spell!.UntilHealed);
        Assert.Equal(1f, launch.Y - fixture.Action.SourcePosition!.Value.Y, 5);
        Assert.Equal(Vector3.UnitZ, bundle.ReleaseDirection);

        Vector3 originalTarget = session.State.PlayerControl.Position!.Value.ToVector();
        fixture.Spatial.FloorHit = _ => default;
        fixture.Update(.1d);
        DaggerfallDungeonSpellFlightView first = Assert.Single(session.ReadDungeonSpellFlights());
        Assert.Equal(2.5f, Vector3.Distance(launch, first.Position.ToVector()), 4);
        Assert.Equal(Vector3.UnitZ, first.Direction);

        // A moving target cannot re-aim an admitted missile. Its target collider may move into
        // the later segment, but the launch direction and already-traversed pose stay fixed.
        session.State.PlayerControl.MoveTo(originalTarget + new Vector3(100f, 0f, 0f));
        fixture.Update(.1d);
        DaggerfallDungeonSpellFlightView second = Assert.Single(session.ReadDungeonSpellFlights());
        Assert.Equal(5f, Vector3.Distance(launch, second.Position.ToVector()), 4);
        Assert.Equal(Vector3.UnitZ, second.Direction);

        fixture.Spatial.FloorHit = request => request.Direction.Z > .99f && request.MaxDistance is > 2.49 and < 2.51
            ? new SpatialHit
            {
                Present = true,
                Kind = SpatialHitKind.Entity,
                Entity = session.State.Actors.Player.Actor.Entity.Value,
                Point = request.Origin + request.Direction * (float)request.MaxDistance,
            }
            : default;
        fixture.Update(.1d);

        Assert.True(bundle.Delivered);
        Assert.Empty(session.Casting.PendingDungeonFlights);
        Assert.Contains(bundle.Results, result => result.Outcome == DaggerfallCastOutcome.Applied);
        Assert.NotEmpty(session.State.Effects.Active);
        SpatialRaycastRequest[] flightRays = fixture.Spatial.FloorProbes
            .Where(request => request.Direction.Z > .99f && request.MaxDistance is > 2.49 and < 2.51)
            .ToArray();
        Assert.Equal(3, flightRays.Length);
        Assert.All(flightRays, request => Assert.Equal(Vector3.UnitZ, request.Direction));
    }

    [Fact]
    public void Dungeon_action_missile_consumes_every_admitted_catch_up_step()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DaggerfallDungeonActionGraph graph = session.State.DungeonActions.Single().Value;
        graph.Trigger(fixture.Action.Id, DaggerfallDungeonActionEvent.Direct);
        DaggerfallLiveSpell bundle = Assert.Single(session.Casting.PendingDungeonFlights);
        Vector3 launch = bundle.ReleaseOrigin!.Value;
        fixture.Spatial.FloorHit = _ => default;

        ProductUpdateFacts facts = OuterUpdate(1) with { SimulationStep = 3, AdmittedStepCount = 3 };
        session.Update(new ProductUpdate(facts, []));

        DaggerfallDungeonSpellFlightView flight = Assert.Single(session.ReadDungeonSpellFlights());
        Assert.Equal(1.25f, Vector3.Distance(launch, flight.Position.ToVector()), 4);
        Assert.Equal(3d / 60d, bundle.DungeonFlightElapsedSeconds, 8);
    }

    [Fact]
    public void Dungeon_action_missile_expires_after_admitted_catch_up_lifetime()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DaggerfallDungeonActionGraph graph = session.State.DungeonActions.Single().Value;
        graph.Trigger(fixture.Action.Id, DaggerfallDungeonActionEvent.Direct);
        DaggerfallLiveSpell bundle = Assert.Single(session.Casting.PendingDungeonFlights);
        fixture.Spatial.FloorHit = _ => default;

        for (ulong outer = 1; outer <= 161; outer++)
        {
            ProductUpdateFacts facts = OuterUpdate(outer) with
            {
                SimulationStep = checked(outer * 3),
                AdmittedStepCount = 3,
            };
            session.Update(new ProductUpdate(facts, []));
        }

        Assert.True(bundle.Delivered);
        Assert.Empty(session.Casting.PendingDungeonFlights);
        Assert.Equal(DaggerfallCastOutcome.Missed, Assert.Single(bundle.Results).Outcome);
    }

    [Fact]
    public void Dungeon_action_missile_expires_as_one_common_missed_result_after_its_admitted_lifetime()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DaggerfallDungeonActionGraph graph = session.State.DungeonActions.Single().Value;
        graph.Trigger(fixture.Action.Id, DaggerfallDungeonActionEvent.Direct);
        DaggerfallLiveSpell bundle = Assert.Single(session.Casting.PendingDungeonFlights);
        fixture.Spatial.FloorHit = _ => default;

        for (int step = 0; step < 81; step++) fixture.Update(.1d);

        Assert.True(bundle.Delivered);
        Assert.Empty(session.Casting.PendingDungeonFlights);
        Assert.Equal(DaggerfallCastOutcome.Missed, Assert.Single(bundle.Results).Outcome);
        Assert.Empty(session.State.Effects.Active);
    }

    [Fact]
    public void Dungeon_action_destruction_uses_player_level_power_without_fabricating_a_caster()
    {
        using Fixture fixture = new(soundIndex: 7, useDefaultEffects: true);
        DaggerfallSession session = fixture.Session;
        DaggerfallDungeonActionGraph graph = session.State.DungeonActions.Single().Value;
        DaggerfallDungeonActionDispatch dispatch = graph.Trigger(fixture.Action.Id, DaggerfallDungeonActionEvent.Direct);
        Assert.Equal(DaggerfallDungeonActionOutcome.AppliedWithoutChange, Assert.Single(dispatch.Executions).Outcome);
        DaggerfallLiveSpell bundle = Assert.Single(session.Casting.PendingDungeonFlights);
        double before = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current;

        fixture.Spatial.FloorHit = request => request.Direction.Z > .99f && request.MaxDistance is > 2.49 and < 2.51
            ? new SpatialHit
            {
                Present = true,
                Kind = SpatialHitKind.Entity,
                Entity = session.State.Actors.Player.Actor.Entity.Value,
                Point = request.Origin + request.Direction * (float)request.MaxDistance,
            }
            : default;
        fixture.Update(.1d);

        double after = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current;
        Assert.True(bundle.Delivered);
        Assert.Contains(bundle.Results, result => result.Outcome == DaggerfallCastOutcome.Applied);
        Assert.True(after < before);
        Assert.Empty(session.State.Effects.Active);
    }

    [Fact]
    public void Dungeon_action_missile_reports_immune_target_without_applying_effect()
    {
        using Fixture fixture = new(soundIndex: 50, useDefaultEffects: true);
        DaggerfallSession session = fixture.Session;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.ImmunityParalysis.Value)).BaseValue = 1;
        DaggerfallDungeonActionGraph graph = session.State.DungeonActions.Single().Value;
        DaggerfallDungeonActionDispatch dispatch = graph.Trigger(fixture.Action.Id, DaggerfallDungeonActionEvent.Direct);
        Assert.Equal(DaggerfallDungeonActionOutcome.AppliedWithoutChange, Assert.Single(dispatch.Executions).Outcome);
        DaggerfallLiveSpell bundle = Assert.Single(session.Casting.PendingDungeonFlights);

        fixture.Spatial.FloorHit = request => request.Direction.Z > .99f && request.MaxDistance is > 2.49 and < 2.51
            ? new SpatialHit
            {
                Present = true,
                Kind = SpatialHitKind.Entity,
                Entity = session.State.Actors.Player.Actor.Entity.Value,
                Point = request.Origin + request.Direction * (float)request.MaxDistance,
            }
            : default;
        fixture.Update(.1d);

        Assert.True(bundle.Delivered);
        Assert.Equal(DaggerfallCastOutcome.Immune, Assert.Single(bundle.Results).Outcome);
        Assert.Empty(session.State.Effects.Active);
    }

    [Fact]
    public void Dungeon_action_missile_reports_removed_target_without_applying_effect()
    {
        using Fixture fixture = new(soundIndex: 7, useDefaultEffects: true);
        DaggerfallSession session = fixture.Session;
        DaggerfallDungeonActionGraph graph = session.State.DungeonActions.Single().Value;
        graph.Trigger(fixture.Action.Id, DaggerfallDungeonActionEvent.Direct);
        DaggerfallLiveSpell bundle = Assert.Single(session.Casting.PendingDungeonFlights);
        session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).SetCurrent(0, clamp: true);

        fixture.Spatial.FloorHit = request => request.Direction.Z > .99f && request.MaxDistance is > 2.49 and < 2.51
            ? new SpatialHit
            {
                Present = true,
                Kind = SpatialHitKind.Entity,
                Entity = session.State.Actors.Player.Actor.Entity.Value,
                Point = request.Origin + request.Direction * (float)request.MaxDistance,
            }
            : default;
        fixture.Update(.1d);

        Assert.True(bundle.Delivered);
        Assert.Equal(DaggerfallCastOutcome.TargetUnavailable, Assert.Single(bundle.Results).Outcome);
        Assert.Empty(session.State.Effects.Active);
    }

    [Fact]
    public void Dungeon_action_area_around_caster_keeps_donor_target_mode_and_terminates_as_miss()
    {
        // Identity 25 (spell.024) is the donor's AreaAroundCaster Fire Storm. DFU leaves this target mode on
        // the actorless missile, so the product must not reinterpret it as an area-at-impact cast
        // or leave it travelling forever without a caster-owned center.
        using Fixture fixture = new(soundIndex: 25, useDefaultEffects: true);
        DaggerfallSession session = fixture.Session;
        DaggerfallDungeonActionGraph graph = session.State.DungeonActions.Single().Value;
        DaggerfallDungeonActionDispatch dispatch = graph.Trigger(fixture.Action.Id, DaggerfallDungeonActionEvent.Direct);
        Assert.Equal(DaggerfallDungeonActionOutcome.AppliedWithoutChange, Assert.Single(dispatch.Executions).Outcome);
        DaggerfallLiveSpell bundle = Assert.Single(session.Casting.PendingDungeonFlights);
        Assert.Equal(DaggerfallSpellTarget.AreaAroundCaster, bundle.Target);

        fixture.Spatial.FloorHit = _ => default;
        fixture.Update(.1d);

        Assert.True(bundle.Delivered);
        Assert.Equal(DaggerfallCastOutcome.Missed, Assert.Single(bundle.Results).Outcome);
        Assert.Empty(session.Casting.PendingDungeonFlights);
    }

    [Fact]
    public void Dungeon_action_missile_is_cleared_at_save_and_never_restored_as_transient_state()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DaggerfallDungeonActionGraph graph = session.State.DungeonActions.Single().Value;
        graph.Trigger(fixture.Action.Id, DaggerfallDungeonActionEvent.Direct);
        DaggerfallLiveSpell bundle = Assert.Single(session.Casting.PendingDungeonFlights);

        session.Casting.ClearTransient();
        Assert.True(bundle.Delivered);
        Assert.Empty(session.Casting.PendingDungeonFlights);
        RulesetSavePayload save = session.CaptureSave();

        using DaggerfallSession restored = fixture.Restore(save);
        Assert.Empty(restored.Casting.PendingDungeonFlights);
        DaggerfallWorldProfileKey profile = restored.State.DungeonActions.Keys.Single();
        Assert.Equal(1UL, restored.State.DungeonActions[profile].State[fixture.Action.Id].ActivationCount);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition composition;
        private ulong step;
        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; }
        internal DaggerfallDungeonActionDefinition Action { get; }

        internal Fixture(byte soundIndex = 28, bool useDefaultEffects = false)
        {
            string root = TestData.RepositoryRoot;
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallSiteProfile source = ReadInputs(root);
            Vector3 player = source.Project.PlayerPosition?.ToVector()
                ?? throw new InvalidOperationException("The session fixture needs an authored player position.");
            Action = new(
                "action/test-dungeon-spell-flight",
                SourceOffset: 60_001,
                TriggerFlag: (uint)DaggerfallDungeonTriggerFlag.Direct,
                ActionFlag: (byte)DaggerfallDungeonActionFlag.CastSpell,
                Axis: 0,
                Duration: 0,
                Magnitude: 0,
                NextObjectOffset: -1,
                NextActionId: null,
                IsFlat: true,
                SoundIndex: soundIndex,
                SourcePosition: player - Vector3.UnitZ * 60f,
                RawIndex: soundIndex);
            DaggerfallSiteProfile inputs = WithAction(source, Action);
            ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(
                FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
            DaggerfallSessionComposition selected = new(definitions, inputs, DaggerfallTuning.Defaults, identity);
            composition = useDefaultEffects
                ? selected
                : selected with { Effects = EffectsFor(definitions.Magic.Spells["spell.027"]) };

            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            Spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
            EngineContextFake engine = EngineContextFake.Create(content, Spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service,
                random: RandomMaximum.Create());
            Session = DaggerfallSession.StartNew(engine.Context, composition);
        }

        internal void Update(double fixedDeltaSeconds)
        {
            Session.Update(new ProductUpdate(OuterUpdate(++step) with { FixedDeltaSeconds = fixedDeltaSeconds }, []));
        }

        internal DaggerfallSession Restore(RulesetSavePayload save)
        {
            string root = TestData.RepositoryRoot;
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, composition.StartSite);
            SpatialFake spatial = SpatialFake.Create(composition.StartSite.SpatialArtifact.Sha256, releases);
            EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service,
                random: RandomMaximum.Create());
            return DaggerfallSession.Restore(engine.Context, composition, save);
        }

        public void Dispose() => Session.Dispose();

        private static DaggerfallSiteProfile WithAction(DaggerfallSiteProfile source, DaggerfallDungeonActionDefinition action) => new(
            source.Project,
            source.SpatialArtifact,
            source.StaticMesh,
            source.WorldAppearance,
            source.InitialLook,
            source.Materials,
            source.ActorSprites,
            source.MobileSprites,
            source.Audio,
            source.ClassicPresentation,
            source.Site,
            source.Doors,
            source.ProfileKind,
            source.ProfileKey.LogicalId,
            source.Portals,
            source.Anchors.Values.ToArray(),
            source.Lights,
            source.GroundContainerSprite,
            source.DungeonMap,
            [.. source.DungeonActions, action],
            source.DungeonActionModels,
            source.InteriorBuilding,
            source.Music,
            source.QuestMarkers,
            source.BillboardSprites);

        private static DaggerfallEffectCatalog EffectsFor(DaggerfallSpellDefinition spell)
        {
            IEnumerable<IActiveEffectContribution> Apply(DaggerfallActiveEffect effect)
            {
                DaggerfallCastEffectState state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)!;
                Stat maximum = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
                var handle = maximum.AddModifier(Math.Max(1, state.Amount));
                return [new DelegateActiveEffectContribution(() => maximum.RemoveModifier(handle))];
            }

            DaggerfallEffectDefinition[] definitions = spell.Effects.DistinctBy(effect => (effect.Type, effect.SubType))
                .Select(effect => new DaggerfallEffectDefinition(
                    $"compiled-dungeon-{effect.Type}-{effect.SubType}",
                    $"compiled-dungeon-{effect.Type}-{effect.SubType}",
                    DaggerfallEffectStacking.Stack,
                    20,
                    1,
                    Apply,
                    Resume: Apply,
                    Feedback: DaggerfallEffectFeedback.MagicSparkle,
                    Spell: new(effect.Type, effect.SubType, SupportsDuration: true, SupportsMagnitude: true, UntilHealed: true)))
                .ToArray();
            return new(definitions);
        }
    }
}
