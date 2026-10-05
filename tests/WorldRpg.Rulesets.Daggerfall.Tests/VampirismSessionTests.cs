using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class VampirismSessionTests
{
    [Fact]
    public void Province_clan_formula_preserves_all_regions_and_fallback_bounds()
    {
        var definitions = TestPayload.Definitions;
        for (int region = 0; region <= 61; region++)
        {
            var provinces = definitions.Factions.Factions.Values.Where(f => f.Type == 7 && f.Region == region).ToArray();
            if (provinces.Length != 1)
            {
                Assert.Throws<NotSupportedException>(() => DaggerfallVampirismPolicy.GetVampireClan(definitions.Factions, region));
                continue;
            }
            int expected = provinces[0].Vampire is >= 150 and <= 158 ? provinces[0].Vampire : 153;
            Assert.Equal(expected, DaggerfallVampirismPolicy.GetVampireClan(definitions.Factions, region).Id);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallVampirismPolicy.GetVampireClan(definitions.Factions, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallVampirismPolicy.GetVampireClan(definitions.Factions, 62));
    }

    [Fact]
    public void All_clans_install_exact_spells_bonuses_immunities_and_remove_owned_sources()
    {
        using var s = FreshSession();
        var stats = s.State.Actors.Player.Stats;
        double strength = stats.GetStat(StatId.Parse("strength")).Value;
        double intelligence = stats.GetStat(StatId.Parse("intelligence")).Value;
        double swimming = stats.GetStat(StatId.Parse("swimming")).Value;
        double running = stats.GetStat(StatId.Parse("running")).Value;
        var birth = s.State.Character.Race;
        for (int clan = 150; clan <= 158; clan++)
        {
            Assert.True(s.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "vamp:" + clan, Minute(s), vampireClan: clan));
            Assert.True(s.State.RacialOverrides.Current!.RequiresSilver);
            Assert.False(s.State.RacialOverrides.Current.SuppressCrime);
            Assert.False(s.State.RacialOverrides.Current.SuppressTalk);
            Assert.Equal(strength + 20, stats.GetStat(StatId.Parse("strength")).Value);
            Assert.Equal(intelligence + (clan == 157 ? 20 : 0), stats.GetStat(StatId.Parse("intelligence")).Value);
            Assert.Equal(swimming, stats.GetStat(StatId.Parse("swimming")).Value);
            Assert.Equal(running + 30, stats.GetStat(StatId.Parse("running")).Value);
            Assert.Equal((int)(DaggerfallMagicEffectFlags.Disease | DaggerfallMagicEffectFlags.Paralysis),
                s.State.Character.Race.ImmunityFlags & (int)(DaggerfallMagicEffectFlags.Disease | DaggerfallMagicEffectFlags.Paralysis));
            var required = DaggerfallVampirismPolicy.GrantedSpells(TestPayload.Definitions.Magic, clan);
            var grants = DaggerfallSavePayload.Read(s.CaptureSave()).Character!.SpellGrants!;
            Assert.Equal(required.Order(), grants.Select(g => g.Spell).Order());
            Assert.All(grants, grant => Assert.Equal(DaggerfallSpellGrantKind.Vampirism, grant.Kind));
            Assert.All(required, spell => Assert.False(s.State.Character.ForgetSpell(spell)));
            Assert.False(s.MorphPlayer());
            Assert.True(s.CureVampirism());
            Assert.Equal(birth, s.State.Character.Race);
            Assert.Equal(strength, stats.GetStat(StatId.Parse("strength")).Value);
            Assert.Equal(intelligence, stats.GetStat(StatId.Parse("intelligence")).Value);
            Assert.Equal(running, stats.GetStat(StatId.Parse("running")).Value);
            Assert.Empty(DaggerfallSavePayload.Read(s.CaptureSave()).Character!.SpellGrants!);
        }
    }

    [Fact]
    public void Saved_grants_preserve_purchased_identity_and_cure_cancels_unlearned_readiness()
    {
        using var f = new Fixture(); var s = f.Session;
        var spells = DaggerfallVampirismPolicy.GrantedSpells(TestPayload.Definitions.Magic, 153);
        s.State.Character.LearnSpell(spells[0]);
        s.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "vampire", Minute(s), vampireClan: 153);
        var mana = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); mana.Maximum.BaseValue = 10000; mana.SetCurrent(10000);
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell(spells[1]).Outcome);
        Assert.Equal(5, s.Casting.ReadyFor(1)!.Cost);
        using var restored = f.Restore();
        Assert.Equal(s.State.RacialOverrides.Current, restored.State.RacialOverrides.Current);
        Assert.False(restored.State.Character.ForgetSpell(spells[0]));
        Assert.True(restored.CureVampirism());
        Assert.Null(restored.Casting.ReadyFor(1));
        Assert.Contains(spells[0], restored.State.Character.KnownSpells);
        Assert.DoesNotContain(spells[1], restored.State.Character.KnownSpells);
        Assert.True(restored.State.Character.ForgetSpell(spells[0]));
    }

    [Fact]
    public void Real_completed_infection_reawakens_in_published_cemetery_after_two_weeks_and_restores()
    {
        using var f = new Fixture(); var s = f.Session;
        const int infectedRegion = 0;
        Assert.Equal(DaggerfallInfectionAdmission.Started, s.InflictTransformationInfection(new("vampire-infection", "monster.hit", null, 1, DaggerfallInfectionKind.Vampire, infectedRegion)));
        s.AdvanceElapsedTime(4 * 86400);
        long day = DaggerfallSavePayload.Read(s.CaptureSave()).Calendar.Day;
        for (ulong i = 1; i <= 5; i++) s.Update(new ProductUpdate(OuterUpdate(i), []));
        var racial = Assert.IsType<DaggerfallRacialOverrideView>(s.State.RacialOverrides.Current);
        Assert.True(racial.IsVampire);
        Assert.Equal(DaggerfallVampirismPolicy.GetVampireClan(TestPayload.Definitions.Factions, infectedRegion).Id, racial.State.Vampire!.Clan);
        var saved = DaggerfallSavePayload.Read(s.CaptureSave());
        Assert.Equal(19, saved.Calendar.Hour);
        Assert.Equal(day + 14, saved.Calendar.Day);
        Assert.Equal(17, saved.Site.Active!.Region);
        Assert.Equal(0, saved.Site.Active!.Index);
        Assert.DoesNotContain(s.DefinitionsByActor.Values, a => a.Kind == DaggerfallActorKinds.Monster);
        Assert.Empty(s.Infections.Ready);
        Assert.NotEqual(new WorldPoint(0,0,0), s.State.PlayerControl.Position!.Value);
        Assert.Contains(saved.Infections.LastOutcomes, x => x.Outcome == DaggerfallInfectionCleanup.Consumed);
        using var restored = f.Restore();
        Assert.Equal(racial, restored.State.RacialOverrides.Current);
        Assert.Equal(saved.Calendar, DaggerfallSavePayload.Read(restored.CaptureSave()).Calendar);
        var exit = Assert.Single(f.Profiles[1].Portals);
        Assert.Equal(f.Profiles[2].ProfileKey.LogicalId, exit.DestinationLogicalProfile);
        Assert.True(restored.TryTransitionTo(f.Profiles[2].ProfileKey));
        Assert.Equal(DaggerfallWorldProfileKind.Exterior, DaggerfallSavePayload.Read(restored.CaptureSave()).Site.ActiveProfile!.Require().Kind);
        Assert.True(restored.TryTransitionTo(f.Profiles[1].ProfileKey));
        Assert.DoesNotContain(restored.DefinitionsByActor.Values, a => a.Kind == DaggerfallActorKinds.Monster);

    }

    [Fact]
    public void Vampire_media_retains_birth_identity_and_cure_and_replacement_restore_it()
    {
        using var s = FreshSession();
        var original = CharacterIdentityPresentation.From(TestPayload.Definitions, s.State.Character.Identity, null);
        s.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "vampire", Minute(s), vampireClan: 157);
        var vampire = CharacterIdentityPresentation.From(TestPayload.Definitions, s.State.Character.Identity, s.State.RacialOverrides.Current);
        Assert.Equal(original.Race, vampire.Race); Assert.Equal(original.Gender, vampire.Gender);
        Assert.Equal("Anthotis", vampire.VampireClan);
        Assert.NotEqual(original.SelectedMedia!.Single(x => x.Layer.StartsWith("head")).MediaId, vampire.SelectedMedia!.Single(x => x.Layer.StartsWith("head")).MediaId);
        Assert.NotEqual(original.SelectedMedia.Single(x => x.Layer == "background").MediaId, vampire.SelectedMedia.Single(x => x.Layer == "background").MediaId);
        Assert.True(s.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "wolf", Minute(s), replace: true));
        Assert.Null(s.State.RacialOverrides.Current!.State.Vampire);
        Assert.DoesNotContain(DaggerfallSavePayload.Read(s.CaptureSave()).Character!.SpellGrants!, g => g.Kind == DaggerfallSpellGrantKind.Vampirism);
        s.CureLycanthropy();
        Assert.Equal(original.SelectedMedia, CharacterIdentityPresentation.From(TestPayload.Definitions, s.State.Character.Identity, null).SelectedMedia);
    }

    [Fact]
    public void Hungry_rest_is_refused_without_time_advance_and_feeding_restores_eligibility()
    {
        using var s = FreshSession();
        s.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "vampire", Minute(s), vampireClan: 153);
        s.AdvanceElapsedTime(1441 * 60);
        var refused = s.ApplyRest(new(DaggerfallRestMode.Timed, 1), new(true), _ => throw new InvalidOperationException("Refused rest must not advance time"));
        Assert.Contains("feed", refused.Message);
        s.State.RacialOverrides.Feed(Minute(s));
        var allowed = s.ApplyRest(new(DaggerfallRestMode.Timed, 1), new(true), seconds => new(seconds, seconds));
        Assert.NotEqual(refused.Message, allowed.Message);
    }

    [Fact]
    public void Clan_and_cure_source_programs_are_supported()
    {
        var sources = TestPayload.Definitions.QuestSources.Quests.Values.Where(source => source.Name.StartsWith("P0") || source.Name == "$CUREVAM");
        List<string> diagnostics = [];
        foreach (var source in sources) diagnostics.AddRange(DaggerfallQuestTaskCompiler.Assess(source).Select(d => source.Name + ": " + d.Text));
        Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics));
        Assert.Equal(11, sources.Count());
    }

    [Fact]
    public void Initial_clan_and_cure_quests_start_real_sources_and_cure_ends_active_clan_work()
    {
        using var f = new Fixture(); var s = f.Session;
        s.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "vampire", Minute(s), vampireClan: 153);
        Assert.True(s.StartVampireQuestOpportunity(Minute(s), false), s.Presentation.LastOutcome);
        Assert.True(s.State.RacialOverrides.Current!.State.Vampire!.InitialQuestStarted);
        var initial = Assert.Single(s.State.Quests.All);
        Assert.Equal("P0A01L00.txt", initial.SourceFile);
        Assert.False(s.StartVampireQuestOpportunity(Minute(s), false));
        using var restored = f.Restore();
        Assert.True(restored.State.RacialOverrides.Current!.State.Vampire!.InitialQuestStarted);
        var work = restored.State.Quests.OrdinaryWorkPool(153, true, 20, 100, 20, DaggerfallCharacterGender.Male);
        Assert.Equal(9, work.Length);
        Assert.All(work, row => Assert.Equal("Vampires", row.Group));
        Assert.True(restored.StartVampireQuestOpportunity(Minute(restored) + 1, false), restored.Presentation.LastOutcome);
        Assert.Contains(restored.State.Quests.All, quest => work.Any(row => row.Name + ".txt" == quest.SourceFile));
        Assert.True(restored.StartVampireQuestOpportunity(Minute(restored), true), restored.Presentation.LastOutcome);
        Assert.Contains(restored.State.Quests.All, q => q.SourceFile == "$CUREVAM.txt");
        Assert.True(restored.CureVampirism());
        Assert.NotEqual(DaggerfallQuestLifecycle.Active, restored.State.Quests.All.Single(q => q.InstanceId == initial.InstanceId).Lifecycle);
    }

    [Fact]
    public void Actual_melee_hit_feeds_and_ordinary_missed_attack_does_not()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        s.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "vampire", Minute(s), vampireClan: 153);
        s.AdvanceElapsedTime(1441 * 60);
        long hungry = s.State.RacialOverrides.Current!.State.Vampire!.LastFedMinute;
        var stamina = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")); stamina.SetCurrent(stamina.Maximum.Value);
        var player = s.State.PlayerControl.Position!.Value; var enemy = s.State.Actors.Get(f.Enemy);
        enemy.ApplyPose(new(new WorldPoint(player.X,player.Y,player.Z-0.1f),0));
        var contact = new ForceContact();
        s.State.Kit.Rules.RegisterAction(s.DefinitionsByActor[1].ActionId!, contact);
        s.ResolveExplicitMelee(new(1,f.Enemy,1,10000,.125));
        Assert.Equal(hungry,s.State.RacialOverrides.Current!.State.Vampire!.LastFedMinute);
        contact.HitResult = true;
        s.ResolveExplicitMelee(new(1,f.Enemy,1,20000,.125));
        Assert.True(Minute(s) == s.State.RacialOverrides.Current!.State.Vampire!.LastFedMinute, s.Presentation.LastOutcome);
    }

    private sealed class ForceContact : WorldRpg.Kit.Combat.ICombatContribution
    {
        internal bool HitResult;
        public void Hit(WorldRpg.Kit.Combat.TryHitEvent value) => value.Hit=HitResult;
        public void Damage(WorldRpg.Kit.Combat.DamageEvent value) => value.Damage=1;
    }

    private static long Minute(DaggerfallSession s)
    {
        var c = DaggerfallSavePayload.Read(s.CaptureSave()).Calendar;
        return new DaggerfallCalendar(c.Year,c.Month,c.Day,c.Hour,c.Minute,c.Second).ToAbsoluteSeconds()/60;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition composition;
        private readonly DaggerfallSiteProfile[] profiles;
        internal DaggerfallSiteProfile[] Profiles => profiles;
        internal DaggerfallSession Session { get; }
        internal Fixture()
        {
            var root = TestData.RepositoryRoot; var definitions = TestPayload.Definitions;
            var files = FullContent(root, "worldrpg/imports/privateers-hold", "worldrpg/imports/the-hawkston-cemetery", "worldrpg/imports/the-hawkston-cemetery/exterior");
            profiles = [ReadInputs(root), ReadProfile(root, files, definitions, "daggerfall.the-hawkston-cemetery.json"),
                ReadProfile(root, files, definitions, "daggerfall.the-hawkston-cemetery-exterior.json")];
            composition = new(definitions, profiles[0], DaggerfallTuning.Defaults) { Profiles = new(profiles), VideosEnabled = false,
                Blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(root,"content/worldrpg/payloads/daggerfall.blocks.json"))) };
            Session = DaggerfallSession.StartNew(Engine().Context, composition);
        }
        private EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases);
            foreach (var profile in profiles) PopulateContent(content, profile);
            var spatial = SpatialFake.Create(profiles[0].SpatialArtifact.Sha256, releases);
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: VampireRandom.Create());
        }
        internal DaggerfallSession Restore() => DaggerfallSession.Restore(Engine().Context, composition, Session.CaptureSave());
        public void Dispose() => Session.Dispose();
    }
}

internal class VampireRandom : SummonRandom
{
    internal new static IRandomService Create() => Create<IRandomService,VampireRandom>();
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        if (method?.Name == nameof(IRandomService.DrawKeyed) && args![0] is KeyedRngRequest request
            && request.ToString().Contains("daggerfall.vampirism") && request.ToString().Contains(":quest"))
            return new KeyedRngReceipt(request.Minimum);
        return base.Invoke(method,args);
    }
}
