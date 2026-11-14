namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Why a service request, a guild's or a shop's, is refused.</summary>
internal enum DaggerfallServiceDenial
{
    None,
    UnknownProvider,
    ProviderUnavailable,
    ServiceUnavailable,
    SiteChanged,
    Closed,
    NotMember,
    InsufficientRank,
    MissingItem,
    ItemChanged,
    InsufficientFunds,
    GrantUnavailable,
    AlreadySubmitted,
    WorkNotReady,
    WorkUnavailable,
}
