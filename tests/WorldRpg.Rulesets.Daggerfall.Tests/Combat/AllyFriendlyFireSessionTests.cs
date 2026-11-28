using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// #11202: allied fighters swinging, shooting, or casting beside each other at one foe do not damage each other,
/// the player, or peaceful civilians, and same-side hostiles flanking the player do not damage each other.
/// </summary>
public sealed class AllyFriendlyFireSessionTests
{
    private const long Player = DaggerfallActorIdentity.PlayerEntityId;

    [Fact]
    public void Two_allies_flanking_one_foe_do_not_damage_each_other_the_player_or_a_civilian_while_striking_the_foe()
    {
        using Stage stage = new(random: RandomMaximum.Create());
        Vector3 playerPos = stage.Session.State.PlayerControl.Position!.Value.ToVector();
        Vector3 foePos = playerPos + new Vector3(0f, 0f, -1.8f);
        Vector3 allyAPos = playerPos + new Vector3(-0.35f, 0f, -0.3f);
        Vector3 allyBPos = playerPos + new Vector3(0.35f, 0f, -0.3f);

        long allyA = stage.SpawnAlly("orc", allyAPos, face: foePos);
        long allyB = stage.SpawnAlly("orc", allyBPos, face: foePos);
        long foe = stage.SpawnHostile("centaur", foePos, face: allyAPos);
        long civilian = stage.SpawnCivilian(playerPos + new Vector3(0f, 0f, -1.0f));

        stage.PrepCombatant(allyA, hp: 1000d, stat: 100);
        stage.PrepCombatant(allyB, hp: 1000d, stat: 100);
        stage.PrepCombatant(foe, hp: 1000d, stat: 20);
        stage.SetHealth(civilian, 1000d);
        stage.SetHealth(Player, 1000d);

        // Both allies see the foe at melee range; the foe is kept passive so only ally swings execute.
        stage.Perception.Responder = request => Receipt(
            [.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray().Select(target =>
            {
                bool allySeesFoe = (observer.Entity == checked((ulong)allyA) || observer.Entity == checked((ulong)allyB))
                    && target.Entity == checked((ulong)foe);
                return new PerceptionPair(observer.Entity, target.Entity, 1.45d, allySeesFoe ? 1d : 0d,
                    allySeesFoe ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, allySeesFoe ? 1d : 0d);
            }))]);

        // Every collider ActorContact offers to the swing volume is returned as overlapping.
        stage.Spatial.OverlapContactsResult = request => Touching(
            request.Start.Translation,
            [.. request.Entities.ToArray().Select(collider => collider.Entity)]);

