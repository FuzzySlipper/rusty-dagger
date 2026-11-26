using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using System.Runtime.CompilerServices;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

public sealed class DaggerfallRuleset : ISaveableGameRuleset, IInstalledContentRuleset
{
    private readonly ConditionalWeakTable<ResolvedGameComposition, DaggerfallAdmittedContent> _admittedContent = [];
    private readonly bool _videosEnabled;
    private readonly ulong? _gameSeed;
    public static readonly RulesetId Identity = new(DaggerfallBaseContent.RulesetName);

    /// <summary>The one authored pack carrying the ruleset identity, vocabulary, actors, items, loot and encounters.</summary>
    internal static readonly ContentPackRoleId BaseRole = new("daggerfall.base");
    /// <summary>
    /// The one pack the import tool generates from the operator's Arena2 files: the shared catalogs, world
    /// records, text and quest sources. The base reader joins it to the authored pack, section by section.
    /// </summary>
    internal static readonly ContentPackRoleId ImportedRole = new("daggerfall.imported");
    /// <summary>The one pack carrying the classic block records sites and interiors are built from.</summary>
    internal static readonly ContentPackRoleId BlocksRole = new("daggerfall.blocks");
    /// <summary>
    /// A quests pack: quest records in its <c>quests/</c> folder, and in its payload any of the quest catalog, tables and
    /// corpora (<see cref="DaggerfallQuestContent"/>). A bundle may select several, such as the classic quests and a
    /// scenario's own.
    /// </summary>
    internal static readonly ContentPackRoleId QuestsRole = new("daggerfall.quests");
    /// <summary>The owned people pack (<see cref="DaggerfallPeopleContent"/>): unique cast, regional spawners, and block slots.</summary>
    internal static readonly ContentPackRoleId PeopleRole = new("daggerfall.people");
    /// <summary>
    /// An optional pack that starts a new game elsewhere than the base pack's new-game definition: its own
    /// start, starting gold and opening quests. A bundle selects at most one.
    /// </summary>
    internal static readonly ContentPackRoleId NewGameStartRole = new("daggerfall.new-game-start");
    /// <summary>The refreshed art set (<see cref="DaggerfallArtReplacements"/>); a bundle selects at most one.</summary>
    internal static readonly ContentPackRoleId ArtReplacementsRole = new("daggerfall.art-replacements");

    /// <summary>Creates the ordinary product ruleset, with its admitted cinematic playback enabled.</summary>
    public DaggerfallRuleset() : this(videosEnabled: true) { }

    /// <summary>Test-only explicit no-video or fixed-seed composition; absent admitted content is never interpreted as this setting.</summary>
    internal DaggerfallRuleset(bool videosEnabled, ulong? gameSeed = null)
    {
        _videosEnabled = videosEnabled;
        _gameSeed = gameSeed;
    }

    public RulesetId Id => Identity;

    /// <summary>
    /// The entry screen's import of the Daggerfall data: the declared import tool, run on the folder the player
    /// chooses and told to install the container where the host opens it.
    /// </summary>
    internal static readonly InstalledContentImport DataImport = new("Daggerfall.Import.Tool",
        "Choose the folder Daggerfall is installed in, or its ARENA2 folder",
        (folder, install) => ["import", "--arena2", folder, "--install", install]);

    /// <summary>The import command a developer runs in the repository root to produce the containers.</summary>
    private const string ImportCommand = "dotnet run --project src/Daggerfall.Import.Tool -- import --classic-art";

