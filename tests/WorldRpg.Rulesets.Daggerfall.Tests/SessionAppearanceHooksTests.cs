using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class SessionAppearanceHooksTests
{
    [Fact]
    public void Every_projection_uses_live_racial_form_and_hearing_across_building_entry_and_return()
    {
        string root = TestData.RepositoryRoot;
        var definitions = TestPayload.Definitions;
        var admitted = FullContent(root);
        var exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        var interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0.json");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior); PopulateContent(content, interior);
        var spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        var audio = AudioRecorder.Create();
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), audioService: audio.Service);
        var tuning = DaggerfallTuning.Defaults with { PresentationAudio = DaggerfallTuning.Defaults.PresentationAudio with {
            VampireAttackChancePercent = 100, VampireBarkChancePercent = 100, BeastAttackChancePercent = 100 } };
        using var session = DaggerfallSession.StartNew(engine.Context, new(definitions, exterior, tuning) {
            Profiles = new DaggerfallSiteProfiles([exterior, interior]) });
        CareerPassiveSessionTests.SetCareer(session, [new("acute-hearing")]);
        ulong step = 1;
        foreach (var profile in new[] { exterior, interior, exterior })
        {
            if (session.Sites.ActiveProfile != profile.ProfileKey) Assert.True(session.TryTransitionTo(profile.ProfileKey));
            var appearance = session.Sites.Projection.Appearance;
            long enemy = session.SpawnActor("rat", new(new(2, 0, 2), 0));
            appearance.UpdateRightHandEquipment(session.State.Equipment.Read(), weaponDrawn: false);

            // The same appearance reads changes in the session, rather than capturing its initial form.
            Assert.True(session.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "voice", 0, replace: true, vampireClan: 153));
            int beforeVoice = audio.Emits.Count;
            appearance.React(new PlayerAttackStartedFact(1, step++) { Feedback = new(false, "") });
            Assert.Contains(audio.Emits.Skip(beforeVoice), cue => cue.SignalId.EndsWith(".sound.205", StringComparison.Ordinal));

            Assert.True(session.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "claws", 0, replace: true));
            session.State.RacialOverrides.SetBeastForm(true, 0);
            int beforeClaws = audio.Emits.Count;
            appearance.React(new AttackHitFact(1, enemy, 5, 5, 0, false, 1, step++), session.State.Actors);
            Assert.Contains(audio.Emits.Skip(beforeClaws), cue => cue.SignalId.EndsWith(".sound.144", StringComparison.Ordinal));
            Assert.Equal(20F, audio.Voices.Last().MaxDistance);
        }
    }
}
