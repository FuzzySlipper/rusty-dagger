using System.Runtime.CompilerServices;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

public sealed class DaggerfallRuleset : ISaveableGameRuleset
{
    private readonly ConditionalWeakTable<ResolvedGameComposition, DaggerfallAdmittedContent> _admittedContent = [];
    public static readonly RulesetId Identity = new("daggerfall");
    internal static readonly ContentPackId BasePack = new("daggerfall.base");
    internal static readonly ContentPackId BlocksPack = new("daggerfall.blocks");
    internal static readonly ContentPackId PrivateersHoldPack = new("daggerfall.privateers-hold");

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
            admitted.Content);
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
            admitted.Content);
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
            DaggerfallTuning tuning = DaggerfallTuning.Read(selected.Tuning.Payload.Span);
            return new DaggerfallAdmittedContent(definitions, blocks, inputs, tuning, new DaggerfallAudioBundle(selected.Content, inputs.Audio), selected.Content);
        });

    private sealed record DaggerfallAdmittedContent(
        DaggerfallDefinitions Definitions,
        DaggerfallBlocksSnapshot Blocks,
        PrivateersHoldInputs Inputs,
        DaggerfallTuning Tuning,
        DaggerfallAudioBundle Audio,
        Rusty.Engine.ProductContent Content);
}
