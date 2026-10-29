using System.Runtime.CompilerServices;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

public sealed class DaggerfallRuleset : ISaveableGameRuleset, IRulesVariantCatalog, IInstalledContentRuleset
{
    private readonly IReadOnlyList<DaggerfallRulesVariant> _variants;
    private readonly ConditionalWeakTable<ResolvedGameComposition, DaggerfallAdmittedContent> _admittedContent = [];
    private readonly bool _videosEnabled;
    public static readonly RulesetId Identity = new("daggerfall");

    /// <summary>The one authored pack carrying the ruleset identity, vocabulary, actors, items, loot and encounters.</summary>
    internal static readonly ContentPackRoleId BaseRole = new("daggerfall.base");
    /// <summary>
    /// The one pack the import tool generates from the operator's Arena2 files: the shared catalogs, world
    /// records, text and quest sources. The base reader joins it to the authored pack, section by section.
    /// </summary>
    internal static readonly ContentPackRoleId ImportedRole = new("daggerfall.imported");
    /// <summary>The one pack carrying the classic block records sites and interiors are built from.</summary>
    internal static readonly ContentPackRoleId BlocksRole = new("daggerfall.blocks");
    /// <summary>A normalized world profile: an exterior, interior or dungeon closure at one site.</summary>
    internal static readonly ContentPackRoleId SiteRole = new("daggerfall.site");
    /// <summary>A categorized classic quest corpus; its categories state which entries are offered.</summary>
    internal static readonly ContentPackRoleId QuestCorpusRole = new("daggerfall.quest-corpus");
    internal static readonly ContentPackRoleId NamedQuestCorpusRole = new("daggerfall.named-quest-corpus");
    /// <summary>The Fighters Guild receipt: the whole active guild group in catalog order, without categories.</summary>
    internal static readonly ContentPackRoleId FightersGuildQuestCorpusRole = new("daggerfall.fighters-guild-quest-corpus");
    /// <summary>
    /// An optional pack that starts a new game elsewhere than the base pack's new-game definition: its own
    /// start site, starting gold and opening quests. A bundle selects at most one.
    /// </summary>
    internal static readonly ContentPackRoleId NewGameStartRole = new("daggerfall.new-game-start");

    /// <summary>Creates the ordinary product ruleset, compiling no rules variants, with its admitted cinematic playback enabled.</summary>
    public DaggerfallRuleset() : this([]) { }

    /// <summary>Creates the ordinary product ruleset with the compiled rules variants a bundle may select.</summary>
    public DaggerfallRuleset(IReadOnlyList<DaggerfallRulesVariant> variants) : this(variants, videosEnabled: true) { }

    /// <summary>Test-only explicit no-video composition; absent admitted content is never interpreted as this setting.</summary>
    internal DaggerfallRuleset(bool videosEnabled) : this([], videosEnabled) { }

    internal DaggerfallRuleset(IReadOnlyList<DaggerfallRulesVariant> variants, bool videosEnabled)
    {
        ArgumentNullException.ThrowIfNull(variants);
        _variants = [.. variants];
        _videosEnabled = videosEnabled;
    }

    public RulesetId Id => Identity;

    public IReadOnlyList<RulesVariantManifest> CompiledVariants => [.. _variants.Select(variant => variant.Manifest)];

    /// <summary>The import tool <c>rusty build --release</c> ships, from the game's folder.</summary>
    private const string ReleaseImportTool = "tools/Daggerfall.Import.Tool/Daggerfall.Import.Tool";

    /// <summary>
    /// The entry screen's import of the Daggerfall data: the declared import tool, run on the folder the player
    /// chooses and told to install the container where the host opens it.
    /// </summary>
    internal static readonly InstalledContentImport DataImport = new("Daggerfall.Import.Tool",
        "Choose the folder Daggerfall is installed in, or its ARENA2 folder",
        (folder, install) => ["import", "--arena2", folder, "--install", install]);

    /// <summary>The import command a developer runs in the repository root to produce the containers.</summary>
    private const string ImportCommand = "dotnet run --project src/Daggerfall.Import.Tool -- import --music local/Sound";

