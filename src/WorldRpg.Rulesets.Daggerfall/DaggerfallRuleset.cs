using System.Runtime.CompilerServices;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

public sealed class DaggerfallRuleset : ISaveableGameRuleset
{
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
    /// <summary>The Fighters Guild receipt: the whole active guild group in catalog order, without categories.</summary>
    internal static readonly ContentPackRoleId FightersGuildQuestCorpusRole = new("daggerfall.fighters-guild-quest-corpus");

    /// <summary>Creates the ordinary product ruleset with its admitted cinematic playback enabled.</summary>
    public DaggerfallRuleset() : this(videosEnabled: true) { }

    /// <summary>Test-only explicit no-video composition; absent admitted content is never interpreted as this setting.</summary>
    internal DaggerfallRuleset(bool videosEnabled) => _videosEnabled = videosEnabled;

    public RulesetId Id => Identity;

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
            Sky = DaggerfallSkyMedia.Read(admitted.Content),
            CinematicContent = admitted.Content,
            VideosEnabled = _videosEnabled,
            QuestAdmission = new DaggerfallQuestRuntimeAdmission(admitted.QuestReceipts),
            DisabledQuestSelection = admitted.DisabledQuestSelection,
            Music = admitted.Music,
            Blocks = admitted.Blocks,
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
                    && pack.Role != QuestCorpusRole && pack.Role != FightersGuildQuestCorpusRole)
                    throw new InvalidOperationException($"Content pack '{pack.Id.Value}' declares role '{pack.Role.Value}', which the Daggerfall ruleset does not interpret.");
            }
            DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(
                RequireSingle(selected, roles, BaseRole).Payload, RequireSingle(selected, roles, ImportedRole).Payload);
            DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(RequireSingle(selected, roles, BlocksRole).Payload);
            blocks.AdmitLocations(definitions.Locations);
            // The composition keeps bundle order with each pack's dependencies ahead of it, so the first
            // site pack is the first site the bundle selects: that site is where a new game starts.
            DaggerfallSiteProfile[] sites = [.. roles[SiteRole].Select(pack => DaggerfallSiteContent.Read(selected.Content, pack.Payload, definitions))];
            if (sites.Length == 0)
                throw new InvalidOperationException($"Game bundle '{selected.Bundle.Id.Value}' selects no '{SiteRole.Value}' content pack, so a new game has nowhere to start.");
            DaggerfallSiteProfile inputs = sites.FirstOrDefault(site => site.VariantName is null)
                ?? throw new InvalidOperationException("A bundle requires a base site before world variants can be selected.");
            IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> fightersGuildQuests =
                [.. roles[FightersGuildQuestCorpusRole].SelectMany(pack => DaggerfallFightersGuildQuestCorpusContent.Read(selected.Content, pack.Payload, definitions))];
            DaggerfallClassicQuestCorpusReceipt[] classicCorpus =
                [.. roles[QuestCorpusRole].SelectMany(pack => DaggerfallClassicQuestCorpusContent.Read(selected.Content, pack.Payload, definitions))];
            DaggerfallDisabledQuestSelection disabledQuestSelection = DaggerfallDisabledQuestSelection.From(classicCorpus);
            IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> classicQuestReceipts =
            [
                .. classicCorpus.Where(receipt => receipt.IsOrdinaryOffer).Select(receipt => receipt.Runtime),
                .. disabledQuestSelection.Receipts,
            ];
            DaggerfallPublishedClassicMedia classicMedia = DaggerfallPublishedClassicMedia.Read(selected.Content, inputs.ClassicPresentation);
            foreach (DaggerfallSiteProfile site in sites.Where(site => !ReferenceEquals(site, inputs)))
                _ = DaggerfallPublishedClassicMedia.Read(selected.Content, site.ClassicPresentation);
            // Every admitted site names its cues against the same published manifest, so each one is
            // joined here: a site whose music nothing published would otherwise fail on entry rather
            // than at composition, where the publication it disagrees with is still identifiable.
            DaggerfallMusicBundle? music = DaggerfallMusicBundle.Admit(selected.Content, inputs.Music);
            foreach (DaggerfallSiteProfile site in sites.Where(site => !ReferenceEquals(site, inputs)))
                _ = DaggerfallMusicBundle.Admit(selected.Content, site.Music);
            DaggerfallTuning tuning = DaggerfallTuning.Read(selected.Tuning.Payload.Span);
            DaggerfallSiteProfiles profiles = new(sites);
            foreach (DaggerfallWorldProfileKey key in profiles.Keys)
                profiles.Require(key).InteriorBuilding?.ValidateAgainst(blocks);
            return new DaggerfallAdmittedContent(definitions, blocks, inputs, profiles, [.. fightersGuildQuests, .. classicQuestReceipts], disabledQuestSelection, tuning, classicMedia, new DaggerfallSiteAudioBundles(selected.Content, profiles), selected.Content, music);
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
        Rusty.Engine.ProductContent Content,
        DaggerfallMusicBundle? Music);
}
