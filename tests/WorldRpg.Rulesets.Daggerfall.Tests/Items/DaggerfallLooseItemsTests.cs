using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Loose items as real Engine Dynamics bodies on a floor: a dropped item falls, settles upright and sleeps by the Engine's
/// thresholds; one met where it rests is made asleep there, as a loaded one is; a body fits its billboard and weighs its pile; a carried item is held
/// before the eye and pushes another; grabbing is refused out of reach or past the player's Strength; a throw carries a
/// light item across a room and a heavy one less far; and bodies let go of are made again where their piles lie.
/// </summary>
public sealed class DaggerfallLooseItemsTests
{
    private const float Update = 1f / 30;
    private static readonly Vector2 Billboard = new(.4f, .3f);
    private static readonly DaggerfallLooseItemTuning Tuning = DaggerfallLooseItemTuning.Default;
    private const float Wall = -1.5f;
    private const float PlayerRadius = .35f;

    [Fact]
    public void A_dropped_item_falls_ahead_settles_upright_on_the_floor_and_sleeps()
    {
        Run(room =>
        {
            room.Add(1, new(0, .9f, 1), 1);
            room.Items.Drop(1, new(1.5f, 0, 0));
            int updates = room.StepUntilAsleep(1);

            DaggerfallLooseItemReadout item = room.Read(1);
            // The Engine sleeps a body still for two seconds, whatever the item (rusty-engine #10945 asks for sooner).
            Assert.True(updates < 120, $"It took {updates} updates to sleep.");
            Assert.InRange(item.Foot.Y, -.02f, .02f);
            Assert.True(item.Foot.X > .1f, $"It fell straight down to {item.Foot}.");
            Assert.Equal(item.Foot, room.Piles[1].Foot);
            Upright(item);
        });
    }

    [Fact]
    public void An_item_met_where_it_rests_is_made_asleep_there_as_a_loaded_one_is()
    {
        Run(room =>
        {
            // Half a metre up: it stays there while nothing near it moves.
            room.Add(1, new(0, .5f, 1), 1);
            room.Step(30);

            DaggerfallLooseItemReadout item = room.Read(1);
            Assert.True(item.Sleeping);
            Assert.True(Vector3.Distance(new(0, .5f, 1), item.Foot) < 1e-4f, $"It moved to {item.Foot}.");
        });
    }

    [Fact]
    public void A_body_is_as_wide_and_deep_as_its_billboard_is_wide_and_weighs_its_pile_or_the_least_mass()
    {
        Run(room =>
        {
            room.Add(1, new(0, 0, 1), 2);
            room.Add(2, new(2, 0, 1), 0);
            room.Step(1);

            Assert.Equal(new Vector3(.4f, .3f, .4f), room.Read(1).Size);
            Assert.Equal(2f, room.Read(1).Kilograms);
            Assert.Equal(Tuning.MinimumKilograms, room.Read(2).Kilograms);
        });
    }

    [Fact]
    public void An_item_dropped_half_over_another_rests_on_it_upright()
    {
        Run(room =>
        {
            room.Add(1, new(0, 0, 1), 1);
            room.Add(2, new(.15f, .9f, 1), 1);
            room.Items.Drop(2, Vector3.Zero);
            room.StepUntilAsleep(2);
            room.StepUntilAsleep(1);

            DaggerfallLooseItemReadout top = room.Read(2);
            Assert.InRange(top.Foot.Y, .28f, .32f);
            Upright(top);
            Upright(room.Read(1));
        });
    }

    [Fact]
    public void A_carried_item_is_held_before_the_eye_and_pushes_another_aside()
    {
        Run(room =>
        {
            room.Add(1, new(0, 0, 1), 1);
            room.Add(2, new(0, 0, 2.6f), 1);
            room.Step(1);
            DaggerfallLooseItemHand hand = new(new(0, .3f, 0), Vector3.UnitZ, 50);
            Assert.Null(room.Items.Grab(1, hand));
            room.Step(30, hand);
            Assert.True(room.Read(1).Carried);
            Assert.True(Vector3.Distance(room.Read(1).Foot + Vector3.UnitY * .15f, new(0, .3f, Tuning.HoldDistance)) < .1f, $"It is held at {room.Read(1).Foot}.");

            // Walking the hand on carries it into the other, which it shoves.
            for (int step = 0; step < 30; step++) room.Step(1, hand with { Eye = new(0, .3f, 1 + step * .05f) });
            Assert.True(room.Read(2).Foot.Z > 2.7f || MathF.Abs(room.Read(2).Foot.X) > .1f, $"The other stayed at {room.Read(2).Foot}.");

            room.Items.Release();
            room.StepUntilAsleep(1);
            Assert.InRange(room.Read(1).Foot.Y, -.02f, .32f);
            Assert.False(room.Read(1).Carried);
        });
    }

