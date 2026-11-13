using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Spells in flight built alone over Magic's owners; the swept segments meeting the world are the session's (DungeonSpellFlightSessionTests).</summary>
public sealed class DaggerfallSpellFlightsTests
{
    private static (DaggerfallSpellFlights Flights, DaggerfallEnemySpellRelease Enemy) Owners(MagicCastFixture f)
    {
        DaggerfallSpellRelease release = new(f.Casting, f.Targets, f.Items.Definitions, f.Actors, f.Items.Character, f.Items.Inventory,
            new PresentationState("", 5d), 1.6f, () => f.Positions, _ => throw new InvalidOperationException("No touch spell here."), null!,
            () => default, (_, _) => false, (_, _, _) => { });
        return (new DaggerfallSpellFlights(f.Casting, release), new DaggerfallEnemySpellRelease(f.Casting, f.Targets, _ => default, null!, null!, () => default));
    }

    // A spell the player buys that flies: free action at range.
    private static string RangedSpell(MagicCastFixture f)
    {
        DaggerfallSpellMaker maker = f.Items.SpellMaker(f.Effects.Catalog);
        DaggerfallServiceProvider provider = f.Items.Provider("make-spells", 40, 64, 0);
        DaggerfallSpellMakerEffect option = maker.Effects.Single(effect => effect.Key == "free-action");
        // A purchase is keyed by the maker's draft revision; one draft past the fixture's keeps this one its own.
        maker.SetDraft(new("Draft", 4, 2, 1, []));
        maker.SetDraft(new("Bolt", 4, 2, 1, [new("free-action", option.Type, option.SubType, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1)]));
        DaggerfallSpellConstructionQuote quote = maker.Quote(provider)!;
        var bought = maker.Buy(provider, quote.Key, (ulong)quote.Gold, true);
        Assert.True(bought.Accepted, bought.ToString());
        return f.Items.Character.KnownSpells.Single(key => key != f.SpellKey);
    }

    private static ProductUpdateFacts Update(ulong step) => new(ProductLifecycleState.Running, 1, 1, step, step, 60, 1, 0, 1d / 60d);

    [Fact]
    public void A_ranged_release_flies_from_its_origin_along_its_direction_with_its_element()
    {
        using MagicCastFixture f = new();
        (DaggerfallSpellFlights flights, DaggerfallEnemySpellRelease enemy) = Owners(f);
        WorldPoint at = new(3f, 0f, 0f);
        Assert.Empty(flights.Views());

        DaggerfallCastResult cast = enemy.Execute(new(MagicCastFixture.Monster, RangedSpell(f), DaggerfallSpellTarget.SingleTargetAtRange, at, new WorldPoint(3f, 0f, 20f), Vector3.UnitZ));
        DaggerfallLiveSpell bundle = cast.Bundle!;

        DaggerfallDungeonSpellFlightView view = Assert.Single(flights.Views());
        Assert.Equal(bundle.Sequence, view.Sequence);
        Assert.Equal(bundle.ReleaseOrigin!.Value, view.Position.ToVector());
        Assert.Equal(Vector3.UnitZ, view.Direction);
        Assert.Equal(bundle.Spell.Element, view.Element);
    }

    [Fact]
    public void A_release_with_no_usable_direction_ends_as_a_miss_when_the_flights_advance()
    {
        using MagicCastFixture f = new();
        (DaggerfallSpellFlights flights, DaggerfallEnemySpellRelease enemy) = Owners(f);
        WorldPoint at = new(3f, 0f, 0f);
        DaggerfallLiveSpell bundle = enemy.Execute(new(MagicCastFixture.Monster, RangedSpell(f), DaggerfallSpellTarget.SingleTargetAtRange, at, new WorldPoint(3f, 0f, 20f), Vector3.UnitZ)).Bundle!;
        Assert.Single(flights.Views());
        bundle.ReleaseDirection = Vector3.Zero;

        flights.Advance(Update(1), 1, 1d / 60d);

        Assert.True(bundle.Delivered);
        Assert.Empty(flights.Views());
        Assert.Throws<ArgumentOutOfRangeException>(() => flights.Advance(Update(2), 1, 0d));
    }
}
