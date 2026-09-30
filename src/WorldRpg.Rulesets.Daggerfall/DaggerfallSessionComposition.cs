using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Everything one Daggerfall session is composed from: the admitted definitions, the site a new game
/// starts at, tuning, and the compiled composition's explicit choices. Both start kinds
/// (<see cref="DaggerfallSession.StartNew"/> and <see cref="DaggerfallSession.Restore"/>) take it, so
/// the ruleset and a test build the same graph through the same path.
/// </summary>
/// <param name="Definitions">The admitted base definitions.</param>
/// <param name="StartSite">Where a new game starts, and a restore's active site when no catalog is admitted.</param>
/// <param name="Tuning">The admitted tuning profile.</param>
/// <param name="Identity">The resolved composition's identity, which the HUD art publication names.</param>
internal sealed record DaggerfallSessionComposition(
    DaggerfallDefinitions Definitions,
    DaggerfallSiteProfile StartSite,
    DaggerfallTuning Tuning,
    ResolvedCompositionIdentity? Identity = null)
{
    /// <summary>The admitted site catalog; null composes a session over the start site alone.</summary>
    internal DaggerfallSiteProfiles? Profiles { get; init; }

    /// <summary>Each admitted site's audio bundle.</summary>
    internal DaggerfallSiteAudioBundles? Audio { get; init; }

    /// <summary>The published content the opening cinematics play from; null composes no cinematics.</summary>
    internal ProductContent? CinematicContent { get; init; }

    /// <summary>Whether admitted cinematic video plays; a composition choice, never inferred from absent content.</summary>
    internal bool VideosEnabled { get; init; } = true;

    internal DaggerfallQuestRuntimeAdmission? QuestAdmission { get; init; }

    internal DaggerfallDisabledQuestSelection? DisabledQuestSelection { get; init; }

    /// <summary>The product-wide music bundle; null composes a silent session.</summary>
    internal DaggerfallMusicBundle? Music { get; init; }

    /// <summary>The compiled effect catalog; null composes the ruleset's own disease and poison families.</summary>
    internal DaggerfallEffectCatalog? Effects { get; init; }

    /// <summary>The classic block records building names are admitted from; null admits none.</summary>
    internal DaggerfallBlocksSnapshot? Blocks { get; init; }
}