    /// <summary>
    /// The containers the player's own Daggerfall files are converted into: the data every game reads, and the
    /// optional score, whose tracks are read by name; and the optional 3D first-person art, built on a development
    /// checkout.
    /// </summary>
    public IReadOnlyList<InstalledContentRequirement> InstalledContent { get; } =
    [
        new(DaggerfallData.DataContainerName, "the Daggerfall data", Required: true,
            DevelopmentStep: $"Choose Import, or with the game's ARENA2 folder at local/arena2 run `{ImportCommand}` in the repository root.",
            ReleaseStep: "Choose Import and select the folder Daggerfall is installed in, or its ARENA2 folder.",
            Import: DataImport),
        new(DaggerfallMusicBundle.ContainerName, "the Daggerfall music", Required: false,
            DevelopmentStep: $"Run `{ImportCommand}` with a folder of <track>.<ext> recordings, such as the Daggerfall Unity song pack.",
            ReleaseStep: $"Run {ReleaseImportTool} with the folder Daggerfall is installed in and --music with a folder of the game's music recordings (<track>.<ext>)."),
        new(DaggerfallHeldArt.ContainerName, "the 3D first-person arms", Required: false,
            DevelopmentStep: "Run `scripts/fp-arms/build.sh` in the repository root (scripts/fp-arms/README.md).",
            ReleaseStep: "Build first-person.rpak on a development checkout with scripts/fp-arms/build.sh and place it beside the product."),
    ];

    /// <summary>
    /// Whether the data container was imported by the importer this ruleset was built with: content another
    /// importer revision wrote may lack, or shape differently, what this build reads.
    /// </summary>
    public string? CheckInstalledContent(InstalledContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Name != DaggerfallData.DataContainerName) return null;
        InstalledContentDescription? description;
        try { description = content.Description; }
        catch (InvalidDataException exception) { return exception.Message; }
        if (description is null) return $"{content.Name} carries no description of the import that produced it.";
        return description.Revision == DaggerfallImporterRevision.Value
            ? null
            : $"{content.Name} was imported by importer revision {Short(description.Revision)}, and this build reads revision {Short(DaggerfallImporterRevision.Value)}.";

