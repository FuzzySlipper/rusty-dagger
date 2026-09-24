using System.Runtime.CompilerServices;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

public sealed class DaggerfallRuleset : ISaveableGameRuleset
{
    private readonly ConditionalWeakTable<ResolvedGameComposition, DaggerfallAdmittedContent> _admittedContent = [];
    private readonly bool _videosEnabled;
    public static readonly RulesetId Identity = new("daggerfall");
    internal static readonly ContentPackId BasePack = new("daggerfall.base");
    internal static readonly ContentPackId BlocksPack = new("daggerfall.blocks");
    internal static readonly ContentPackId PrivateersHoldPack = new("daggerfall.privateers-hold");
    internal static readonly ContentPackId FightersGuildQuestPack = new("daggerfall.quests.fighters");
    internal static readonly IReadOnlyList<ContentPackId> ClassicQuestCorpusPacks =
    [
        new("daggerfall.quests.mages"), new("daggerfall.quests.temples"), new("daggerfall.quests.social"),
        new("daggerfall.quests.witches-commoners"), new("daggerfall.quests.merchants-vampires"),
        new("daggerfall.quests.disabled"), new("daggerfall.quests.nobility"),
    ];
    internal static readonly ContentPackId CastleNecromoghanPack = new("daggerfall.castle-necromoghan");
    internal static readonly ContentPackId CharingExteriorPack = new("daggerfall.charing-exterior");
    internal static readonly ContentPackId CharingInteriorPack = new("daggerfall.charing-interior-1-1-0");

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
        DaggerfallAdmittedContent admitted = Admit(context.Composition);
        DaggerfallSession session = DaggerfallSession.Restore(
            context.Engine,
            context.CompositionIdentity,
            admitted.Definitions,
            admitted.Inputs,
            admitted.Tuning,
            saved,
            context.Engine.Random,
            admitted.Audio,
            admitted.Content,
            _videosEnabled,
            new DaggerfallQuestRuntimeAdmission(admitted.QuestReceipts),
            admitted.Profiles,
            admitted.DisabledQuestSelection);
        session.AdmitSiteProfiles(admitted.Profiles);
        session.Site.AdmitBuildingNames(context.Engine.Random, admitted.Definitions, admitted.Blocks);
        return session;
    }

    /// <summary>A fresh session: no saved state exists, so nothing is resolved or reported.</summary>
    private IGameSession CreateSessionCore(GameSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Composition.Ruleset != Identity)
            throw new InvalidOperationException($"Daggerfall cannot interpret ruleset '{context.Composition.Ruleset.Value}'.");
        DaggerfallAdmittedContent admitted = Admit(context.Composition);
        DaggerfallSession session = new(
            context.Engine,
            context.CompositionIdentity,
            admitted.Definitions,
            admitted.Inputs,
            admitted.Tuning,
            admitted.Audio,
            admitted.Content,
            _videosEnabled,
            new DaggerfallQuestRuntimeAdmission(admitted.QuestReceipts),
            admitted.DisabledQuestSelection);
        session.AdmitSiteProfiles(admitted.Profiles);
        session.Site.AdmitBuildingNames(context.Engine.Random, admitted.Definitions, admitted.Blocks);
        return session;
    }

    private DaggerfallAdmittedContent Admit(ResolvedGameComposition composition) =>
        _admittedContent.GetValue(composition, static selected =>
        {
            if (selected.Ruleset != Identity)
                throw new InvalidOperationException($"Daggerfall cannot interpret ruleset '{selected.Ruleset.Value}'.");
            DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(selected.RequireContentPack(BasePack).Payload);
            DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(selected.RequireContentPack(BlocksPack).Payload);
            ContentPack pack = selected.RequireContentPack(PrivateersHoldPack);
            PrivateersHoldInputs inputs = PrivateersHoldContent.Read(selected.Content, pack.Payload, definitions);
            ContentPack fighters = selected.RequireContentPack(FightersGuildQuestPack);
            IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> fightersGuildQuests = DaggerfallFightersGuildQuestCorpusContent.Read(selected.Content, fighters.Payload, definitions);
            ContentPack disabled = selected.RequireContentPack(new ContentPackId("daggerfall.quests.disabled"));
            DaggerfallDisabledQuestSelection disabledQuestSelection = DaggerfallDisabledQuestSelection.Read(selected.Content, disabled.Payload, definitions);
            IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> classicQuestReceipts =
            [
                .. ClassicQuestCorpusPacks.Where(id => id.Value != "daggerfall.quests.disabled")
                    .SelectMany(id => DaggerfallClassicQuestCorpusContent.Read(selected.Content, selected.RequireContentPack(id).Payload, definitions, DaggerfallClassicQuestCorpusExpectations.Require(id.Value["daggerfall.quests.".Length..]))),
                .. disabledQuestSelection.Receipts,
            ];
            ContentPack castle = selected.RequireContentPack(CastleNecromoghanPack);
            PrivateersHoldInputs castleInputs = PrivateersHoldContent.Read(selected.Content, castle.Payload, definitions);
            ContentPack charingExterior = selected.RequireContentPack(CharingExteriorPack);
            PrivateersHoldInputs charingExteriorInputs = PrivateersHoldContent.Read(selected.Content, charingExterior.Payload, definitions);
            ContentPack charingInterior = selected.RequireContentPack(CharingInteriorPack);
            PrivateersHoldInputs charingInteriorInputs = PrivateersHoldContent.Read(selected.Content, charingInterior.Payload, definitions);
            DaggerfallPublishedClassicMedia classicMedia = DaggerfallPublishedClassicMedia.Read(selected.Content, inputs.ClassicPresentation);
            _ = DaggerfallPublishedClassicMedia.Read(selected.Content, castleInputs.ClassicPresentation);
            _ = DaggerfallPublishedClassicMedia.Read(selected.Content, charingExteriorInputs.ClassicPresentation);
            _ = DaggerfallPublishedClassicMedia.Read(selected.Content, charingInteriorInputs.ClassicPresentation);
            DaggerfallTuning tuning = DaggerfallTuning.Read(selected.Tuning.Payload.Span);
            DaggerfallSiteProfiles profiles = new([inputs, castleInputs, charingExteriorInputs, charingInteriorInputs]);
            foreach (DaggerfallWorldProfileKey key in profiles.Keys)
                profiles.Require(key).InteriorBuilding?.ValidateAgainst(blocks);
            return new DaggerfallAdmittedContent(definitions, blocks, inputs, profiles, [.. fightersGuildQuests, .. classicQuestReceipts], disabledQuestSelection, tuning, classicMedia, new DaggerfallSiteAudioBundles(selected.Content, profiles), selected.Content);
        });

    private sealed record DaggerfallAdmittedContent(
        DaggerfallDefinitions Definitions,
        DaggerfallBlocksSnapshot Blocks,
        PrivateersHoldInputs Inputs,
        DaggerfallSiteProfiles Profiles,
        IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> QuestReceipts,
        DaggerfallDisabledQuestSelection DisabledQuestSelection,
        DaggerfallTuning Tuning,
        DaggerfallPublishedClassicMedia ClassicMedia,
        DaggerfallSiteAudioBundles Audio,
        Rusty.Engine.ProductContent Content);
}
