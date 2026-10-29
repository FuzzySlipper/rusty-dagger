using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;

using WorldRpg.Rulesets.Daggerfall.Presentation;

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

    internal DaggerfallSkyMedia? Sky { get; init; }

    /// <summary>The derived content the composition reads; the cinematics and the UI art come from it, and null composes neither.</summary>
    internal DaggerfallData? Data { get; init; }

    /// <summary>Whether admitted cinematic video plays; a composition choice, never inferred from absent content.</summary>
    internal bool VideosEnabled { get; init; } = true;

    internal DaggerfallQuestRuntimeAdmission? QuestAdmission { get; init; }

    /// <summary>Named content selected by this bundle for committed new characters; loading never starts it again.</summary>
    internal IReadOnlyList<string> NewGameQuests { get; init; } = [];

    internal DaggerfallDisabledQuestSelection? DisabledQuestSelection { get; init; }

    /// <summary>The score read by name from the music container; null composes a silent session.</summary>
    internal DaggerfallMusicBundle? Music { get; init; }

    /// <summary>The 3D first-person art installed beside the product, or none; drawn when the viewmodel tuning asks for it.</summary>
    internal DaggerfallHeldArt? HeldArt { get; init; }

    /// <summary>The compiled effect catalog; null composes the ruleset's own disease and poison families.</summary>
    internal DaggerfallEffectCatalog? Effects { get; init; }

    /// <summary>The owner selected for each rules seam; classic unless a selected variant claims it.</summary>
    internal DaggerfallSeamSelection Seams { get; init; } = DaggerfallSeamSelection.Classic;

    /// <summary>The classic block records building names are admitted from; null admits none.</summary>
    internal DaggerfallBlocksSnapshot? Blocks { get; init; }

    /// <summary>The host's loading screen, where slow world changes accepted in an update are staged; null runs them at once.</summary>
    internal LoadingScreen? Loading { get; init; }
}
