using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallRacialOverridesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void One_source_overrides_compound_race_and_cure_restores_known_spell_and_legal_policy(int variant)
    {
        var kind = (DaggerfallRacialKind)variant;
        using var session = FreshSession();
        var racial = session.State.RacialOverrides;
        var birth = session.State.Character.Race;
        Assert.Null(racial.Current);
        Assert.True(racial.Select(kind, "curse", 0));
        Assert.True(racial.Select(kind, "curse", 0));
        Assert.False(racial.Select(kind, "other", 0));
        Assert.False(racial.Remove("other"));
        Assert.Equal(birth.Id, session.State.Character.Identity.RaceId);
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Immune, session.State.Character.Race.Tolerance(DaggerfallCareerTolerances.Disease));
        Assert.True(session.State.Character.IsGrantedSpell("spell.085"));
        Assert.False(session.State.Character.ForgetSpell("spell.085"));
        racial.SetBeastForm(true, 1);
        Assert.True(racial.Current!.SuppressCrime);
        Assert.False(session.State.Crime.RecordIncident(Incident("suppressed")));
        using var restored = Restore(session.CaptureSave());
        Assert.Equal(racial.Current, restored.State.RacialOverrides.Current);
        Assert.True(restored.State.Character.IsGrantedSpell("spell.085"));
        Assert.True(restored.State.RacialOverrides.Remove("curse"));
        Assert.Null(restored.State.RacialOverrides.Current);
        Assert.Equal(birth, restored.State.Character.Race);
        Assert.DoesNotContain("spell.085", restored.State.Character.KnownSpells);
        Assert.True(restored.State.Crime.RecordIncident(Incident("normal")));
        Assert.Empty(restored.State.Effects.Active);
    }

    [Fact]
    public void Explicit_replacement_and_grant_cleanup_preserve_independently_learned_spells()
    {
        using var session = FreshSession();
        session.State.Character.LearnSpell("spell.085");
        Assert.True(session.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "first", 0));
        Assert.True(session.State.RacialOverrides.Select(DaggerfallRacialKind.Wereboar, "second", 1, replace: true));
        Assert.Single(session.State.Effects.Active);
        Assert.Equal("second", session.State.RacialOverrides.Current!.Source);
        Assert.True(session.State.RacialOverrides.Remove("second"));
        Assert.Contains("spell.085", session.State.Character.KnownSpells);
        Assert.Empty(session.State.Character.SpellGrants);
        Assert.True(session.State.Character.ForgetSpell("spell.085"));
    }

    [Fact]
    public void Save_refuses_missing_grant_wrong_kind_and_timed_racial_sources()
    {
        using var session = FreshSession(); session.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "curse", 0);
        var saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Throws<ArgumentException>(() => Restore(DaggerfallSavePayload.Encode(saved with
        { Character = saved.Character! with { SpellGrants = [] } })));
        Assert.Throws<ArgumentException>(() => Restore(DaggerfallSavePayload.Encode(saved with
        { Character = saved.Character! with { SpellGrants = [saved.Character.SpellGrants![0] with { Kind = DaggerfallSpellGrantKind.Vampirism }] } })));
        Assert.Throws<ArgumentException>(() => Restore(DaggerfallSavePayload.Encode(saved with
        { ActiveEffects = [saved.ActiveEffects[0] with { RemainingRounds = 1 }] })));
    }

    [Fact]
    public void Saved_curse_grants_must_match_the_actual_racial_owner()
    {
        using var session = FreshSession();
        session.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "curse", 0);
        session.State.Character.GrantSpell("spell.009", "curse", DaggerfallSpellGrantKind.Vampirism);
        Assert.Throws<ArgumentException>(() => Restore(session.CaptureSave()));
        session.State.RacialOverrides.Remove("curse");
        Assert.False(session.State.Character.IsGrantedSpell("spell.009"));
        Assert.DoesNotContain("spell.009", session.State.Character.KnownSpells);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("bank-open")]
    public void Beast_form_refuses_inventory_actions_and_human_form_restores_admission(string action)
    {
        using var session = FreshSession();
        session.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "curse", 0);
        session.State.RacialOverrides.SetBeastForm(true, 0);
        session.Update(new ProductUpdate(OuterUpdate(1), [Ui(System.Text.Json.JsonSerializer.Serialize(action == "bank-open" ? new { action, revision = "current" } : (object)new { action }))]));
        Assert.Equal("You cannot use your inventory in beast form.", session.Presentation.LastOutcome);
        session.State.RacialOverrides.SetBeastForm(false, 0);
        session.Presentation.SetOutcome("Human form");
        session.Update(new ProductUpdate(OuterUpdate(2), [Ui(System.Text.Json.JsonSerializer.Serialize(action == "bank-open" ? new { action, revision = "current" } : (object)new { action }))]));
        Assert.NotEqual("You cannot use your inventory in beast form.", session.Presentation.LastOutcome);
    }

    private static DaggerfallCrimeIncidentSave Incident(string key) => new(key, DaggerfallCrimeKind.Assault,
        DaggerfallCrimeStage.Completed, 1, null, 1, 1, DaggerfallCrimeTargetKind.Civilian,
        DaggerfallCrimeWitnessEvidence.NotQueried, DaggerfallCrimeGuildCredit.None);
    private static DaggerfallSession Restore(RulesetSavePayload save)
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        return DaggerfallSession.Restore(EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases)).Context,
            new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults), save);
    }
}
