using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A player's attack decides as it starts whether it can harm civilians, by whether the view meets one. Rays here answer
/// with the first body of <see cref="Stage.Bodies"/> the query still offers, so a body an attack passes through isn't met.
/// </summary>
public sealed class CivilianAimSessionTests
{
    private const long Enemy = 2000;
    private const long Player = DaggerfallActorIdentity.PlayerEntityId;

    [Fact]
    public void A_cut_aimed_at_an_enemy_goes_through_a_civilian_in_its_path()
    {
        using Stage stage = new();
        stage.Bodies = [stage.EnemyBody];
        stage.Tap();

        stage.Bodies = [stage.CivilianBody, stage.EnemyBody];
        stage.Run(40);

        Assert.Equal(1000d, stage.Health(stage.Civilian));
        Assert.True(stage.Health(Enemy) < 1000d);
    }

    [Fact]
    public void A_cut_aimed_at_a_civilian_harms_them()
    {
        using Stage stage = new();
        stage.Bodies = [stage.CivilianBody, stage.EnemyBody];
        stage.Tap();

        stage.Run(40);

        Assert.True(stage.Health(stage.Civilian) < 1000d);
    }

    [Fact]
    public void An_enemy_in_front_of_a_civilian_is_what_the_view_meets()
    {
        using Stage stage = new();
        stage.Bodies = [stage.EnemyBody, stage.CivilianBody];
        stage.Tap();

        stage.Bodies = [stage.CivilianBody];
        stage.Run(40);

        Assert.Equal(1000d, stage.Health(stage.Civilian));
    }

    [Fact]
    public void A_missile_aimed_past_a_civilian_flies_through_them()
    {
        using Stage stage = new();
        stage.Ready(DaggerfallSpellTarget.SingleTargetAtRange);
        stage.Bodies = [stage.EnemyBody];
        stage.Cast();
        stage.Run(2);

        stage.Bodies = [stage.CivilianBody, stage.EnemyBody];
        int probes = stage.Spatial.FloorProbes.Count;
        stage.Run(40);

        Assert.DoesNotContain(stage.Spatial.FloorProbes.Skip(probes).SelectMany(probe => probe.Entities.ToArray()), body => body.Entity == stage.CivilianBody);
        Assert.Equal(1000d, stage.Health(stage.Civilian));
    }