        static string Short(string revision) => revision.Length > 12 ? revision[..12] : revision;
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
            Audio = admitted.Audio,
            Sky = DaggerfallSkyMedia.Read(admitted.Data),
            Data = admitted.Data,
            VideosEnabled = _videosEnabled,
            QuestAdmission = new DaggerfallQuestRuntimeAdmission(admitted.QuestReceipts),
            NewGameQuests = admitted.Definitions.NewGame.Quests,
            DisabledQuestSelection = admitted.DisabledQuestSelection,
            Music = admitted.Music,
            HeldArt = admitted.HeldArt,
            Blocks = admitted.Blocks,
            Seams = DaggerfallSeamSelection.From(context.Composition.Variants, _variants),
            Loading = context.Loading,
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
                if (pack.Role != BaseRole && pack.Role != ImportedRole && pack.Role != BlocksRole && pack.Role != SiteRole
                    && pack.Role != QuestCorpusRole && pack.Role != FightersGuildQuestCorpusRole && pack.Role != NamedQuestCorpusRole
                    && pack.Role != NewGameStartRole)
                    throw new InvalidOperationException($"Content pack '{pack.Id.Value}' declares role '{pack.Role.Value}', which the Daggerfall ruleset does not interpret.");
            }
            DaggerfallData data = DaggerfallData.For(selected);
            DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(
                RequireSingle(selected, roles, BaseRole).Payload, RequireSingle(selected, roles, ImportedRole).Payload, data);
            ContentPack[] startPacks = [.. roles[NewGameStartRole]];
            if (startPacks.Length > 1)
                throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' selects {startPacks.Length} '{NewGameStartRole.Value}' content packs; a new game starts in one place.");
            if (startPacks.Length == 1)
                definitions = definitions.WithNewGame(DaggerfallBaseContent.ReadNewGameStart(startPacks[0].Payload, definitions.NewGame, startPacks[0].Id.Value));
            DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(RequireSingle(selected, roles, BlocksRole).Payload);
            blocks.AdmitLocations(definitions.Locations);
            ContentPack[] sitePacks = [.. roles[SiteRole]];
            if (sitePacks.Length == 0)
                throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' selects no '{SiteRole.Value}' content pack, so a new game has nowhere to start.");
            // Each site pack states the profile id it overrides; its closure is read only when that profile is
            // first needed. Every other location profile is assembled from the per-block publication on demand.
            DaggerfallSitePayloadHeader[] headers = [.. sitePacks.Select(pack => DaggerfallSiteContent.ReadHeader(pack.Payload, pack.Id.Value))];
            // Every site closure and assembly references the one product-wide world media publication, read
            // once for all of them, when the first one needs it.
            Lazy<DaggerfallWorldMedia> worldMedia = new(() => DaggerfallWorldMedia.Read(data));
            Lazy<DaggerfallProductMedia> productMedia = new(() => DaggerfallSiteContent.ReadProductMedia(data, worldMedia.Value, definitions));
            Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(data, worldMedia.Value));
            DaggerfallLocationAssembly assembly = new(definitions, new DaggerfallWorldBlocks(data), () => productMedia.Value, () => meshes.Value);
            // Every resolved profile names its classic images against the published manifests, and an interior
            // names its building against the block catalog: each is joined when the profile is resolved, before
            // a transition touches the source, so the publication it disagrees with is named.
            HashSet<NormalizedClassicPresentation> joinedPresentations = new(ReferenceEqualityComparer.Instance);
            void Join(DaggerfallSiteProfile site)
            {
                if (joinedPresentations.Add(site.ClassicPresentation)) _ = DaggerfallPublishedClassicMedia.Read(data, site.ClassicPresentation);
                site.InteriorBuilding?.ValidateAgainst(blocks);
            }

            DaggerfallSiteProfiles profiles = DaggerfallSiteProfiles.Resolve(
                sitePacks.Zip(headers, (pack, header) => new DaggerfallAuthoredSite(header,
                    () => DaggerfallSiteContent.Read(data, pack.Payload, definitions, worldMedia.Value))),
                assembly, Join);
            // The authored new-game definition names the site a new game starts at; pack order in the
            // bundle carries no meaning, so adding or reordering sites cannot move the start.
            string startPack = definitions.NewGame.StartSitePack;
            DaggerfallSitePayloadHeader startHeader = headers.FirstOrDefault(header => header.PackId == startPack)
                ?? throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' does not select the new-game start site '{startPack}'.");
            if (startHeader.VariantName is not null)
                throw new InvalidOperationException($"New-game start site '{startPack}' is a world variant, not a base site.");
            DaggerfallSiteProfile inputs = profiles.Require(startHeader.Key);
            // The start site's classic selection is the session's held-item and spell presentation at
            // every site, so a start site without one would leave the player's hands unpresented.
            if (inputs.ClassicPresentation.Viewmodel is null || inputs.ClassicPresentation.UnarmedVisual is null)
                throw new InvalidOperationException($"New-game start site '{startPack}' publishes no classic viewmodel selection for the session.");
            IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> fightersGuildQuests =
                [.. roles[FightersGuildQuestCorpusRole].SelectMany(pack => DaggerfallFightersGuildQuestCorpusContent.Read(pack.Payload, definitions))];
            DaggerfallClassicQuestCorpusReceipt[] classicCorpus =
                [.. roles[QuestCorpusRole].SelectMany(pack => DaggerfallClassicQuestCorpusContent.Read(pack.Payload, definitions))];
            DaggerfallDisabledQuestSelection disabledQuestSelection = DaggerfallDisabledQuestSelection.From(classicCorpus);
            IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> classicQuestReceipts =
            [
                .. classicCorpus.Where(receipt => receipt.IsOrdinaryOffer).Select(receipt => receipt.Runtime),
                .. disabledQuestSelection.Receipts,
                .. roles[NamedQuestCorpusRole].SelectMany(pack => DaggerfallNamedQuestCorpusContent.Read(pack.Payload, definitions)),
            ];
            // The authored new-game definition names the quests a new game starts, so a bundle that does
            // not admit one is refused by name rather than starting a game silently without it.
            IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> questReceipts = [.. fightersGuildQuests, .. classicQuestReceipts];
            string[] missingNewGameQuests = [.. definitions.NewGame.Quests.Where(file => !questReceipts.Any(receipt => receipt.SourceFile == file))];
            if (missingNewGameQuests.Length != 0)
                throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' admits no quest source for the new-game quests {string.Join(", ", missingNewGameQuests.Select(file => $"'{file}'"))}.");
            DaggerfallPublishedClassicMedia classicMedia = DaggerfallPublishedClassicMedia.Read(data, inputs.ClassicPresentation);
            DaggerfallMusicBundle? music = DaggerfallMusicBundle.For(selected);
            DaggerfallHeldArt? heldArt = DaggerfallHeldArt.For(selected);
            DaggerfallTuning tuning = DaggerfallTuning.Read(selected.Tuning.Payload.Span);
            return new DaggerfallAdmittedContent(definitions, blocks, inputs, profiles, questReceipts, disabledQuestSelection, tuning, classicMedia, new DaggerfallSiteAudioBundles(data, profiles), data, music, heldArt);
        });

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
        DaggerfallSiteProfiles Profiles,
        IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> QuestReceipts,
        DaggerfallDisabledQuestSelection DisabledQuestSelection,
        DaggerfallTuning Tuning,
        DaggerfallPublishedClassicMedia ClassicMedia,
        DaggerfallSiteAudioBundles Audio,
        DaggerfallData Data,
        DaggerfallMusicBundle? Music,
        DaggerfallHeldArt? HeldArt);
}