    /// <summary>
    /// The containers the player's own Daggerfall files are converted into: the data every game reads and the developer's
    /// optional classic art the data leaves out; and three that ship with the game, built on a development checkout: the
    /// score, Patch's arrangement, whose tracks are read by name, the optional 3D first-person art, and the optional
    /// animation clips, which never live in Git.
    /// </summary>
    public IReadOnlyList<InstalledContentRequirement> InstalledContent { get; } =
    [
        new(DaggerfallData.DataContainerName, "the Daggerfall data", Required: true,
            DevelopmentStep: $"Choose Import, or with the game's ARENA2 folder at local/inputs/arena2 run `{ImportCommand}` in the repository root.",
            ReleaseStep: "Choose Import and select the folder Daggerfall is installed in, or its ARENA2 folder.",
            Import: DataImport),
        new(DaggerfallMusicBundle.ContainerName, "the music", Required: false,
            DevelopmentStep: "Run `scripts/data-sync.sh pull` (or `link`), or build it with `authoring/tools/music/build.sh` (authoring/tools/music).",
            ReleaseStep: "Build music.rpak on a development checkout with authoring/tools/music/build.sh and place it beside the product."),
        new(DaggerfallData.ClassicArtContainerName, "the classic art the refreshed art draws anew", Required: false,
            DevelopmentStep: $"Run `{ImportCommand}` in the repository root; it is the developer's comparison (world.classic-art), never a player's.",
            ReleaseStep: "A player's game draws the refreshed art and has no classic-art container."),
        new(DaggerfallHeldArt.ContainerName, "the 3D first-person arms", Required: false,
            DevelopmentStep: "Run `authoring/tools/fp-arms/build.sh` in the repository root (authoring/tools/fp-arms/README.md).",
            ReleaseStep: "Build first-person.rpak on a development checkout with authoring/tools/fp-arms/build.sh and place it beside the product."),
        new(DaggerfallAnimations.ContainerName, "the townspeople's animation clips", Required: false,
            DevelopmentStep: "Run `scripts/data-sync.sh pull` (or `link`), or build it with `authoring/tools/people/splat-people.py`.",
            ReleaseStep: "Pull animations.rpak on a development checkout with scripts/data-sync.sh and place it beside the product."),
    ];

    /// <summary>
    /// Whether the data container, or the developer's classic-art container, is one whole import by the importer this
    /// ruleset was built with (<see cref="DaggerfallData.Stale"/>).
    /// </summary>
    public string? CheckInstalledContent(InstalledContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Name is DaggerfallData.DataContainerName or DaggerfallData.ClassicArtContainerName ? DaggerfallData.Stale(content) : null;
    }

    /// <summary>The entry screen that states why no Daggerfall game can be played, with nothing to play.</summary>
    public IGameSession CreateUnavailableSession(GameUnavailableContext context) => new DaggerfallUnavailableSession(context);

    public IGameSession CreateSession(GameSessionContext context)
        => CreateSessionCore(context);