    [Fact]
    public void A_missile_aimed_at_a_civilian_who_then_moves_away_still_harms_civilians()
    {
        using Stage stage = new();
        stage.Ready(DaggerfallSpellTarget.SingleTargetAtRange);
        stage.Bodies = [stage.CivilianBody];
        stage.Cast();
        stage.Run(2);

        stage.Bodies = [];
        stage.RunUntilReleased();

        Assert.True(stage.Session.Casting.PendingRangedFlights.Single().HarmsCivilians);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_area_around_the_caster_takes_in_civilians_only_when_the_cast_was_aimed_at_one(bool aimed)
    {
        using Stage stage = new();
        stage.Ready(DaggerfallSpellTarget.AreaAroundCaster);
        stage.Session.ReadyPlayerSpell(stage.Spell);
        int queries = stage.Perception.Requests.Count;

        stage.Session.ReleaseReadySpell(Player, -Vector3.UnitZ, harmsCivilians: aimed);

        PerceptionTarget[] candidates = stage.Perception.Requests.Skip(queries).SelectMany(request => request.Targets.ToArray()).ToArray();
        Assert.Equal(aimed, candidates.Any(target => target.Entity == checked((ulong)stage.Civilian)));
        Assert.Contains(candidates, target => target.Entity == checked((ulong)Enemy));
    }

    [Fact]
    public void A_touch_only_reaches_a_civilian_when_the_cast_was_aimed_at_one()
    {
        using Stage stage = new();
        stage.Ready(DaggerfallSpellTarget.ByTouch);
        stage.Spatial.OverlapContactsResult = _ => Touching(stage.CivilianBody);
        stage.Session.ReadyPlayerSpell(stage.Spell);

        Assert.Equal(DaggerfallCastOutcome.Missed, stage.Session.ReleaseReadySpell(Player, -Vector3.UnitZ).Outcome);

        stage.Session.ReadyPlayerSpell(stage.Spell);
        Assert.NotEqual(DaggerfallCastOutcome.Missed, stage.Session.ReleaseReadySpell(Player, -Vector3.UnitZ, harmsCivilians: true).Outcome);
    }

    [Fact]
    public void A_helpful_touch_reaches_a_civilian_it_wasnt_aimed_at()
    {
        using Stage stage = new();
        stage.Ready(DaggerfallSpellTarget.ByTouch, harmful: false);
        stage.Spatial.OverlapContactsResult = _ => Touching(stage.CivilianBody);
        stage.Session.ReadyPlayerSpell(stage.Spell);

        Assert.NotEqual(DaggerfallCastOutcome.Missed, stage.Session.ReleaseReadySpell(Player, -Vector3.UnitZ).Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_touch_reaches_an_ally_only_when_it_helps(bool harmful)
    {
        using Stage stage = new();
        long ally = stage.Ally();
        stage.Ready(DaggerfallSpellTarget.ByTouch, harmful);
        stage.Spatial.OverlapContactsResult = _ => Touching(stage.Session.State.Actors.Get(ally).Actor.Entity.Value);
        stage.Session.ReadyPlayerSpell(stage.Spell);

        DaggerfallCastOutcome outcome = stage.Session.ReleaseReadySpell(Player, -Vector3.UnitZ, harmsCivilians: true).Outcome;

        Assert.Equal(harmful, outcome == DaggerfallCastOutcome.Missed);
    }

    [Fact]
    public void A_helpful_area_takes_in_a_civilian_it_wasnt_aimed_at()
    {
        using Stage stage = new();
        stage.Ready(DaggerfallSpellTarget.AreaAroundCaster, harmful: false);
        stage.Session.ReadyPlayerSpell(stage.Spell);
        int queries = stage.Perception.Requests.Count;

        stage.Session.ReleaseReadySpell(Player, -Vector3.UnitZ);

        Assert.Contains(stage.Perception.Requests.Skip(queries).SelectMany(request => request.Targets.ToArray()), target => target.Entity == checked((ulong)stage.Civilian));
    }

    [Theory]
    [InlineData(4, 0, true)] // Damage health.
    [InlineData(1, 1, true)] // Continuous damage to fatigue.
    [InlineData(0, -1, true)] // Paralyze.
    [InlineData(11, 8, true)] // Transfer health.
    [InlineData(10, 8, false)] // Heal health.
    [InlineData(9, 0, false)] // Fortify strength.
    [InlineData(3, 1, false)] // Cure poison.
    [InlineData(35, -1, false)] // Shield.
    public void Harm_is_read_from_the_effects_kind(int type, int subType, bool harms)
    {
        DaggerfallSpellEffectDefinition sample = TestPayload.Definitions.Magic.Spells.Values.SelectMany(spell => spell.Effects).First();

        Assert.Equal(harms, DaggerfallMagicHarm.Harms(sample with { Type = type, SubType = subType }));
    }

    [Fact]
    public void Only_civilians_depend_on_the_aim()
    {
        using Stage stage = new();

        Assert.True(stage.Session.PassesThrough(stage.Civilian, harmsCivilians: false));
        Assert.False(stage.Session.PassesThrough(stage.Civilian, harmsCivilians: true));
        Assert.False(stage.Session.PassesThrough(Enemy, harmsCivilians: false));
    }

    private sealed class Stage : IDisposable
    {
        private ulong _step = 1;

        internal Stage()
        {
            List<string> releases = [];
            (DaggerfallSession session, _, PerceptionFake perception) = VisibleEnemySession(releases, out SpatialFake spatial, distance: 1.5d, random: RandomMaximum.Create());
            (Session, Spatial, Perception) = (session, spatial, perception);
            Session.State.WeaponDrawn = true;
            DaggerfallSiteRecord site = Session.Site.ActiveSite!;
            Civilian = Session.State.Npcs.RegisterCivilian(new(site.Id.Region, site.Name, string.Empty), new("breton", "Female", 0, 0, 0, 0), "civilian", ["talk"]);
            Session.MaterializeNpcActor(Civilian, Session.State.Actors.Get(Enemy).Pose);
            Session.State.Npcs.Bind(Civilian, Session.Sites.ActiveProfile);
            foreach (long actor in new[] { Civilian, Enemy })
            {
                Track health = Session.State.Actors.Get(actor).Stats.GetTrack(TrackId.Parse("health"));
                health.Maximum.BaseValue = 1000;
                health.SetCurrent(1000);
            }
            Track magicka = Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value));
            magicka.Maximum.BaseValue = 5000;
            magicka.SetCurrent(5000);
            Spatial.FloorHit = request => MathF.Abs(request.Direction.Y) >= .5f ? default : First(request);
        }

        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; }
        internal PerceptionFake Perception { get; }
        internal long Civilian { get; }
        internal ulong CivilianBody => Session.State.Actors.Get(Civilian).Actor.Entity.Value;
        internal ulong EnemyBody => Session.State.Actors.Get(Enemy).Actor.Entity.Value;
        internal ulong[] Bodies { get; set; } = [];
        internal string Spell { get; private set; } = "";

        /// <summary>A summoned ally beside the enemy, through the roster that summons them in play.</summary>
        internal long Ally()
        {
            var roster = (DaggerfallActorRoster)typeof(DaggerfallSession).GetField("_roster", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(Session)!;
            return roster.Spawn("rat", Session.State.Actors.Get(Enemy).Pose, playerAllied: true);
        }

        internal double Health(long actor) => Session.State.Actors.Get(actor).Stats.GetTrack(TrackId.Parse("health")).Current;

        /// <summary>A tap, run on until the attack has started and made up its mind.</summary>
        internal void Tap()
        {
            Session.Update(new ProductUpdate(OuterUpdate(_step++), AttackTap));
            Run(2);
        }

        internal void Run(int steps)
        {
            for (int step = 0; step < steps; step++) Session.Update(new ProductUpdate(OuterUpdate(_step++), []));
        }

        internal void Ready(DaggerfallSpellTarget target, bool harmful = true)
        {
            foreach (DaggerfallSpellDefinition spell in TestPayload.Definitions.Magic.Spells.Values.OrderBy(spell => spell.Key, StringComparer.Ordinal))
            {
                if (DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType) != target || spell.Effects.Any(DaggerfallMagicHarm.Harms) != harmful) continue;
                Session.State.Character.LearnSpell(spell.Key);
                if (Session.ReadyPlayerSpell(spell.Key).Outcome != DaggerfallCastOutcome.Ready) continue;
                Spell = spell.Key;
                return;
            }
            throw new InvalidOperationException($"No stock {target} spell can be readied.");
        }

        internal void Cast() => Session.Update(new ProductUpdate(OuterUpdate(_step++), [Ui("""{"action":"spell-cast"}""")]));

        internal void RunUntilReleased()
        {
            for (int step = 0; step < 40 && Session.Casting.PendingRangedFlights.Count == 0; step++) Run(1);
        }

        public void Dispose() => Session.Dispose();

        private SpatialHit First(SpatialRaycastRequest request)
        {
            ulong[] offered = [.. request.Entities.ToArray().Select(body => body.Entity)];
            foreach (ulong body in Bodies)
                if (offered.Contains(body))
                    return new SpatialHit(true, SpatialHitKind.Entity, body, 0, 0, 0, 0, 0, 0, default, request.Origin + request.Direction * .1f, -request.Direction, .1d, 0d, 0d, false, true);
            return default;
        }
    }
}