    [Fact]
    public void Grabbing_is_refused_past_reach_past_Strength_and_where_no_body_is()
    {
        Run(room =>
        {
            room.Add(1, new(0, 0, 1), 30);
            room.Add(2, new(0, 0, 6), 1);
            room.Step(1);
            DaggerfallLooseItemHand hand = new(new(0, 1.6f, 0), Vector3.UnitZ, 50);

            Assert.Equal("That is too heavy to lift.", room.Items.Grab(1, hand));
            // Strength 70 lifts 35 kg.
            Assert.Null(room.Items.Grab(1, hand with { Strength = 70 }));
            room.Items.Release();
            Assert.Equal("That is out of reach.", room.Items.Grab(2, hand));
            Assert.Equal("That cannot be moved.", room.Items.Grab(3, hand));
            Assert.Null(room.Items.Carried);
        });
    }

    [Fact]
    public void A_throw_carries_a_light_item_across_a_room_and_a_heavier_one_less_far()
    {
        float Thrown(float kilograms)
        {
            float distance = 0;
            Run(room =>
            {
                room.Add(1, new(0, 0, 1), kilograms);
                room.Step(1);
                DaggerfallLooseItemHand hand = new(new(0, 1.6f, 0), Vector3.UnitZ, 50);
                Assert.Null(room.Items.Grab(1, hand));
                room.Step(20, hand);
                Assert.True(room.Items.Throw(hand));
                Assert.Null(room.Items.Carried);
                room.StepUntilAsleep(1, hand);
                distance = room.Read(1).Foot.Z;
            });
            return distance;
        }

        float light = Thrown(.5f), heavy = Thrown(5);
        Assert.True(light > 4, $"A light item flew {light} m.");
        Assert.True(heavy < light - 1, $"A heavy item flew {heavy} m, a light one {light} m.");
    }

    [Fact]
    public void Bodies_let_go_of_are_made_again_where_their_piles_lie_asleep_or_falling_on()
    {
        Run(room =>
        {
            room.Add(1, new(0, .9f, 1), 1);
            room.Items.Drop(1, Vector3.Zero);
            room.StepUntilAsleep(1);
            Vector3 rested = room.Read(1).Foot;
            room.Add(2, new(2, 1.5f, 1), 1);
            room.Items.Drop(2, Vector3.Zero);
            room.Step(5);
            Assert.False(room.Read(2).Sleeping);

            // The site changes or the origin moves under them: their piles keep where they are.
            room.Items.LetGo();
            room.Step(1);
            Assert.True(Vector3.Distance(rested, room.Read(1).Foot) < .01f, $"It moved from {rested} to {room.Read(1).Foot}.");
            Assert.False(room.Read(2).Sleeping);
            room.StepUntilAsleep(2);
            room.StepUntilAsleep(1);
            Assert.InRange(room.Read(2).Foot.Y, -.02f, .02f);
            Assert.True(Vector3.Distance(rested, room.Read(1).Foot) < .01f, $"It moved from {rested} to {room.Read(1).Foot}.");
        });
    }

    [Fact]
    public void Items_dropped_together_are_let_go_side_by_side_and_settle_on_the_floor_not_in_a_tower()
    {
        Run(room =>
        {
            for (long id = 1; id <= 6; id++)
            {
                room.Add(id, new(0, .9f, 1), 1);
                room.Items.Drop(id, new(0, 0, 1.5f));
            }
            for (long id = 1; id <= 6; id++) room.StepUntilAsleep(id);

            Assert.All(room.Items.Read(), item => Assert.InRange(item.Foot.Y, -.02f, .02f));
            Assert.All(room.Items.Read(), Upright);
            // None lands inside the player's body: the first, let go where the player stands, is tossed out of it.
            Assert.DoesNotContain(room.Items.Read(), item => new Vector2(item.Foot.X, item.Foot.Z - 1).Length() < PlayerRadius);
        });
    }

    [Fact]
    public void More_items_dropped_together_than_there_is_room_for_wait_and_fall_after_the_others_rather_than_stacking()
    {
        Run(room =>
        {
            // The wall a metre aside closes half the rings about the drop point.
            for (long id = 1; id <= 40; id++)
            {
                room.Add(id, new(-.5f, .9f, 1), 1);
                room.Items.Drop(id, new(0, 0, 1.5f));
            }
            room.Step(1);
            Assert.True(room.Items.Read().Count < 40, "Every item was made at once.");
            room.Step(600);

            Assert.Equal(40, room.Items.Read().Count);
            Assert.All(room.Items.Read(), item => Assert.True(item.Foot.Y < .35f, $"Pile {item.Id} rests at {item.Foot}, on a tower."));
        });
    }

