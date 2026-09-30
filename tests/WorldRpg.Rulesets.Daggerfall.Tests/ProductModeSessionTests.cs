using System.Numerics;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Product modes over the real session: the realtime update, the opening, pause, entry screen and background commit.</summary>
public sealed class ProductModeSessionTests
{
    [Fact]
    public void Host_admits_one_realtime_update_and_releases_the_normalized_session_owners()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        List<string> releases = [];
        ContentFake content = new(releases);
        DaggerfallSiteProfile inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        // This lifecycle seam is about one admitted realtime step and owner release ordering. Keep
        // the opening-media policy explicit so a cinematic waiting for terminal Engine facts cannot
        // make the first world-step assertion depend on playback timing.
        CapturingDaggerfallRuleset ruleset = new(videosEnabled: false);

        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.privateers-hold")))
        {
            product.Start();
            // The product a launcher starts shows its entry screen, and a client that has read it asks to
            // begin; the admitted update under test is the first one that reaches the world.
            Assert.Equal(ProductMode.Title, product.Mode);
            product.Begin();
            Assert.Equal(ProductMode.Playing, product.Mode);
            ProductUpdateFacts facts = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d);
            Assert.Equal(ProductUpdateResult.None, product.Update(new ProductUpdate(facts, ReadOnlySpan<ProductInputEvent>.Empty)));
            Assert.Equal(1, spatial.StepCalls);
            product.Shutdown();
        }

        Assert.Equal(1, engine.UiOpenCalls);
        Assert.True(releases.IndexOf("session") < releases.LastIndexOf("content"));
    }

    [Fact]
    public void Enabled_production_opening_refuses_to_begin_when_its_admitted_cinematic_bundle_is_missing()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake engineContent = new(releases);
        // This ordinary Engine fixture deliberately excludes the cinematic bundle. Unlike the explicit
        // no-video fixture used by unrelated gameplay tests, production ruleset startup must surface it.
        PopulateContent(engineContent, inputs);
        EngineContextFake engine = EngineContextFake.Create(engineContent,
            SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new(videosEnabled: true);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.privateers-hold"));

        product.Start();
        ProductModeChange result = product.Begin();

        Assert.Equal(ProductMode.Title, product.Mode);
        Assert.Equal(ProductModeChangeOutcome.Refused, result.Outcome);
        Assert.Contains("could not start its opening sequence", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ordinary_product_plays_its_opening_through_Engine_video_facts_then_begins_play_with_its_music_cue()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake engineContent = new(releases);
        PopulateContent(engineContent, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        VideoRecorder video = VideoRecorder.Create();
        EngineContextFake engine = EngineContextFake.Create(engineContent, spatial.Service, new AppearanceFake(releases), video: video.Service);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        // The one ordinary product entry: the Host's default bundle over the committed content, staged as
        // the Host declares it (the cinematics included), and the built-in ruleset with its videos on.
        ProductContent staged = StagedContent(root, out BundleContentFake bundles);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, staged, input));

        product.Start();
        Assert.Equal(ProductMode.Title, product.Mode);
        // The entry screen's own action starts the opening rather than play: the first cinematic is
        // playing and the product waits at its entry screen for the Engine to report it finished.
        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"begin\"}")]));
        Assert.Equal(ProductMode.Title, product.Mode);
        Assert.Single(video.Played);
        Assert.Equal(0, spatial.StepCalls);

        // Each completion the Engine reports advances to the next cinematic on the next admitted update,
        // and the product stays at its entry screen until the last one completes.
        ulong step = 1;
        for (int played = 1; played < 3; played++)
        {
            video.Complete(video.Played[^1]);
            product.Update(new ProductUpdate(OuterUpdate(++step), []));
            Assert.Equal(played + 1, video.Played.Count);
            Assert.Equal(ProductMode.Title, product.Mode);
        }
        Assert.Equal(
            [("daggerfall.cinematics", "anim0000.webm"), ("daggerfall.cinematics", "anim0011.webm"), ("daggerfall.cinematics", "dag2.webm")],
            bundles.OpenedReferences.Where(reference => reference.Bundle == "daggerfall.cinematics"));

        video.Complete(video.Played[^1]);
        product.Update(new ProductUpdate(OuterUpdate(++step), []));
        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Equal(ProductModeChangeOutcome.Applied, product.ModeHistory[^1].Outcome);
        Assert.Contains("opening sequence", product.ModeHistory[^1].Reason, StringComparison.Ordinal);
        Assert.Equal(3, video.Played.Count);

        // Ordinary play takes world steps and plays the site's published cue as one retained loop.
        product.Update(new ProductUpdate(OuterUpdate(++step), []));
        Assert.True(spatial.StepCalls > 0, "ordinary play admits world time");
        Assert.Contains("daggerfall.music/cue.started: Music cue 'song_dungeon' started for context 'Dungeon'.", engine.PublishedDiagnostics);
        Assert.Single(engine.StartedAudioVoices);
        Assert.Equal(0, engine.ReleasedAudioVoices);
        // The cue's body came out of the staged music bundle, which is how the Engine serves it.
        Assert.Contains(bundles.OpenedReferences, reference => reference.Bundle == "daggerfall.music");
    }

    [Fact]
    public void Stamina_recovery_is_held_back_outside_ordinary_play_and_resumes_with_it()
    {
        using DaggerfallSession session = FreshSession();
        StatsComponent mechanics = session.State.Actors.Player.Stats;
        TrackId stamina = TrackId.Parse("stamina");

        // Ordinary play recovers stamina over admitted world time, which is the calibration: without
        // it the held-back assertion below would pass on a mechanic that never runs at all.
        double maximum = mechanics.GetTrack(stamina).MaximumValue;
        mechanics.GetTrack(stamina).SetCurrent(1, clamp: true);
        // Recovery is per second against an integer track, so one step of a sixtieth recovers less
        // than one unit: the calibration has to give the mechanic enough admitted time to show.
        for (ulong step = 1; step <= 120; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        double recovered = mechanics.GetTrack(stamina).Current;
        Assert.True(recovered > 1, $"ordinary play should recover stamina, but it stayed at {recovered}");

        // A modal holds the world still, so the same amount of admitted time recovers nothing.
        mechanics.GetTrack(stamina).SetCurrent(1, clamp: true);
        session.ApplyProductMode(ProductMode.Modal);
        for (ulong step = 121; step <= 240; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.Equal(1d, mechanics.GetTrack(stamina).Current);

        // And ordinary play resumes it, so the gate is a gate rather than a stopped mechanic.
        session.ApplyProductMode(ProductMode.Playing);
        for (ulong step = 241; step <= 360; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.True(mechanics.GetTrack(stamina).Current > 1);
        Assert.True(mechanics.GetTrack(stamina).Current <= maximum);
    }

    [Fact]
    public void Paused_mode_holds_enemy_facts_stamina_and_sprite_playback_until_play_resumes()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        Track stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina"));
        stamina.SetCurrent(1, clamp: true);

        session.ApplyProductMode(ProductMode.Paused);
        int factReactionsBefore = appearance.ControlRequests.Count;
        int playbackAdvancesBefore = appearance.AdvanceRequests.Count;
        AppearanceFact[] snapshotBefore = appearance.Snapshots[^1];
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        // A held update republishes the existing scene for the mode UI, but does not simulate an
        // enemy, deliver its EnemyAttackStartedFact into appearance, recover stamina, or advance
        // any sprite playback.
        Assert.Empty(session.LastEnemyBehavior);
        Assert.Equal(1d, stamina.Current);
        Assert.Equal(factReactionsBefore, appearance.ControlRequests.Count);
        Assert.Equal(playbackAdvancesBefore, appearance.AdvanceRequests.Count);
        Assert.Equal(snapshotBefore, appearance.Snapshots[^1]);

        // Ordinary play is the sensitivity control: the same perception now runs behavior, its
        // delivered attack-start fact drives appearance, recovery advances, and the outer path
        // advances playback.
        session.ApplyProductMode(ProductMode.Playing);
        // Complete each authored swing so this fixture does not leave a hit marker pending forever.
        appearance.AdvanceReceiptForAll = CompletedMarker(1);
        for (ulong step = 2; step <= 121; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[2000].State);
        Assert.True(stamina.Current > 1d);
        Assert.True(appearance.ControlRequests.Count > factReactionsBefore);
        Assert.True(appearance.AdvanceRequests.Count > playbackAdvancesBefore);
    }

    [Fact]
    public void Title_background_commit_materializes_biography_grants_once_and_restores_them()
    {
        static ProductInputEvent Ui(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };

        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases), random: RandomMinimum.Create());
        RulesetSavePayload saved;
        DaggerfallCharacterBackgroundSave committed;
        ulong goldAfterCommit;
        using (DaggerfallSession session = DaggerfallSession.StartNew(source.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
            session.ApplyProductMode(ProductMode.Title);
            ulong goldBefore = Gold(session);
            session.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"character-begin\"}")]));
            session.Update(new ProductUpdate(OuterUpdate(2), [Ui("{\"action\":\"character-background-reroll\",\"name\":\"Nameless\",\"race\":\"breton\",\"gender\":\"male\",\"faceIndex\":0,\"reflexes\":2,\"career\":\"class00\"}")]));
            session.Update(new ProductUpdate(OuterUpdate(3), [Ui("{\"action\":\"character-cancel\"}")]));
            Assert.Equal(goldBefore, Gold(session));
            session.Update(new ProductUpdate(OuterUpdate(4), [Ui("{\"action\":\"character-begin\"}")]));
            DaggerfallCharacterBackgroundSave rolled = Assert.IsType<DaggerfallCharacterBackgroundSave>(session.State.Character.Pending!.Background);
            DaggerfallBiographyDefinition biography = definitions.Biographies.Biographies.Single(value => value.ClassIndex == rolled.BiographyClassIndex);
            DaggerfallBiographyAnswerSave[] answers = biography.Questions.Select(question => new DaggerfallBiographyAnswerSave(question.Number, question.Answers[0].Letter)).ToArray();
            Select(DaggerfallBiographyEffectKind.Gold);
            Select(DaggerfallBiographyEffectKind.Item);
            Select(DaggerfallBiographyEffectKind.FactionReputation);
            Select(DaggerfallBiographyEffectKind.SocialReputation);
            Select(DaggerfallBiographyEffectKind.Skill);
            SelectModifier("RD");
            DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
            DaggerfallCharacterBackgroundSave complete = DaggerfallCharacterBackgroundPolicy.Update(definitions, career, session.State.Character.Pending!.ToIdentity(), rolled, answers,
                [new DaggerfallCreationAllocationSave(career.Attributes[0], rolled.AttributeBonusPool)],
                [new DaggerfallCreationAllocationSave(career.PrimarySkills[0], 6), new DaggerfallCreationAllocationSave(career.MajorSkills[0], 6), new DaggerfallCreationAllocationSave(career.MinorSkills[0], 6)]);
            int faction = DaggerfallCharacterBackgroundPolicy.FactionReputations(definitions, career, complete).First().Faction;
            int factionBefore = session.State.Social.FactionReputation(faction);
            int social = DaggerfallCharacterBackgroundPolicy.SocialReputations(definitions, career, complete).First().Group;
            int socialBefore = session.State.Social.PersonalReputation(social);
            session.State.Character.ReplacePending(session.State.Character.Pending! with { Background = complete });
            session.Update(new ProductUpdate(OuterUpdate(5), [Ui("{\"action\":\"character-commit\",\"name\":\"Nameless\",\"race\":\"breton\",\"gender\":\"male\",\"faceIndex\":0,\"reflexes\":2,\"career\":\"class00\"}")]));

            committed = Assert.IsType<DaggerfallCharacterBackgroundSave>(session.State.Character.Background);
            Assert.NotEmpty(committed.StartingGrants);
            ulong grantedGold = committed.StartingGrants.Where(grant => grant.ItemId == "template-276").Aggregate(0UL, (total, grant) => checked(total + grant.Quantity));
            goldAfterCommit = Gold(session);
            Assert.Equal(goldBefore + grantedGold, goldAfterCommit);
            Assert.True(session.State.Inventory.Read().Stacks.Any(item => committed.StartingGrants.Any(grant => grant.ItemId == item.Definition.Value))
                || session.State.Inventory.Read().UniqueItems.Any(item => committed.StartingGrants.Any(grant => grant.ItemId == item.Definition.Value)));
            Assert.True(session.State.Social.FactionReputation(faction) > factionBefore);
            Assert.True(session.State.Social.PersonalReputation(social) > socialBefore);
            Assert.Equal(-5, committed.Modifiers.DiseaseResistance);
            Assert.Equal(committed.RolledSkills.Single(skill => skill.Id == career.PrimarySkills[0]).Points + 6,
                session.State.Actors.Player.Stats.GetStat(StatId.Parse(career.PrimarySkills[0])).BaseValue);

            session.Update(new ProductUpdate(OuterUpdate(6), [Ui("{\"action\":\"character-begin\"}")]));
            session.Update(new ProductUpdate(OuterUpdate(7), [Ui("{\"action\":\"character-commit\",\"name\":\"Nameless\",\"race\":\"breton\",\"gender\":\"male\",\"faceIndex\":0,\"reflexes\":2,\"career\":\"class00\"}")]));
            Assert.Equal(goldAfterCommit, Gold(session));
            saved = session.CaptureSave();

            void Select(DaggerfallBiographyEffectKind kind)
            {
                (DaggerfallBiographyQuestionDefinition question, DaggerfallBiographyAnswerDefinition answer) = biography.Questions
                    .SelectMany(question => question.Answers.Select(answer => (question, answer)))
                    .First(value => value.answer.Effects.Any(effect => effect.Kind == kind));
                answers[Array.FindIndex(answers, answer => answer.Question == question.Number)] = new(question.Number, answer.Letter);
            }
            void SelectModifier(string code)
            {
                (DaggerfallBiographyQuestionDefinition question, DaggerfallBiographyAnswerDefinition answer) = biography.Questions
                    .SelectMany(question => question.Answers.Select(answer => (question, answer)))
                    .First(value => value.answer.Effects.Any(effect => effect.Kind == DaggerfallBiographyEffectKind.BiographyModifier && effect.First == code));
                answers[Array.FindIndex(answers, answer => answer.Question == question.Number)] = new(question.Number, answer.Letter);
            }
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), random: RandomMinimum.Create());
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), saved);
        Assert.Equal(committed.Biography, resumed.State.Character.Background!.Biography);
        Assert.Equal(committed.Modifiers, resumed.State.Character.Background!.Modifiers);
        Assert.Equal(goldAfterCommit, Gold(resumed));

        static ulong Gold(DaggerfallSession session) => session.State.Inventory.Read().Stacks.Where(stack => stack.Definition.Value == "template-276").Aggregate(0UL, (total, stack) => checked(total + stack.Quantity));
    }

    /// <summary>
    /// The entry-screen mode the title screen owns: while it holds, no world time and no gameplay input
    /// reach the session, it asks the product for nothing on its own, and the projection names it so the
    /// thin UI knows which screen the mode is showing.
    /// </summary>
    [Fact]
    public void The_entry_screen_mode_holds_the_world_and_names_itself_on_the_wire()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));

        // The product decides the mode, which is what the session applies.
        session.ApplyProductMode(ProductMode.Title);
        Assert.Equal(ProductMode.Title, session.Mode);

        // The screen is up, so the world is not: an admitted update with held movement in it takes no
        // step, and the projection still publishes so the screen can be read.
        int stepsBefore = spatial.StepCalls;
        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Assert.Equal(stepsBefore, spatial.StepCalls);
        Assert.Equal("title", engine.PublishedField("mode"));

        // The session asks the product for nothing while the entry screen holds: it has no loot container
        // to follow, and the mode is the product's own decision rather than one it reports. (The case with
        // a container open is the modal test's, which has the corpse this fixture does not: a container
        // cannot be opened from a world that has not started, so the state it protects is unreachable
        // here and is covered where it is reachable.)
        Assert.Null(session.PendingModeRequest);

        // A gameplay action read while the entry screen holds is dropped rather than acted on, so a key
        // pressed during the screen cannot land in the world behind it.
        ProductInputEvent loot = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes("""{"action":"loot"}"""),
        };
        session.Update(new ProductUpdate(OuterUpdate(2), [loot]));
        Assert.Null(session.OpenLoot);
        Assert.Equal(ProductMode.Title, session.Mode);
        Assert.Equal(stepsBefore, spatial.StepCalls);

        // A status line would compete with the screen that is up, so the entry screen says nothing.
        Assert.Equal(string.Empty, engine.PublishedField("lastOutcome"));

        // A slice carrying the entry screen's own action is a shape this knows and does not act on rather
        // than an unrecognized one: the product answers it, and reporting it over the screen that asked
        // would put a message on the entry screen for doing the one thing the entry screen does.
        ProductInputEvent begin = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes("""{"action":"begin"}"""),
        };
        session.Update(new ProductUpdate(OuterUpdate(3), [begin]));
        Assert.DoesNotContain("Unrecognized", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);
        Assert.Equal(ProductMode.Title, session.Mode);
        Assert.Equal(stepsBefore, spatial.StepCalls);

        // Leaving the entry screen is the product's transition, and ordinary play resumes under it.
        session.ApplyProductMode(ProductMode.Playing);
        Assert.Equal("playing", engine.PublishedField("mode"));
        session.Update(new ProductUpdate(OuterUpdate(4), []));
        Assert.True(spatial.StepCalls > stepsBefore, "ordinary play admits world time again");
    }

    /// <summary>A session whose one placed enemy can see the player at the given distance.</summary>
    [Fact]
    public void Product_modes_gate_gameplay_input_and_world_time_while_the_modal_keeps_acting()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        AimActivationAt(session, 2000);
        CorpseContainer corpse = session.Corpses[2000];
        Assert.True(corpse.IsRegistered);
        session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.4534"))]);
        RegisterCorpseStack(session, definitions, 2000, "test.loot.4534");
        // The pair the passing attack test uses, so the melee assertions below can actually fire.
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        void Ui(string json, ulong step)
        {
            ProductInputEvent action = Input(InputEventKind.DirectDigital) with
            {
                ValueKind = InputValueKind.ProductPayload,
                PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
                PayloadData = Encoding.UTF8.GetBytes(json),
            };
            session.Update(new ProductUpdate(OuterUpdate(step), [action]));
        }

        // The appearance starts with the weapon drawn, so the attack assertions below test the mode
        // gate rather than an unarmed player. Intent drives the ordinary input path.
        void Intent(string intent, ulong step) =>
            session.Update(new ProductUpdate(OuterUpdate(step), [Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: intent)]));

        // Nothing owns input, so the session asks the product for nothing.
        Assert.Null(session.PendingModeRequest);

        // The loot key opens a container, and that is what the session reports: it can open an
        // interaction the product cannot see, so it asks rather than deciding.
        Ui("{\"action\":\"loot\"}", 2);
        Assert.Equal(ProductMode.Modal, session.PendingModeRequest);

        // The product decides, and the session applies it.
        session.ApplyProductMode(ProductMode.Modal);
        Assert.Equal(ProductMode.Modal, session.Mode);

        // A product that holds the world somewhere other than play asks for nothing, even with this
        // container open: the request follows the container, so a held world with one open would otherwise
        // ask to be put back into a modal - or into play - behind the product's back. The entry screen is
        // that held world here; a pause is the same fact with a different mode.
        session.ApplyProductMode(ProductMode.Title);
        Assert.NotEqual(ProductMode.Modal, session.PendingModeRequest);
        Assert.NotEqual(ProductMode.Playing, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Paused);
        Assert.NotEqual(ProductMode.Modal, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Modal);

        // Held movement reaches no world step while a modal owns input, and the modal's own action
        // still lands: the gold moves even though the world does not.
        int stepsBeforeModal = spatial.StepCalls;
        ulong revisionBefore = session.State.Inventory.Read().StoreRevision;
        session.Update(new ProductUpdate(OuterUpdate(3), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Assert.Equal(stepsBeforeModal, spatial.StepCalls);

        // An attack asked for while the modal owns input reaches no combat: the per-step update that
        // would run it is the one the mode holds back, which is the same gate the step assertion
        // above measures. (An outer-path melee cannot be observed landing in this fixture even in
        // ordinary play - the existing melee test drives the internal per-step seam - so the attack
        // half is stated as this structural fact rather than claimed as an executed refusal.)
        Intent("attack", 4);
        Assert.Null(session.LastMeleeTargeting);
        Assert.Equal(stepsBeforeModal, spatial.StepCalls);
        LootPresentation opened = Assert.IsType<LootPresentation>(session.OpenLoot);
        InventoryItemPresentation gold = opened.Items.Single(item => item.Key == DaggerfallInventoryPresentation.StackKey(InventoryStackId.Parse("test.loot.4534")));

        // The projection the thin UI renders carries the mode the product decided and the token a
        // close has to name, so the UI keeps no focus authority of its own.
        Assert.Equal("modal", engine.PublishedField("mode"));
        Assert.Equal(opened.Container, engine.PublishedNested("focus", "container"));
        Assert.Equal("loot-close", engine.PublishedNested("focus", "close"));
        Assert.Equal("modal", engine.PublishedNested("view", "interaction"));

        // A status row an owner publishes reaches the projection without the projection knowing
        // what it means: this is the slot path effects, escorts and quests will use.
        session.Slots.Publish(new PresentationSlot("effect.poison", "tick", "Poisoned", "3 damage", 10));
        session.ApplyProductMode(ProductMode.Playing);
        session.ApplyProductMode(ProductMode.Modal);
        Assert.Equal("Poisoned", engine.PublishedArrayItem("slots", 0, "label"));
        Assert.Equal("effect.poison", engine.PublishedArrayItem("slots", 0, "owner"));
        Ui(System.Text.Json.JsonSerializer.Serialize(new { action = "loot-take", container = opened.Container, revision = opened.Revision, item = gold.Key }), 4);
        Assert.NotEqual(revisionBefore, session.State.Inventory.Read().StoreRevision);

        // Closing through the interaction's own token is what returns the session to ordinary play,
        // and it asks the product for it rather than deciding for itself.
        Ui(System.Text.Json.JsonSerializer.Serialize(new { action = "loot-close", container = opened.Container }), 4);
        Assert.Equal(ProductMode.Playing, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Playing);
        Assert.Null(session.OpenLoot);
        Assert.Equal("playing", engine.PublishedField("mode"));
        Assert.Null(engine.PublishedNested("focus", "container"));


        session.Update(new ProductUpdate(OuterUpdate(5), []));
        Assert.True(spatial.StepCalls > stepsBeforeModal, "ordinary play admits world time again");
        Assert.Equal(Vector2.Zero, spatial.StepRequests[^1].Command.PlanarIntent);
        session.Update(new ProductUpdate(OuterUpdate(6), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Assert.True(spatial.StepCalls > stepsBeforeModal, "ordinary play admits world steps again");
        Assert.NotEqual(Vector2.Zero, spatial.StepRequests[^1].Command.PlanarIntent);

        // A player whose health track reached zero is dead, whatever mode they were in, and death
        // admits no world time either. The negative is a *valid* take - the same container token and
        // revision that moved the gold while the modal was open - so it would move it again if the
        // dead gate were missing.
        session.Update(new ProductUpdate(OuterUpdate(6), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        Ui("{\"action\":\"loot\"}", 7);
        LootPresentation reopened = Assert.IsType<LootPresentation>(session.OpenLoot);
        session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        Assert.Equal(ProductMode.Dead, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Dead);
        int stepsBeforeDeath = spatial.StepCalls;
        ulong afterDeath = session.State.Inventory.Read().StoreRevision;
        InventoryItemPresentation remaining = reopened.Items.Single(item => item.Key == gold.Key);
        session.Update(new ProductUpdate(OuterUpdate(8), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Ui(System.Text.Json.JsonSerializer.Serialize(new { action = "loot-take", container = reopened.Container, revision = reopened.Revision, item = remaining.Key }), 9);
        Assert.Equal(stepsBeforeDeath, spatial.StepCalls);
        Assert.Equal(afterDeath, session.State.Inventory.Read().StoreRevision);

        // A dead product advertises no closable interaction: its own gate would ignore the close, so
        // offering the token would be a control that silently does nothing. The contents panel is
        // the same affordance and is withheld for the same reason.
        Assert.Equal("dead", engine.PublishedField("mode"));
        Assert.Null(engine.PublishedNested("focus", "container"));
        Assert.Null(engine.PublishedNested("loot", "container"));
        Assert.Equal(ulong.Parse(remaining.Quantity), ulong.Parse(session.OpenLoot!.Items.Single(item => item.Key == remaining.Key).Quantity));

    }
}
