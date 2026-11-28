using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Where a blow comes from and whom the player's harm passes, built without the session.</summary>
public sealed class DaggerfallStrikeAimTests
{
    private const long Ally = 12, Ally2 = 15, Civilian = 13, Enemy = 14, Enemy2 = 16;

    private sealed class Stage : IDisposable
    {
        internal Stage()
        {
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), TestSessions.HealthyMechanics(), "health", DaggerfallActorFactory.PlayerCapabilities);
            foreach (long actor in new[] { Ally, Ally2, Civilian, Enemy, Enemy2 })
                Actors.CreateActor(actor, new EntityTypeId("test"), TestSessions.HealthyMechanics(), new ActorPose(new WorldPoint(40f + actor, 0f, 0f), 0f), "health", DaggerfallActorFactory.NonPlayerCapabilities);
            _ = CombatCollaborators.Targeting(Actors, new Dictionary<long, DaggerfallActorDefinition>(), out _spatial);
            Aim = new DaggerfallStrikeAim(Actors, () => Player, () => Vector3.UnitZ, 1.5f,
                CombatCollaborators.Contact(Actors, _spatial, () => Player), () => null, 2d,
                actor => actor is Ally or Ally2, actor => actor == Civilian,
                isSameSide: (a, b) => (a is DaggerfallActorIdentity.PlayerEntityId or Ally or Ally2 && b is DaggerfallActorIdentity.PlayerEntityId or Ally or Ally2)
                    || (a is Enemy or Enemy2 && b is Enemy or Enemy2));
        }

        private readonly SpatialMovementSystem _spatial;
        internal ActorsState Actors { get; } = new();
        internal WorldPoint? Player { get; set; } = new WorldPoint(1f, 0f, 2f);
        internal DaggerfallStrikeAim Aim { get; }

        public void Dispose() { _spatial.Dispose(); Actors.Dispose(); }
    }

    [Fact]
    public void The_player_strikes_from_the_eye_along_the_look_and_a_site_actor_from_its_body_along_its_heading()
    {
        using Stage stage = new();
        Assert.Equal(new DaggerfallStrikePose(new Vector3(1f, 1.5f, 2f), Vector3.UnitZ), stage.Aim.StrikePose(DaggerfallActorIdentity.PlayerEntityId));

        ActorState enemy = stage.Actors.Get(Enemy);
        Assert.Equal(new DaggerfallStrikePose(enemy.Position.ToVector() + Vector3.UnitY * (DaggerfallContactBody.Envelope.Height * DaggerfallContactBody.EyeFraction),
            ActorHeading.Forward(enemy.HeadingYawRadians)), stage.Aim.StrikePose(Enemy));

        stage.Player = null;
        Assert.Null(stage.Aim.StrikePose(DaggerfallActorIdentity.PlayerEntityId));
        Assert.False(stage.Aim.AimedAtCivilian());
        Assert.Null(stage.Aim.StrikePose(404));
    }

    [Fact]
    public void The_players_harm_passes_allies_always_and_civilians_unless_it_started_aimed_at_one()
    {
        using Stage stage = new();
        Assert.True(stage.Aim.PassesThrough(Ally, harmsCivilians: true));
        Assert.True(stage.Aim.PassesThrough(Civilian, harmsCivilians: false));
        Assert.False(stage.Aim.PassesThrough(Civilian, harmsCivilians: true));
        Assert.False(stage.Aim.PassesThrough(Enemy, harmsCivilians: false));
    }

    [Fact]
    public void Non_player_harm_passes_same_side_allies_and_player_allies_pass_the_player_and_civilians()
    {
        using Stage stage = new();
        Assert.True(stage.Aim.PassesThrough(Ally, Ally2, harmsCivilians: false));
        Assert.True(stage.Aim.PassesThrough(Ally, DaggerfallActorIdentity.PlayerEntityId, harmsCivilians: false));
        Assert.True(stage.Aim.PassesThrough(Ally, Civilian, harmsCivilians: false));
        Assert.False(stage.Aim.PassesThrough(Ally, Enemy, harmsCivilians: false));

        Assert.True(stage.Aim.PassesThrough(Enemy, Enemy2, harmsCivilians: false));
        Assert.False(stage.Aim.PassesThrough(Enemy, DaggerfallActorIdentity.PlayerEntityId, harmsCivilians: false));
        Assert.False(stage.Aim.PassesThrough(Enemy, Ally, harmsCivilians: false));
        Assert.False(stage.Aim.PassesThrough(Enemy, Civilian, harmsCivilians: false));
    }
}