    [Fact]
    public void Items_dropped_together_beside_a_wall_are_never_let_go_beyond_it()
    {
        Run(room =>
        {
            for (long id = 1; id <= 12; id++)
            {
                room.Add(id, new(-1, .9f, 1), 1);
                room.Items.Drop(id, Vector3.Zero);
            }
            for (long id = 1; id <= 12; id++) room.StepUntilAsleep(id);

            Assert.All(room.Items.Read(), item => Assert.True(item.Foot.X - item.Size.X / 2 > Wall - .01f, $"Pile {item.Id} lies beyond the wall at {item.Foot}."));
        });
    }

    [Fact]
    public void A_pile_placed_anew_by_its_owner_has_its_body_made_again_there()
    {
        Run(room =>
        {
            room.Add(1, new(0, 0, 1), 1);
            room.Step(1);
            // A quest places the item elsewhere.
            room.Add(1, new(3, 0, 2), 1);
            room.Step(1);
            Assert.True(Vector3.Distance(new(3, 0, 2), room.Read(1).Foot) < .01f, $"Its body stayed at {room.Read(1).Foot}.");
        });
    }

    private static void Upright(DaggerfallLooseItemReadout item) =>
        Assert.True(MathF.Abs(Quaternion.Dot(item.Turn, Quaternion.Identity)) > .9999f, $"It turned to {item.Turn}.");

    private static void Run(Action<Room> test)
    {
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            using SpatialMovementSystem spatial = new(engine.Spatial, engine.Content, null, new SpatialTuning(.5d, 8, 8, 1, new CharacterControllerTuning()));
            // A floor 40 m square at height 0, and a wall 3 m high along x = -1.5, each faced both ways.
            engine.Spatial.ReplaceCollision(new(spatial.Session, new StaticMeshAsset[] { new(1, 0, 8, 0, 8) },
                new Vector3[] { new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20), new(-20, 0, 20),
                    new(Wall, 0, -20), new(Wall, 3, -20), new(Wall, 3, 20), new(Wall, 0, 20) },
                new Triangle[] { new(0, 2, 1), new(0, 3, 2), new(0, 1, 2), new(0, 2, 3), new(4, 6, 5), new(4, 7, 6), new(4, 5, 6), new(4, 6, 7) },
                new StaticMeshInstance[] { new(1, 1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) }));
            using DaggerfallLooseItems items = new(engine.Dynamics, Tuning);
            test(new Room(spatial, items));
        });
    }

    private sealed class Room(SpatialMovementSystem spatial, DaggerfallLooseItems items)
    {
        internal DaggerfallLooseItems Items { get; } = items;
        internal Dictionary<long, (Vector3 Foot, float Kilograms)> Piles { get; } = [];

        internal void Add(long id, Vector3 foot, float kilograms) => Piles[id] = (foot, kilograms);

        internal DaggerfallLooseItemReadout Read(long id) => Items.Read().Single(item => item.Id == id);

        internal void Step(int updates, DaggerfallLooseItemHand? hand = null)
        {
            for (int update = 0; update < updates; update++)
                Items.Step(Update, new(spatial.Session, id => Piles[id].Kilograms, _ => true,
                        (from, to, half) => !spatial.CastCapsule(from, Math.Max(0, half.Y - half.X), half.X, to - from, ReadOnlyMemory<SpatialEntityCollider>.Empty).Present,
                        (id, foot) => Piles[id] = (foot, Piles[id].Kilograms), PlayerRadius),
                    [.. Piles.Select(pile => new DaggerfallLooseItem(pile.Key, pile.Value.Foot, Billboard))], Vector3.Zero, hand ?? new(new(0, 1.6f, 0), Vector3.UnitZ, 50));
        }

        /// <summary>Steps until the pile's body sleeps, up to ten seconds, and answers how many updates it took.</summary>
        internal int StepUntilAsleep(long id, DaggerfallLooseItemHand? hand = null)
        {
            for (int update = 1; update <= 300; update++)
            {
                Step(1, hand);
                if (Read(id).Sleeping) return update;
            }
            Assert.Fail($"Pile {id} never slept; it lies at {Read(id).Foot} moving {Read(id).Velocity}.");
            return 0;
        }
    }
}