        ulong[] attackMarkers = stage.AttackMarkers("orc");
        int swingsBefore = stage.Spatial.OverlapContactsRequests.Count;
        bool allyASwung = false, allyBSwung = false;
        for (int step = 0; step < 180 && (!allyASwung || !allyBSwung || stage.Health(foe) >= 950d); step++)
        {
            if (attackMarkers.Length > 0)
                stage.Appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: attackMarkers[step % attackMarkers.Length]);
            stage.Step();
            if (stage.Session.EnemyBehavior.LastEvidence.TryGetValue(allyA, out var evA) && evA.State == EnemyBehaviorState.Attack)
                allyASwung = true;
            if (stage.Session.EnemyBehavior.LastEvidence.TryGetValue(allyB, out var evB) && evB.State == EnemyBehaviorState.Attack)
                allyBSwung = true;
        }

        Assert.True(allyASwung && allyBSwung,
            $"Both flanking allies must enter melee attack against the foe (A={stage.StateOf(allyA)}, B={stage.StateOf(allyB)}).");
        Assert.True(stage.Spatial.OverlapContactsRequests.Count > swingsBefore, "Allied swings must query swing contacts.");
        Assert.True(stage.Health(foe) < 1000d, $"Allies must damage the flanked foe (hp={stage.Health(foe)}).");

        // Neither ally, nor the player, nor the civilian took friendly-fire damage.
        Assert.Equal(1000d, stage.Health(allyA));
        Assert.Equal(1000d, stage.Health(allyB));
        Assert.Equal(1000d, stage.Health(Player));
        Assert.Equal(1000d, stage.Health(civilian));

        ulong allyABody = stage.Body(allyA), allyBBody = stage.Body(allyB), playerBody = stage.Body(Player), civBody = stage.Body(civilian);
        foreach (SpatialContactQueryRequest query in stage.Spatial.OverlapContactsRequests.Skip(swingsBefore))
        {
            ulong[] offered = [.. query.Entities.ToArray().Select(collider => collider.Entity)];
            Assert.DoesNotContain(allyABody, offered);
            Assert.DoesNotContain(allyBBody, offered);
            Assert.DoesNotContain(playerBody, offered);
            Assert.DoesNotContain(civBody, offered);
        }
    }

    [Fact]
    public void An_allied_archers_arrow_passes_through_an_ally_and_the_player_to_hit_a_foe()
    {
        using Stage stage = new(random: RandomMaximum.Create());
        Vector3 playerPos = stage.Session.State.PlayerControl.Position!.Value.ToVector();
        double separation = TestPayload.Definitions.EnemyRanged.MinimumDistance + 2d;
        Vector3 foePos = playerPos + new Vector3(0f, 0f, -(float)separation);
        Vector3 archerPos = playerPos + new Vector3(0f, 0f, 2f);

        // Use the placed hold archer turned player-ally so its authored ranged release marker matches directly.
        long archer = HoldActor(5);
        ActorState archerActor = stage.Session.State.Actors.Get(archer);
        archerActor.ApplyPose(new ActorPose(WorldPoint.From(archerPos), ActorHeading.Yaw(foePos - archerPos)));
        archerActor.Actor.Get<FactionComponent>().SetTeamOverride("player-ally");

        long allyInFront = stage.SpawnAlly("orc", playerPos + new Vector3(0f, 0f, -1f), face: foePos);
        long foe = stage.SpawnHostile("orc", foePos, face: archerPos);

        stage.PrepCombatant(archer, hp: 1000d, stat: 100);
        stage.PrepCombatant(allyInFront, hp: 1000d, stat: 50);
        stage.PrepCombatant(foe, hp: 1000d, stat: 20);
        stage.SetHealth(Player, 1000d);

        stage.Perception.Responder = request => Receipt(
            [.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray().Select(target =>
            {
                bool archerSeesFoe = observer.Entity == checked((ulong)archer) && target.Entity == checked((ulong)foe);
                return new PerceptionPair(observer.Entity, target.Entity, separation, archerSeesFoe ? 1d : 0d,
                    archerSeesFoe ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, archerSeesFoe ? 1d : 0d);
            }))]);

        ulong allyFrontBody = stage.Body(allyInFront), playerBody = stage.Body(Player), foeBody = stage.Body(foe);
        // Clear line of fire at release, then an ally and the player cross the arrow's flight path.
        stage.SetRayBodies(foeBody);

        ulong rangedMarker = AuthoredMarker(archer, "rangedFrames");
        stage.Appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: rangedMarker);
        stage.Step();
        stage.Appearance.AdvanceReceiptForAll = null;

        int probesBefore = stage.Spatial.FloorProbes.Count;
        stage.SetRayBodies(allyFrontBody, playerBody, foeBody);
        for (int step = 0; step < 240 && stage.Health(foe) == 1000d; step++)
            stage.Step();

        Assert.True(stage.Health(foe) < 1000d,
            $"Allied archer's arrow must pass through allies and hit the foe (hp={stage.Health(foe)}, state={stage.StateOf(archer)}, probes={stage.Spatial.FloorProbes.Count - probesBefore}).");
        Assert.Equal(1000d, stage.Health(allyInFront));
        Assert.Equal(1000d, stage.Health(Player));

        ulong[] offeredOnLevelRays = [.. stage.Spatial.FloorProbes.Skip(probesBefore)
            .Where(probe => MathF.Abs(probe.Direction.Y) < .5f)
            .SelectMany(probe => probe.Entities.ToArray())
            .Select(collider => collider.Entity)];
        Assert.DoesNotContain(allyFrontBody, offeredOnLevelRays);
        Assert.DoesNotContain(playerBody, offeredOnLevelRays);
    }

    [Fact]
    public void Two_same_team_hostiles_flanking_the_player_do_not_damage_each_other()
    {
        using Stage stage = new(random: RandomMaximum.Create());
        Vector3 playerPos = stage.Session.State.PlayerControl.Position!.Value.ToVector();

        long orcA = stage.SpawnHostile("orc", playerPos + new Vector3(-0.35f, 0f, -1.75f), face: playerPos);
        long orcB = stage.SpawnHostile("orc", playerPos + new Vector3(0.35f, 0f, -1.75f), face: playerPos);
        stage.PrepCombatant(orcA, hp: 1000d, stat: 100);
        stage.PrepCombatant(orcB, hp: 1000d, stat: 100);
        stage.SetHealth(Player, 1000d);

        stage.Perception.Responder = request => Receipt(
            [.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray().Select(target =>
            {
                bool orcSeesPlayer = (observer.Entity == checked((ulong)orcA) || observer.Entity == checked((ulong)orcB))
                    && target.Entity == checked((ulong)Player);
                return new PerceptionPair(observer.Entity, target.Entity, 1.45d, orcSeesPlayer ? 1d : 0d,
                    orcSeesPlayer ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, orcSeesPlayer ? 1d : 0d);
            }))]);

        stage.Spatial.OverlapContactsResult = request => Touching(
            request.Start.Translation,
            [.. request.Entities.ToArray().Select(collider => collider.Entity)]);

        ulong[] attackMarkers = stage.AttackMarkers("orc");
        int swingsBefore = stage.Spatial.OverlapContactsRequests.Count;
        bool orcASwung = false, orcBSwung = false;
        for (int step = 0; step < 180 && (!orcASwung || !orcBSwung || stage.Health(Player) == 1000d); step++)
        {
            if (attackMarkers.Length > 0)
                stage.Appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: attackMarkers[step % attackMarkers.Length]);
            stage.Step();
            if (stage.Session.EnemyBehavior.LastEvidence.TryGetValue(orcA, out var evA) && evA.State == EnemyBehaviorState.Attack)
                orcASwung = true;
            if (stage.Session.EnemyBehavior.LastEvidence.TryGetValue(orcB, out var evB) && evB.State == EnemyBehaviorState.Attack)
                orcBSwung = true;
        }

        Assert.True(orcASwung && orcBSwung,
            $"Both flanking orcs must attack the player (A={stage.StateOf(orcA)}, B={stage.StateOf(orcB)}).");
        Assert.True(stage.Health(Player) < 1000d, $"Hostile orcs must damage the player (hp={stage.Health(Player)}).");
        Assert.Equal(1000d, stage.Health(orcA));
        Assert.Equal(1000d, stage.Health(orcB));

        ulong orcABody = stage.Body(orcA), orcBBody = stage.Body(orcB);
        foreach (SpatialContactQueryRequest query in stage.Spatial.OverlapContactsRequests.Skip(swingsBefore))
        {
            ulong[] offered = [.. query.Entities.ToArray().Select(collider => collider.Entity)];
            Assert.DoesNotContain(orcABody, offered);
            Assert.DoesNotContain(orcBBody, offered);
        }
    }

    [Fact]
    public void Allied_ranged_touch_and_area_harmful_spells_pass_allies_and_the_player_while_hitting_a_foe()
    {
        // 1. Allied ranged spell (imp casting spell.007) flies past an ally and the player to strike the foe.
        using (Stage stage = new())
        {
            Vector3 playerPos = stage.Session.State.PlayerControl.Position!.Value.ToVector();
            Vector3 foePos = playerPos + new Vector3(0f, 0f, -10f);
            long allyImp = stage.SpawnAlly("imp", playerPos + new Vector3(0f, 0f, 2f), face: foePos);
            long allyPartner = stage.SpawnAlly("orc", playerPos + new Vector3(0f, 0f, -1f), face: foePos);
            long foe = stage.SpawnHostile("orc", foePos, face: playerPos);

            stage.Session.State.Actors.Get(allyImp).Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value)).SetCurrent(100, clamp: true);
            stage.SetHealth(allyPartner, 1000d);
            stage.SetHealth(Player, 1000d);
            stage.SetHealth(foe, 1000d);

            stage.Perception.Responder = request => Receipt(
                [.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray().Select(target =>
                {
                    bool impSeesFoe = observer.Entity == checked((ulong)allyImp) && target.Entity == checked((ulong)foe);
                    return new PerceptionPair(observer.Entity, target.Entity, 10d, impSeesFoe ? 1d : 0d,
                        impSeesFoe ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, impSeesFoe ? 1d : 0d);
                }))]);

            ulong partnerBody = stage.Body(allyPartner), playerBody = stage.Body(Player), foeBody = stage.Body(foe);
            stage.SetRayBodies();

            int probesBefore = stage.Spatial.FloorProbes.Count;
            stage.Step();
            Assert.True(stage.Session.LastEnemySpell.TryGetValue(allyImp, out DaggerfallEnemySpellEvidence? evidence));
            Assert.Equal(DaggerfallCastOutcome.Released, evidence!.Outcome);
            DaggerfallLiveSpell flight = Assert.Single(stage.Session.Casting.PendingRangedFlights, f => f.CasterId == allyImp);

            stage.SetRayBodies(partnerBody, playerBody, foeBody);
            for (int step = 0; step < 60 && !flight.Delivered; step++)
                stage.Step();

            Assert.True(flight.Delivered, "Allied ranged spell must reach and deliver to the foe.");
            Assert.Contains(flight.Results, result => result.TargetId == foe);
            Assert.DoesNotContain(flight.Results, result => result.TargetId is var id && (id == allyPartner || id == Player));
            Assert.Equal(1000d, stage.Health(allyPartner));
            Assert.Equal(1000d, stage.Health(Player));

            ulong[] offered = [.. stage.Spatial.FloorProbes.Skip(probesBefore)
                .Where(probe => MathF.Abs(probe.Direction.Y) < .5f)
                .SelectMany(probe => probe.Entities.ToArray())
                .Select(collider => collider.Entity)];
            Assert.DoesNotContain(partnerBody, offered);
            Assert.DoesNotContain(playerBody, offered);
        }

        // 2. Allied touch spell (ghost casting spell.033) ignores overlapping allies and the player,
        //    and an allied AreaAroundCaster harmful spell excludes allies and the player while hitting the foe.
        using (Stage stage = new())
        {
            Vector3 playerPos = stage.Session.State.PlayerControl.Position!.Value.ToVector();
            Vector3 foePos = playerPos + new Vector3(0f, 0f, -1.6f);
            long allyGhost = stage.SpawnAlly("ghost", playerPos + new Vector3(0f, 0f, -0.8f), face: foePos);
            long allyPartner = stage.SpawnAlly("orc", playerPos + new Vector3(0.4f, 0f, -0.8f), face: foePos);
            long foe = stage.SpawnHostile("orc", foePos, face: playerPos);

            stage.Session.State.Actors.Get(allyGhost).Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value)).SetCurrent(1000, clamp: true);
            stage.SetHealth(allyPartner, 1000d);
            stage.SetHealth(Player, 1000d);
            stage.SetHealth(foe, 1000d);

            stage.Perception.Responder = request => Receipt(
                [.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray().Select(target =>
                {
                    bool ghostSeesFoe = observer.Entity == checked((ulong)allyGhost) && target.Entity == checked((ulong)foe);
                    return new PerceptionPair(observer.Entity, target.Entity, 0.8d, ghostSeesFoe ? 1d : 0d,
                        ghostSeesFoe ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, ghostSeesFoe ? 1d : 0d);
                }))]);

            // Only allies and the player overlap the touch volume -> spell misses harmlessly.
            ulong partnerBody = stage.Body(allyPartner), playerBody = stage.Body(Player);
            stage.Spatial.OverlapContactsResult = request => Touching(request.Start.Translation, [partnerBody, playerBody]);
            stage.Step();

            Assert.True(stage.Session.LastEnemySpell.TryGetValue(allyGhost, out DaggerfallEnemySpellEvidence? missed));
            Assert.Equal(DaggerfallCastOutcome.Missed, missed!.Outcome);
            Assert.Equal(1000d, stage.Health(allyPartner));
            Assert.Equal(1000d, stage.Health(Player));
            Assert.Equal(1000d, stage.Health(foe));

            // Harmful AreaAroundCaster over the session's casting/target owners and StrikeAim.PassesThrough excludes allies and player.
            DaggerfallEnemySpellRelease enemyRelease = new(stage.Session.Casting, stage.Session.SpellTargets,
                _ => new(Releases: true, Targets: [allyPartner, Player, foe]), null!, null!, () => default,
                harmPassesThrough: (caster, target) => stage.Session.StrikeAim.PassesThrough(caster, target, harmsCivilians: false));
            string harmfulArea = TestPayload.Definitions.Magic.Spells.Values
                .OrderBy(spell => spell.Key, StringComparer.Ordinal)
                .First(spell => DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType) == DaggerfallSpellTarget.AreaAroundCaster
                    && spell.Effects.Any(DaggerfallMagicHarm.Harms)).Key;
            stage.Perception.Responder = request => Receipt(
                [.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray()
                    .Select(target => new PerceptionPair(observer.Entity, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d)))]);
            WorldPoint casterPoint = stage.Session.State.Actors.Get(allyGhost).Position;
            int areaQueries = stage.Perception.Requests.Count;
            DaggerfallCastResult areaCast = enemyRelease.Execute(new(allyGhost, harmfulArea, DaggerfallSpellTarget.AreaAroundCaster,
                casterPoint, WorldPoint.From(foePos), -Vector3.UnitZ, foe));

            Assert.NotNull(areaCast.Bundle);
            Assert.Contains(areaCast.Bundle!.Results, result => result.TargetId == foe);
            Assert.DoesNotContain(areaCast.Bundle.Results, result => result.TargetId is var id && (id == allyPartner || id == Player));
            PerceptionTarget[] areaTargets = [.. stage.Perception.Requests.Skip(areaQueries).SelectMany(r => r.Targets.ToArray())];
            Assert.DoesNotContain(areaTargets, t => t.Entity == checked((ulong)allyPartner) || t.Entity == checked((ulong)Player));
            Assert.Contains(areaTargets, t => t.Entity == checked((ulong)foe));
        }
    }

    private sealed class Stage : IDisposable
    {
        private readonly List<string> _releases = [];
        private ulong _step = 1;
        private ulong[] _rayBodies = [];

        internal Stage(IRandomService? random = null)
        {
            string root = TestData.RepositoryRoot;
            Inputs = ReadInputs(root);
            ContentFake content = new(_releases);
            PopulateContent(content, Inputs);
            Spatial = SpatialFake.Create(Inputs.Geometry.Spatial[0].Sha256, _releases);
            Spatial.KeepPosition = true;
            Spatial.NavigationResult = request =>
            {
                Vector3 delta = request.Target - request.From;
                float dist = delta.Length();
                float step = MathF.Min(0.25f, dist);
                Vector3 next = dist <= 1e-5f ? request.Target : request.From + Vector3.Normalize(delta) * step;
                return new NavigationStepResult(ReadOnlyMemory<PlanarNavCell>.Empty, ReadOnlyMemory<NavigationPathEdge>.Empty,
                    NavigationPathOutcome.Reached, next, default, default, dist, 1, 1, 1, 1, 0, false, default, default);
            };
            Perception = PerceptionFake.Create();
            Appearance = new AppearanceFake(_releases);
            EngineContextFake engine = EngineContextFake.Create(content, Spatial.Service, Appearance, Perception.Service,
                random: random);
            Session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, Inputs, DaggerfallTuning.Defaults));
            Spatial.FloorHit = request => MathF.Abs(request.Direction.Y) >= .5f ? default : FirstOffered(request);
        }

        internal DaggerfallSiteProfile Inputs { get; }
        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; }
        internal PerceptionFake Perception { get; }
        internal AppearanceFake Appearance { get; }

        internal long SpawnAlly(string definitionId, Vector3 position, Vector3 face)
        {
            long id = Session.SpawnActor(definitionId, new ActorPose(WorldPoint.From(position), ActorHeading.Yaw(face - position)), level: 5);
            Session.State.Actors.Get(id).Actor.Get<FactionComponent>().SetTeamOverride("player-ally");
            return id;
        }

        internal long SpawnHostile(string definitionId, Vector3 position, Vector3 face) =>
            Session.SpawnActor(definitionId, new ActorPose(WorldPoint.From(position), ActorHeading.Yaw(face - position)), level: 5);

        internal long SpawnCivilian(Vector3 position)
        {
            DaggerfallSiteRecord site = Session.Site.ActiveSite!;
            long id = Session.State.Npcs.RegisterCivilian(new(site.Id.Region, site.Name, string.Empty), new("breton", "Female", 0, 0, 0, 0), "civilian", ["talk"]);
            Session.Civilians.Materialize(id, new ActorPose(WorldPoint.From(position), 0f));
            Session.State.Npcs.Bind(id, Session.Sites.ActiveProfile);
            return id;
        }

        internal ulong[] AttackMarkers(string actorId)
        {
            int mobile = TestPayload.Definitions.RequireActor(new(actorId)).MobileId!.Value;
            NormalizedActorSprite sprite = Inputs.MobileSprites[mobile];
            return [.. sprite.AttackSequences
                .Select(seq => seq.SourceFrames.ToList().IndexOf(-1))
                .Where(idx => idx >= 0)
                .Select(idx => checked((ulong)idx + 1))
                .Distinct()];
        }

        internal string StateOf(long id) =>
            Session.EnemyBehavior.LastEvidence.TryGetValue(id, out var ev) ? $"{ev.State}->{ev.Target?.DurableId}" : "none";

        internal void PrepCombatant(long id, double hp, double stat)
        {
            SetHealth(id, hp);
            ActorState actor = Session.State.Actors.Get(id);
            actor.Stats.GetStat(StatId.Parse("strength")).BaseValue = stat;
            actor.Stats.GetStat(StatId.Parse("agility")).BaseValue = stat;
        }

        internal void SetHealth(long id, double hp)
        {
            StatsComponent stats = id == Player ? Session.State.Actors.Player.Stats : Session.State.Actors.Get(id).Stats;
            Track health = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
            health.Maximum.BaseValue = hp;
            health.SetCurrent(hp, clamp: true);
        }

        internal double Health(long id)
        {
            StatsComponent stats = id == Player ? Session.State.Actors.Player.Stats : Session.State.Actors.Get(id).Stats;
            return stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current;
        }

        internal ulong Body(long id) => (id == Player ? Session.State.Actors.Player.Actor : Session.State.Actors.Get(id).Actor).Entity.Value;

        internal void SetRayBodies(params ulong[] bodies) => _rayBodies = bodies;

        internal void Step() => Session.Update(new ProductUpdate(OuterUpdate(_step++), []));

        public void Dispose() => Session.Dispose();

        private SpatialHit FirstOffered(SpatialRaycastRequest request)
        {
            ulong[] offered = [.. request.Entities.ToArray().Select(body => body.Entity)];
            foreach (ulong body in _rayBodies)
                if (offered.Contains(body))
                    return new SpatialHit(true, SpatialHitKind.Entity, body, 0, 0, 0, 0, 0, 0, default,
                        request.Origin + request.Direction * .1f, -request.Direction, .1d, 0d, 0d, false, true);
            return default;
        }
    }
}
