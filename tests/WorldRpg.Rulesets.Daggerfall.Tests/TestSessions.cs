using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Session-building helpers shared by the ruleset session suites: content, inputs, updates and receipts.</summary>
internal static class TestSessions
{
    internal static readonly ContentSha256 Hash = new(1, 2, 3, 4);

    internal static DaggerfallSavePayload CapturedSave(string root)
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        return DaggerfallSavePayload.Read(session.CaptureSave());
    }

    /// <summary>A session that has not swung, so its weapon is ready.</summary>
    internal static DaggerfallSession FreshSession(DaggerfallEffectCatalog? effects = null)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        return effects is null
            ? DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults))
            : DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults) { Effects = effects });
    }

    internal static LookReceipt ForwardLook() => new(default, default, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);

    /// <summary>Places the fixture player inside the Engine interaction cone for the named world target.</summary>
    internal static void AimActivationAt(DaggerfallSession session, long actorId)
    {
        WorldPoint target = session.State.Actors.Get(actorId).Position;
        session.State.PlayerControl.MoveTo(target.ToVector() + Vector3.UnitZ);
        session.State.PlayerControl.YawRadians = 0f;
        session.State.PlayerControl.PitchRadians = 0f;
    }

    internal static void AimActivationAt(DaggerfallSession session, WorldPoint target)
    {
        session.State.PlayerControl.MoveTo(target.ToVector() + Vector3.UnitZ);
        session.State.PlayerControl.YawRadians = 0f;
        session.State.PlayerControl.PitchRadians = 0f;
    }

    internal static ProductInputEvent Ui(string payload) => Input(InputEventKind.DirectDigital) with
    {
        ValueKind = InputValueKind.ProductPayload,
        PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
        PayloadData = Encoding.UTF8.GetBytes(payload),
    };

    internal static PerceptionReadoutResult Receipt(params PerceptionPair[] pairs) => new(pairs, ReadOnlyMemory<PerceptionAggregate>.Empty, checked((uint)pairs.Length), false, 0, 1, 1, checked((uint)pairs.Length), checked((ulong)pairs.Length), 0, 0, 0, 0);

    internal static DaggerfallSiteProfile ReadInputs(string root)
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        return DaggerfallSiteContent.Read(ImportContent(root), File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);
    }

    internal static DaggerfallSiteProfile ReadProfile(string root, ProductContent content, DaggerfallDefinitions definitions, string payload) =>
        DaggerfallSiteContent.Read(content, File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads", payload)), definitions);

    internal static void RegisterCorpseStack(DaggerfallSession session, DaggerfallDefinitions definitions, long actorId, string stackId)
    {
        CorpseContainer corpse = session.Corpses[actorId];
        InventoryStackId id = InventoryStackId.Parse(stackId);
        InventoryStack stack = session.State.Containers.Read(corpse.Owner).Stacks.Single(value => value.Id == id);
        session.State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Corpse(actorId), stack,
            definitions.Items[new DaggerfallItemId(stack.Definition.Value)]);
    }

    internal static NormalizedActorSprite SpriteFor(DaggerfallSiteProfile inputs, string actorId) => inputs.ActorSprites.First(pair => inputs.Project.Actors[pair.Key].ActorId.Value == actorId).Value;

    internal static ProductContent ImportContent(string root) => ContentAt(root, "worldrpg/imports/privateers-hold");

    internal static ProductContent FullContent(string root)
    {
        (string Root, string Bundle)[] audioBundles =
        [
            // The score's clips are staged as their own bundle: the manifest that names them sits above
            // this root so it is read eagerly while the cue bodies stay lazy.
            ("worldrpg/media/music/clips", "daggerfall.music"),
            ("worldrpg/media/audio/clips", "daggerfall.classic-audio"),
            // Each site's clips are staged as the bundle its own payload declares.
            .. SiteAudioBundles(root),
        ];
        return ContentWithBundles(root, audioBundles, out _);
    }

    /// <summary>
    /// The content the SDK stages for the Host: every bundle the Host project declares is served lazily
    /// as that bundle, the cinematics included, and every other file sits in the eager snapshot.
    /// </summary>
    internal static ProductContent StagedContent(string root, out BundleContentFake bundles)
    {
        (string Root, string Bundle)[] declared = [.. XDocument.Load(Path.Combine(root, "src/WorldRpg.Host/WorldRpg.Host.csproj"))
            .Descendants("RustyEngineContentBundle")
            .Select(item => (item.Attribute("Root")!.Value, item.Attribute("Include")!.Value))];
        return ContentWithBundles(root, declared, out bundles);
    }

    private static ProductContent ContentWithBundles(string root, (string Root, string Bundle)[] declared, out BundleContentFake bundles)
    {
        string contentRoot = Path.Combine(root, "content");
        bundles = new();
        List<ProductContentFile> eager = [];

        foreach (string file in Directory.GetFiles(Path.Combine(contentRoot, "worldrpg"), "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(contentRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            (string Root, string Bundle) bundle = declared.FirstOrDefault(value => relative.StartsWith(value.Root + "/", StringComparison.Ordinal));
            if (string.IsNullOrEmpty(bundle.Root))
            {
                eager.Add(new ProductContentFile(Encoding.UTF8.GetBytes(relative), File.ReadAllBytes(file)));
                continue;
            }

            bundles.Add(bundle.Bundle, relative[(bundle.Root.Length + 1)..], File.ReadAllBytes(file));
        }

        return new ProductContent(eager.ToArray(), bundles);
    }

    /// <summary>The audio bundle every committed site payload declares, rooted at its publication's clips.</summary>
    internal static IEnumerable<(string Root, string Bundle)> SiteAudioBundles(string root)
    {
        foreach (string payload in Directory.GetFiles(Path.Combine(root, "content/worldrpg/payloads"), "*.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(payload));
            if (!document.RootElement.TryGetProperty("world", out JsonElement world) || !world.TryGetProperty("audioBundle", out JsonElement bundle)) continue;
            yield return ($"{world.GetProperty("publicationRoot").GetString()}/media/audio/clips", bundle.GetString()!);
        }
    }

    internal static ProductContent ContentAt(string root, string relativeDirectory)
    {
        string contentRoot = Path.Combine(root, "content");
        string selected = Path.Combine(contentRoot, relativeDirectory);
        return new ProductContent(Directory.GetFiles(selected, "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray());
    }

    internal static void PopulateContent(ContentFake content, DaggerfallSiteProfile inputs)
    {
        content.Add(inputs.SpatialArtifact.Path, inputs.SpatialArtifact.Sha256);
        content.Add(inputs.StaticMesh.Path, inputs.StaticMesh.Sha256);
        foreach (NormalizedMaterial material in inputs.Materials) content.Add(material.TexturePath, material.TextureSha256);
        foreach (NormalizedActorSprite sprite in inputs.ActorSprites.Values)
        {
            content.Add(sprite.TexturePath, sprite.TextureSha256);
            if (sprite.Corpse is { } corpse) content.Add(corpse.TexturePath, corpse.TextureSha256);
        }
        foreach (NormalizedAudioClip clip in inputs.Audio) content.Add(clip.Path, clip.Sha256);
        foreach (NormalizedClassicEffect effect in inputs.ClassicPresentation.Effects) content.Add(effect.TexturePath, effect.TextureSha256);
        foreach (NormalizedClassicWeapon weapon in inputs.ClassicPresentation.Weapons.Values) content.Add(weapon.TexturePath, weapon.TextureSha256);
        // The session resolves the DOM's art by media identity through the published group inventory,
        // so this fake serves the real group: its generated inventory and every artifact it describes.
        foreach ((string path, byte[] bytes) in PublishedUiArt(TestData.RepositoryRoot)) content.Add(path, bytes);
    }

    /// <summary>The published UI art group as admitted content: the inventory and the artifacts it names.</summary>
    internal static IEnumerable<(string Path, byte[] Bytes)> PublishedUiArt(string root)
    {
        string inventoryPath = Path.Combine(root, "content", DaggerfallUiArt.InventoryPath);
        byte[] inventory = File.ReadAllBytes(inventoryPath);
        yield return (DaggerfallUiArt.InventoryPath, inventory);
        using JsonDocument document = JsonDocument.Parse(inventory);
        foreach (JsonElement artifact in document.RootElement.GetProperty("artifacts").EnumerateArray())
        {
            string path = artifact.GetProperty("path").GetString()!;
            yield return (path, File.ReadAllBytes(Path.Combine(root, "content", path)));
        }
    }

    internal static ContentSha256 Digest(ReadOnlySpan<byte> bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return new ContentSha256(
            BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, 8)),
            BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(8, 8)),
            BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(16, 8)),
            BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(24, 8)));
    }

    internal static ContentFake MediaContent(List<string> releases)
    {
        ContentFake content = new(releases);
        content.Add("mesh/hold.json", Hash);
        content.Add("sprite/enemy.png", Hash);
        content.Add("audio/swing.wav", Hash);
        content.Add("audio/hit.wav", Hash);
        content.Add("audio/hit2.wav", Hash);
        content.Add("audio/hit3.wav", Hash);
        content.Add("audio/hit4.wav", Hash);
        content.Add("audio/hit5.wav", Hash);
        content.Add("effect/blood0.png", Hash);
        content.Add("effect/blood1.png", Hash);
        content.Add("effect/blood2.png", Hash);
        content.Add("effect/sparkle.png", Hash);
        content.Add("sprite/treasure.png", Hash);
        return content;
    }

    internal static DaggerfallSiteProfile MediaInputs(int primaryChance = 50, IReadOnlyList<int>? primaryFrames = null, bool includeAlternate = true, bool directional = false, IReadOnlyList<NormalizedAudioClip>? audio = null, string? preferredRestState = null, NormalizedClassicPresentation? classic = null, IReadOnlyList<NormalizedAtlasFrame>? actorFrames = null, IReadOnlyList<int>? rangedFrames = null, bool shortAttackDirection = false, NormalizedGroundContainerSprite? groundContainerSprite = null, DaggerfallActorFeedback? feedback = null, long spriteActorId = 11)
    {
        NormalizedSpriteState idle = new("idle", [0], 10F, true)
        {
            Orientations = directional
                ? Enumerable.Range(0, 8).ToDictionary(sector => sector, sector => (IReadOnlyList<uint>)(sector == 6 ? [1] : [0]))
                : new Dictionary<int, IReadOnlyList<uint>>(),
        };
        NormalizedSpriteState move = new("move", [0], 10F, true);
        NormalizedSpriteState hurt = new("hurt", [1], 10F, false);
        NormalizedSpriteState attack = new("primaryAttack", [2, 3], 10F, false)
        {
            Orientations = directional
                ? Enumerable.Range(0, 8).ToDictionary(sector => sector, sector => (IReadOnlyList<uint>)(sector == 6 && shortAttackDirection ? [3] : sector == 6 ? [3, 2] : [2, 3]))
                : new Dictionary<int, IReadOnlyList<uint>>(),
        };
        Dictionary<string, NormalizedSpriteState> states = new()
        {
            [idle.Name] = idle,
            [move.Name] = move,
            [hurt.Name] = hurt,
            [attack.Name] = attack,
        };
        if (preferredRestState is not null && !states.ContainsKey(preferredRestState)) states.Add(preferredRestState, new(preferredRestState, [0], 10F, true));
        if (rangedFrames is not null) states.Add("rangedAttack1", new("rangedAttack1", [0, 1, 2, 3], 10F, false));
        NormalizedActorSprite sprite = new("sprite/enemy.png", Hash, 32, 32,
            actorFrames ?? [new NormalizedAtlasFrame(0, 0, 0, 8, 8), new NormalizedAtlasFrame(1, 8, 0, 8, 8), new NormalizedAtlasFrame(2, 16, 0, 8, 8), new NormalizedAtlasFrame(3, 24, 0, 8, 8)],
            0, new Vector2(.5F, 0F), Vector2.One)
        {
            Feedback = feedback,
            States = states,
            PreferredRestState = preferredRestState,
            AttackSequences = includeAlternate
                ? [new NormalizedAttackSequence(100 - primaryChance, primaryFrames ?? [0]), new NormalizedAttackSequence(primaryChance, [1])]
                : [new NormalizedAttackSequence(100, primaryFrames ?? [0])],
            RangedAttackSequence = rangedFrames is null ? null : new NormalizedAttackSequence(100, rangedFrames, "rangedAttack1"),
        };
        return new DaggerfallSiteProfile(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            new SpatialContentArtifact("spatial/hold.json", Hash, 1),
            new ContentArtifact("mesh/hold.json", Hash),
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0),
            [],
            new Dictionary<long, NormalizedActorSprite> { [spriteActorId] = sprite },
            null,
            audio ??
            [
                new NormalizedAudioClip("swing", "audio/swing.wav", Hash),
                new NormalizedAudioClip("hit1", "audio/hit.wav", Hash),
                new NormalizedAudioClip("hit2", "audio/hit2.wav", Hash),
                new NormalizedAudioClip("hit3", "audio/hit3.wav", Hash),
                new NormalizedAudioClip("hit4", "audio/hit4.wav", Hash),
                new NormalizedAudioClip("hit5", "audio/hit5.wav", Hash),
            ],
            classic,
            groundContainerSprite: groundContainerSprite);
    }

    internal static NormalizedClassicPresentation ClassicEffects(IReadOnlyList<Vector2>? bloodDisplaySizes = null)
    {
        IReadOnlyList<NormalizedAtlasFrame> frames = [new NormalizedAtlasFrame(0, 0, 0, 8, 8)];
        Vector2 Size(int sourceRecordOrdinal) => sourceRecordOrdinal < 3 && bloodDisplaySizes is { Count: 3 } ? bloodDisplaySizes[sourceRecordOrdinal] : Vector2.One;
        NormalizedClassicEffect Effect(string name, int sourceRecordOrdinal, string path) => new(name, sourceRecordOrdinal, path, Hash, 8, 8, frames, new Vector2(.5F, .5F), Size(sourceRecordOrdinal), [0], 10F, false);
        return new NormalizedClassicPresentation(new Dictionary<string, NormalizedClassicWeapon>(), [Effect("blood0", 0, "effect/blood0.png"), Effect("blood1", 1, "effect/blood1.png"), Effect("blood2", 2, "effect/blood2.png"), Effect("magicSparkle", 3, "effect/sparkle.png")]);
    }

    internal static NormalizedClassicPresentation ClassicWeapon()
    {
        IReadOnlyList<NormalizedAtlasFrame> frames = [new NormalizedAtlasFrame(0, 0, 0, 8, 8)];
        string[] names = ["idle", "strikeDown", "strikeDownLeft", "strikeLeft", "strikeRight", "strikeDownRight", "strikeUp"];
        IReadOnlyDictionary<string, NormalizedClassicWeaponAction> actions = names.Select((name, sourceRecordOrdinal) => new NormalizedClassicWeaponAction(name, sourceRecordOrdinal, 0, 1, "right", name == "idle" ? .1F : .4F, 10F, name == "idle", 0, 0)).ToDictionary(action => action.Name);
        return new NormalizedClassicPresentation(new Dictionary<string, NormalizedClassicWeapon> { ["weapon.dagger.steel"] = new("weapon.dagger.steel", "weapon/dagger.png", Hash, 8, 8, frames, new Vector2(.5F, .5F), Vector2.One, [0], actions) }, [])
        {
            CompatibleItemVisuals = new Dictionary<string, string> { ["iron-dagger"] = "weapon.dagger.steel" },
            Viewmodel = new ClassicViewmodelStyle(0),
        };
    }

    /// <summary>One advance receipt reading the given frame of a playing sprite.</summary>
    internal static SpritePlaybackAdvanceResult Reading(uint frameId, uint frameIndex) =>
        new(default, new SpritePlaybackReadout(frameId, frameIndex, SpritePlaybackState.Playing, 0d, 0, 0, false), true);

    internal static DaggerfallSiteAppearance.ActorVisual Visual(DaggerfallSiteAppearance presentation)
    {
        FieldInfo field = typeof(DaggerfallSiteAppearance).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((Dictionary<long, DaggerfallSiteAppearance.ActorVisual>)field.GetValue(presentation)!)[11];
    }

    internal static ActorsState EmptyActors()
    {
        ActorsState actors = new();
        actors.CreatePlayer(99, new EntityTypeId("player"), new StatsComponent(), "health");
        return actors;
    }
    internal static WorldRpg.Kit.Inventory.EquipmentRead RightHand(string itemId) => new([new WorldRpg.Kit.Inventory.EquipmentAssignment(new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand"), new WorldRpg.Kit.Inventory.UniqueInventoryItem(1, new WorldRpg.Kit.Inventory.InventoryItemId(itemId)))], 1, 1);
    internal static ActorsState ActorsAt(WorldPoint point) => ActorsWithNpc(12, HealthyMechanics(), point);

    internal static ActorsState ActorsWithNpc(long durableId, StatsComponent stats, WorldPoint point)
    {
        ActorsState actors = new();
        actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), new StatsComponent(), "health");
        actors.CreateActor(durableId, new EntityTypeId("test"), stats, new ActorPose(point, 0f), "health");
        return actors;
    }

    internal static StatsComponent HealthyMechanics()
    {
        Stat maximum = new(100);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(maximum, 100));
        return stats;
    }

    internal static int EffectCount(DaggerfallSiteAppearance presentation) => ((List<DaggerfallSiteAppearance.EffectVisual>)typeof(DaggerfallSiteAppearance).GetField("effects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation)!).Count;
    internal static DaggerfallSiteAppearance.EffectVisual Effect(DaggerfallSiteAppearance presentation) => Assert.Single((List<DaggerfallSiteAppearance.EffectVisual>)typeof(DaggerfallSiteAppearance).GetField("effects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation)!);
    internal static DaggerfallSiteAppearance.ViewmodelVisual Viewmodel(DaggerfallSiteAppearance presentation) => Assert.IsType<DaggerfallSiteAppearance.ViewmodelVisual>(typeof(DaggerfallSiteAppearance).GetField("viewmodel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation));

    internal static (DaggerfallSession Session, AppearanceFake Appearance, PerceptionFake Perception) VisibleEnemySession(List<string> releases, double distance = 1d, Action<EngineContextFake>? engineCreated = null)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, distance, 1d, PerceptionPairKind.Visible, distance));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        engineCreated?.Invoke(engine);
        return (DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)), appearance, perception);
    }

    internal static long PlayerHealth(DaggerfallSession session) => session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).ValueInt64;

    internal static ProductUpdateFacts OuterUpdate(ulong simulationStep) => new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, simulationStep, simulationStep, 60, 1, 0, 1d / 60d);

    /// <summary>One advanced sprite frame whose authored damage frame was crossed.</summary>
    /// <summary>A playback receipt that completes without crossing a marker, which is what clears a swing.</summary>
    internal static SpritePlaybackAdvanceResult CompletedMarker(uint frame) => new(
        Array.Empty<SpritePlaybackMarkerCrossing>(),
        new SpritePlaybackReadout(frame, 1, SpritePlaybackState.Completed, 0D, 0, frame, true),
        true);

    /// <summary>
    /// The damage marker the placed actor's own authored melee sequence publishes. A fixture that
    /// fabricates the crossing the appearance would emit names the marker the author designated for the
    /// actor actually swinging, derived here from the same content the session loads.
    /// </summary>
    internal static ulong AuthoredMeleeMarker(long placedEntityId) => AuthoredMarker(placedEntityId, "primaryFrames");

    internal static ulong AuthoredMarker(long placedEntityId, string framesProperty)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        JsonObject profile = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")))!.AsObject();
        JsonObject placement = profile["placements"]!.AsArray().Select(value => value!.AsObject())
            .Single(value => value["entityId"]!.GetValue<long>() == placedEntityId);
        DaggerfallActorDefinition actor = definitions.RequireActor(new DaggerfallActorId(placement["actor"]!.GetValue<string>()));
        JsonObject media = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/imports/privateers-hold/media/dungeon/manifest.json")))!.AsObject();
        JsonObject mobile = media["actors"]!.AsArray().Select(value => value!.AsObject())
            .Single(value => value["mobileId"]!.GetValue<int>() == actor.MobileId);
        List<int> frames = [.. mobile["sourceAttackSequence"]![framesProperty]!.AsArray().Select(value => value!.GetValue<int>())];
        int index = frames.IndexOf(-1);
        Assert.True(index >= 0, $"placed actor {placedEntityId} has no authored damage frame in {framesProperty}");
        return checked((ulong)index + 1);
    }

    /// <summary>A crossing of the named authored marker, identified the way the product identifies it.</summary>
    internal static SpritePlaybackAdvanceResult CrossedMarker(uint frame, ulong markerId = 2, ulong crossing = 1) => new(
        new[] { new SpritePlaybackMarkerCrossing(markerId, 3, 1, 0, crossing) },
        new SpritePlaybackReadout(frame, 1, SpritePlaybackState.Playing, 0D, 0, frame, false),
        true);

    internal static ProductInputEvent Input(InputEventKind kind, InputEdge edge = InputEdge.None, KeyboardControl keyboard = KeyboardControl.None, float x = 0F, float y = 0F, InputPhase phase = InputPhase.None, string intent = "") => new(kind, edge, InputDevice.None, InputChannel.None, InputAxis.None, keyboard, PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None, InputValueKind.None, phase, InputProvenance.None, default, default, default, x, y, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, Encoding.UTF8.GetBytes(intent), ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

    /// <summary>One physical controller button edge, as the shell publishes it.</summary>
    internal static ProductInputEvent PadButton(ControllerButton button, InputEdge edge) =>
        Input(InputEventKind.ControllerButton, edge, x: edge == InputEdge.Pressed ? 1F : 0F) with
        {
            Device = InputDevice.Controller,
            Channel = InputChannel.Button,
            ValueKind = InputValueKind.Digital,
            ControllerButton = button,
            Phase = edge == InputEdge.Pressed ? InputPhase.Pressed : InputPhase.Released,
            Provenance = InputProvenance.Physical,
        };

    /// <summary>The default tuning payload with one test mutation applied.</summary>
    internal static byte[] MutatedTuning(string repositoryRoot, Action<JsonObject> mutate)
    {
        JsonObject tuning = JsonNode.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "content/worldrpg/tuning-payloads/daggerfall.defaults.json")))!.AsObject();
        mutate(tuning);
        return Encoding.UTF8.GetBytes(tuning.ToJsonString());
    }

    internal static DaggerfallSiteProfile SameContentAt(DaggerfallSiteProfile source, DaggerfallSiteId site, DaggerfallWorldProfileKind kind, string logicalId) => new(
        kind == DaggerfallWorldProfileKind.Exterior
            ? new ProjectFacts(new WorldPoint(1f, 1f, 1f), source.Project.Actors)
            : source.Project,
        source.SpatialArtifact,
        source.StaticMesh,
        source.WorldAppearance,
        source.InitialLook,
        source.Materials,
        source.ActorSprites,
        source.MobileSprites,
        source.Audio,
        source.ClassicPresentation,
        site,
        kind == DaggerfallWorldProfileKind.Dungeon ? source.Doors : [],
        kind,
        logicalId,
        source.Portals,
        source.Anchors.Values.ToArray(),
        source.Lights,
        source.GroundContainerSprite,
        kind == DaggerfallWorldProfileKind.Dungeon ? source.DungeonMap : null,
        kind == DaggerfallWorldProfileKind.Dungeon ? source.DungeonActions : [],
        kind == DaggerfallWorldProfileKind.Dungeon ? source.DungeonActionModels : []);

    internal static DaggerfallSavePayload RoundTrip(DaggerfallSavePayload value) => DaggerfallSavePayload.Read(DaggerfallSavePayload.Encode(value));
}

/// <summary>Small reusable real-session fixture for focused ruleset tests that need a save/reload boundary.</summary>
internal sealed class ConditionSessionFixture : IDisposable
{
    private readonly DaggerfallDefinitions definitions;
    private readonly DaggerfallSiteProfile inputs;
    private readonly ResolvedCompositionIdentity identity;
    private readonly List<string> releases = [];
    internal DaggerfallSession Session { get; }
    internal DaggerfallDefinitions Definitions => definitions;

    internal ConditionSessionFixture(IRandomService? random = null)
    {
        string root = TestData.RepositoryRoot;
        definitions = TestPayload.Definitions;
        inputs = ReadInputs(root);
        identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: random);
        Session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
    }

    internal DaggerfallSession Restore(RulesetSavePayload saved)
    {
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        return DaggerfallSession.Restore(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), saved);
    }

    public void Dispose() => Session.Dispose();
}