    public IGameSession CreateSession(GameSessionContext context, RulesetSavePayload saved)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(saved);
        if (context.Composition.Ruleset != Identity)
            throw new InvalidOperationException($"Daggerfall cannot interpret ruleset '{context.Composition.Ruleset.Value}'.");
        return DaggerfallSession.Restore(context.Engine, Compose(context, Admit(context.Composition)), saved);
    }

    /// <summary>A fresh session: no saved state exists, so nothing is resolved or reported.</summary>
    private IGameSession CreateSessionCore(GameSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Composition.Ruleset != Identity)
            throw new InvalidOperationException($"Daggerfall cannot interpret ruleset '{context.Composition.Ruleset.Value}'.");
        return DaggerfallSession.StartNew(context.Engine, Compose(context, Admit(context.Composition)));
    }

    /// <summary>The one session composition both start kinds are built from.</summary>
    private DaggerfallSessionComposition Compose(GameSessionContext context, DaggerfallAdmittedContent admitted) =>
        new(admitted.Definitions, admitted.Inputs, admitted.Tuning, context.CompositionIdentity)
        {
            Profiles = admitted.Profiles,
            StartPose = admitted.StartPose,
            Audio = admitted.Audio,
            Sky = DaggerfallSkyMedia.Read(admitted.Data),
            Data = admitted.Data,
            VideosEnabled = _videosEnabled,
            QuestAdmission = new DaggerfallQuestRuntimeAdmission(admitted.QuestReceipts),
            NewGameQuests = admitted.Definitions.NewGame.Quests,
            DisabledQuestSelection = admitted.DisabledQuestSelection,
            Music = admitted.Music,
            HeldArt = admitted.HeldArt,
            Animations = admitted.Animations,
            ArtReplacements = admitted.ArtReplacements,
            Blocks = admitted.Blocks,
            Loading = context.Loading,
            GameSeed = _gameSeed ?? DaggerfallGameRandom.NewSeed(),
        };

    private DaggerfallAdmittedContent Admit(ResolvedGameComposition composition) =>
        _admittedContent.GetValue(composition, static selected =>
        {
            if (selected.Ruleset != Identity)
                throw new InvalidOperationException($"Daggerfall cannot interpret ruleset '{selected.Ruleset.Value}'.");
            // Every selected pack is read by the reader its declared role names. A role this ruleset does
            // not interpret is refused: admitting the pack without reading it would drop its content
            // while the bundle still claimed to carry it.
            ILookup<ContentPackRoleId, ContentPack> roles = selected.ContentPacks.ToLookup(pack => pack.Role);
            foreach (ContentPack pack in selected.ContentPacks)
            {
                if (pack.Role != BaseRole && pack.Role != ImportedRole && pack.Role != BlocksRole && pack.Role != QuestsRole
                    && pack.Role != PeopleRole && pack.Role != NewGameStartRole && pack.Role != ArtReplacementsRole)
                    throw new InvalidOperationException($"Content pack '{pack.Id.Value}' declares role '{pack.Role.Value}', which the Daggerfall ruleset does not interpret.");
            }
            DaggerfallData data = DaggerfallData.For(selected);
            ContentPack basePack = RequireSingle(selected, roles, BaseRole);
            DaggerfallBasePackFiles baseFiles = DaggerfallBaseContent.Files(selected.Content, basePack.Id.Value, basePack.PayloadPath, basePack.Payload);
            // The locations the base pack lays out anew replace the imported ones before anything reads the catalog.
            DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(baseFiles, RequireSingle(selected, roles, ImportedRole).Payload, data)
                .WithLocationOverrides(DaggerfallLocationOverrides.Read(selected.Content, basePack.PayloadPath, DaggerfallBaseContent.AssembleAuthored(baseFiles)))
                .WithQuestSources(DaggerfallQuestContent.Read([.. roles[QuestsRole].Select(pack => QuestPackFiles(selected, pack))]));
            ContentPack[] peoplePacks = [.. roles[PeopleRole]];
            if (peoplePacks.Length > 1)
                throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' selects {peoplePacks.Length} '{PeopleRole.Value}' content packs; a session reads at most one.");
            if (peoplePacks.Length == 1)
                definitions = definitions.WithPeople(DaggerfallPeopleContent.Read(
                    PeoplePackFiles(selected, peoplePacks[0]), definitions.Factions, definitions.Vocabulary, definitions.Items, definitions.ActorCatalog));
            ContentPack[] startPacks = [.. roles[NewGameStartRole]];
            if (startPacks.Length > 1)
                throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' selects {startPacks.Length} '{NewGameStartRole.Value}' content packs; a new game starts in one place.");
            if (startPacks.Length == 1)
                definitions = definitions.WithNewGame(DaggerfallBaseContent.ReadNewGameStart(startPacks[0].Payload, definitions.NewGame, startPacks[0].Id.Value));
            if (definitions.NewGame.Foes is { } foes)
            {
                // A start's foes are brought one at a time, so each is checked as world.spawn checks one.
                if (foes.Actors.Length > DaggerfallNearbySpawn.MaximumCount || !(foes.Metres is >= DaggerfallNearbySpawn.MinimumMetres and <= DaggerfallNearbySpawn.MaximumMetres))
                    throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' starts with {foes.Actors.Length} foes within {foes.Metres} m; "
                        + $"a start brings up to {DaggerfallNearbySpawn.MaximumCount} within {DaggerfallNearbySpawn.MinimumMetres:0} to {DaggerfallNearbySpawn.MaximumMetres:0} m.");
                foreach (string foe in foes.Actors)
                    if (!definitions.Actors.TryGetValue(new DaggerfallActorId(foe), out DaggerfallActorDefinition? actor)
                        || actor.Kind is not (DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass))
                        throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' starts with foe '{foe}', which is no published monster or enemy class.");
            }
            DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(RequireSingle(selected, roles, BlocksRole).Payload);
            blocks.AdmitLocations(definitions.Locations);
            // Every location profile is assembled from the per-block publication when first needed, over the one
            // product-wide world media publication, read once for all of them when the first one needs it.
            Lazy<DaggerfallWorldMedia> worldMedia = new(() => DaggerfallWorldMedia.Read(data));
            Lazy<DaggerfallProductMedia> productMedia = new(() => DaggerfallSiteContent.ReadProductMedia(data, worldMedia.Value, definitions));
            Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(data, worldMedia.Value));
            // The refreshed art set's remodels draw in place of the classic models an assembled location places.
            ContentPack[] artPacks = [.. roles[ArtReplacementsRole]];
            if (artPacks.Length > 1)
                throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' selects {artPacks.Length} '{ArtReplacementsRole.Value}' content packs; a session draws one refreshed art set.");
            DaggerfallArtReplacements? art = artPacks.Length == 1 ? DaggerfallArtReplacements.Read(artPacks[0].Payload, artPacks[0].Id.Value) : null;
            // A classic image the player's data left out because the pack drew it anew must still be drawn anew.
            DaggerfallLeftOutMedia.Check(data, art);
            // The refreshed doll draws a masking item with its own mask, never the classic one.
            art?.RequireOwnMasks(definitions.CharacterPresentation);
            // A quest's world update selects a location variant the quests packs carry, baked as block documents beside them.
            DaggerfallWorldVariantContent variants = DaggerfallWorldVariantContent.Read([.. roles[QuestsRole].Select(pack => DaggerfallWorldVariantContent.Files(selected.Content, pack.Id.Value, pack.PayloadPath, pack.Payload))],
                definitions.Locations.Records);
            DaggerfallTuning tuning = DaggerfallTuning.Read(selected.Tuning.Payload.Span);
            DaggerfallLocationAssembly assembly = new(definitions, new DaggerfallWorldBlocks(data, variants), () => productMedia.Value, () => meshes.Value, art?.Models, tuning.Social.WorldSocialSeed);
            // Every resolved profile names its classic images against the published manifests, and an interior
            // names its building against the block catalog: each is joined when the profile is resolved, before
            // a transition touches the source, so the publication it disagrees with is named.
            HashSet<NormalizedClassicPresentation> joinedPresentations = new(ReferenceEqualityComparer.Instance);
            void Join(DaggerfallSiteProfile site)
            {
                if (joinedPresentations.Add(site.ClassicPresentation)) _ = DaggerfallPublishedClassicMedia.Read(data, site.ClassicPresentation);
                site.InteriorBuilding?.ValidateAgainst(blocks);
            }

            DaggerfallSiteProfiles profiles = DaggerfallSiteProfiles.Resolve(assembly, Join);
            // The new-game definition names the profile a new game starts in, and may name where in it the player stands.
            DaggerfallNewGameStart start = definitions.NewGame.Start;
            if (!profiles.TryGetLogicalProfile(start.Profile, out DaggerfallSiteProfile inputs))
                throw new InvalidOperationException($"The new-game start names profile '{start.Profile}', which no location places.");
            // The classic selection is the session's held-item and spell presentation at every site, so a session
            // without one would leave the player's hands unpresented.
            if (inputs.ClassicPresentation.Viewmodel is null || inputs.ClassicPresentation.UnarmedVisual is null)
                throw new InvalidOperationException("The base payload publishes no classic viewmodel selection for the session.");
            DaggerfallQuestAdmission quests = DaggerfallQuestContent.Admit(definitions.QuestSources, new DaggerfallQuestLanguage());
            // The authored new-game definition names the quests a new game starts, so a bundle that does
            // not carry one is refused by name rather than starting a game silently without it.
            string[] missingNewGameQuests = [.. definitions.NewGame.Quests.Where(file => !definitions.QuestSources.Quests.ContainsKey(file))];
            if (missingNewGameQuests.Length != 0)
                throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' carries no quest record for the new-game quests {string.Join(", ", missingNewGameQuests.Select(file => $"'{file}'"))}.");
            DaggerfallPublishedClassicMedia classicMedia = DaggerfallPublishedClassicMedia.Read(data, inputs.ClassicPresentation);
            DaggerfallMusicBundle? music = DaggerfallMusicBundle.For(selected);
            DaggerfallHeldArt? heldArt = DaggerfallHeldArt.For(selected, DaggerfallAttackTuning.Default.Classes.Keys);
            return new DaggerfallAdmittedContent(definitions, blocks, inputs, start.Pose, profiles, quests.Receipts, quests.DisabledSelection, tuning, classicMedia, new DaggerfallSiteAudioBundles(data, profiles), data, music, heldArt,
                DaggerfallAnimations.For(selected), art);
        });

    /// <summary>A quests pack's payload and the records in the <c>quests/</c> folder beside it.</summary>
    private static DaggerfallQuestPackFiles QuestPackFiles(ResolvedGameComposition selected, ContentPack pack)
    {
        string folder = $"{pack.PayloadPath[..(pack.PayloadPath.LastIndexOf('/') + 1)]}{DaggerfallQuestContent.RecordsFolder}";
        return new(pack.Id.Value, pack.Payload, [.. selected.Content.ReadDirectory(folder, false).ToArray()
            .Where(file => file.RelativePath.EndsWith(".json", StringComparison.Ordinal))
            .Select(file => new DaggerfallQuestRecordFile(file.RelativePath, file.Bytes))]);
    }

    /// <summary>The owned people pack's payload, ambient barks, and block slot files beside it.</summary>
    private static DaggerfallPeoplePackFiles PeoplePackFiles(ResolvedGameComposition selected, ContentPack pack)
    {
        string root = pack.PayloadPath[..pack.PayloadPath.LastIndexOf('/')];
        ProductContentFile[] files = selected.Content.ReadDirectory(root, true).ToArray();
        ProductContentFile[] barkFiles = [.. files.Where(file => file.RelativePath.EndsWith("/" + DaggerfallPeopleContent.BarksFile, StringComparison.Ordinal))];
        if (barkFiles.Length != 1)
            throw new InvalidOperationException($"People pack '{pack.Id.Value}' must carry exactly one '{DaggerfallPeopleContent.BarksFile}'.");
        DaggerfallPeopleBlockFile[] blocks = [.. files
            .Where(file => file.RelativePath.Contains("/" + DaggerfallPeopleContent.BlocksFolder + "/", StringComparison.Ordinal)
                && file.RelativePath.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .Select(file => new DaggerfallPeopleBlockFile(file.RelativePath, file.Bytes))];
        return new(pack.Id.Value, pack.Payload, CastBytes: null, SpawnersBytes: null, blocks, ArchetypesBytes: null, BarksBytes: barkFiles[0].Bytes);
    }

    /// <summary>The one pack a bundle must select for a role every session reads exactly once.</summary>
    private static ContentPack RequireSingle(ResolvedGameComposition selected, ILookup<ContentPackRoleId, ContentPack> roles, ContentPackRoleId role)
    {
        ContentPack[] packs = [.. roles[role]];
        return packs.Length == 1
            ? packs[0]
            : throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' selects {packs.Length} '{role.Value}' content packs; a session reads exactly one.");
    }

    private sealed record DaggerfallAdmittedContent(
        DaggerfallDefinitions Definitions,
        DaggerfallBlocksSnapshot Blocks,
        DaggerfallSiteProfile Inputs,
        DaggerfallSiteAnchor? StartPose,
        DaggerfallSiteProfiles Profiles,
        IReadOnlyList<DaggerfallQuestRuntimeReceipt> QuestReceipts,
        DaggerfallDisabledQuestSelection DisabledQuestSelection,
        DaggerfallTuning Tuning,
        DaggerfallPublishedClassicMedia ClassicMedia,
        DaggerfallSiteAudioBundles Audio,
        DaggerfallData Data,
        DaggerfallMusicBundle? Music,
        DaggerfallHeldArt? HeldArt,
        DaggerfallAnimations? Animations,
        DaggerfallArtReplacements? ArtReplacements);
}
