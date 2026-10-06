using Rusty.Engine.Mechanics;
using System.Text;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed partial class DaggerfallEquipmentWearTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delayed_melee_feedback_retains_the_accepted_weapon_after_its_source_is_removed(bool hit)
    {
        using WearFixture f = new();
        f.Script(body: 9, critical: 50, hit: hit ? 1 : 100, damage: hit ? 15 : null);
        var facts = f.RunPlayerAttack(beforeImpact: () => f.DestroyNamira(1001));
        var feedback = hit ? Assert.Single(facts.OfType<AttackHitFact>()).Feedback
            : Assert.Single(facts.OfType<AttackMissedFact>()).Feedback;
        Assert.True(feedback.Weapon);
        Assert.Equal("sound.347", feedback.SwingCue);
        Assert.DoesNotContain(f.PlayerEquipment.Read().Assignments, item => item.Item.Definition.Value == "iron-longsword");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(100, true)]
    public void Razor_uses_the_victims_magic_save_and_one_ordinary_hit_and_death(int save, bool terminal)
    {
        using WearFixture f = new();
        f.EquipRazor();
        f.Script(body: 9, critical: 50, hit: 1, damage: 5, razorSave: save);
        var facts = f.RunPlayerAttack();
        var artifact = Assert.Single(facts.OfType<ArtifactTerminalStrikeFact>());
        Assert.Equal(terminal ? 200 : 0, artifact.AddedDamage);
        Assert.Equal(9501UL, artifact.SourceItemId);
        var hit = Assert.Single(facts.OfType<AttackHitFact>());
        Assert.Single(facts.OfType<DamageAppliedFact>());
        Assert.Equal(terminal, hit.ActualHealthLost == 200);
        Assert.Equal(terminal ? 1 : 0, facts.OfType<ActorDiedFact>().Count());
        Assert.True(terminal ? f.Condition(9501) <= 1300 : f.Condition(9501) > 1300);
        if (terminal) Assert.True(facts.ToList().FindIndex(x => x is AttackHitFact) < facts.ToList().FindIndex(x => x is ActorDiedFact));
    }

    [Fact]
    public void Razor_miss_offers_its_save_and_a_removed_source_has_no_artifact_result()
    {
        // The donor runs the player's Strikes payloads on a miss too.
        using WearFixture miss = new();
        miss.EquipRazor();
        miss.Script(body: 9, critical: 50, hit: 100, razorSave: 1);
        Assert.Equal(0, Assert.Single(miss.RunPlayerAttack().OfType<ArtifactTerminalStrikeFact>()).AddedDamage);
        Assert.Equal(1500, miss.Condition(9501));
        using WearFixture removed = new();
        removed.EquipRazor();
        removed.DestroyNamira(9501);
        removed.EquipPlayerItem("iron-longsword", 9600, "right-hand");
        removed.Script(body: 9, critical: 50, hit: 1, damage: 15);
        Assert.Empty(removed.RunPlayerAttack().OfType<ArtifactTerminalStrikeFact>());
    }

    [Fact]
    public void Razor_magic_immune_career_resists_without_a_random_save()
    {
        var payload = JsonNode.Parse(TestPayload.CombinedText)!;
        var career = payload["catalogs"]!["careers"]!.AsArray().First(x => x!["id"]!.GetValue<string>() == "class00")!;
        career["immunityFlags"] = 2;
        career["immunityElements"] = new JsonArray("magic");
        using WearFixture f = new(DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(payload.ToJsonString())));
        f.EquipRazor();
        var immune = f.Definitions.Catalogs.RequireCareer("class00");
        f.ReplaceActor(Enemy, f.Definitions.RequireActor(new DaggerfallActorId("thief")) with { ActionId = "enemy-class-equipped-melee", Career = immune.Id });
        f.Script(body: 9, critical: 50, hit: 1, damage: 5);
        Assert.Equal(0, Assert.Single(f.RunPlayerAttack().OfType<ArtifactTerminalStrikeFact>()).AddedDamage);
        Assert.True(f.EnemyActor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).Current > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Razor_refused_material_and_dead_target_do_not_evaluate_or_charge_the_source(bool dead)
    {
        using WearFixture f = new(); f.EquipRazor();
        if (dead) f.EnemyActor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).SetCurrent(0);
        else
        {
            f.ReplaceActor(Enemy, f.Definitions.RequireActor(new DaggerfallActorId("thief")) with
                { ActionId = "enemy-class-equipped-melee", MinimumMaterial = "daedric" });
            f.Script(body: 9, critical: 50, hit: 1);
        }
        var facts = f.RunPlayerAttack(requireAccepted: !dead);
        Assert.Empty(facts.OfType<ArtifactTerminalStrikeFact>());
        Assert.Empty(facts.OfType<ActorDiedFact>());
        Assert.Equal(1500, f.Condition(9501));
    }

    [Fact]
    public void Razor_terminal_durability_break_unequips_its_actual_source()
    {
        using WearFixture f = new();
        f.EquipRazor(condition: 50);
        f.Script(body: 9, critical: 50, hit: 1, damage: 5, razorSave: 100);
        var facts = f.RunPlayerAttack();
        Assert.Equal(0, f.Condition(9501));
        Assert.DoesNotContain(f.PlayerEquipment.Read().Assignments, assignment => assignment.Item.Definition.Value == "template-113-orcish-magic-magic-item-0001");
        Assert.Single(facts.OfType<ActorDiedFact>());
        Assert.Single(facts.OfType<EquipmentWornFact>(), x => x.Broken);
        Assert.True(Assert.Single(facts.OfType<AttackHitFact>()).Feedback.Weapon);
        Assert.Equal("swing", Assert.Single(facts.OfType<AttackHitFact>()).Feedback.SwingCue);
    }
}
