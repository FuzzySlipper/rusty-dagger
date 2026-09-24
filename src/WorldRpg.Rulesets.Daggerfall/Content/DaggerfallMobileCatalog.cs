namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// One classic mobile as the published catalog states it: the donor's own parameters, the identity this
/// product publishes for it, and the disposition that reconciles the two.
/// </summary>
internal sealed record DaggerfallMobileDefinition(
    int DonorId,
    string DonorName,
    string Identity,
    bool CastsMagic,
    bool SeesThroughInvisibility,
    string? Actor,
    string Disposition,
    string Behaviour,
    string Affinity,
    int MaleTexture,
    int FemaleTexture,
    int CorpseArchive,
    int CorpseRecord,
    bool HasIdle,
    bool HasRangedAttack1,
    bool HasRangedAttack2,
    string MoveSound,
    string BarkSound,
    string AttackSound,
    string? LootTableKey,
    string MinMetalToHit,
    int MinimumDamage,
    int MaximumDamage,
    int MinimumHealth,
    int MaximumHealth,
    int Level,
    int ArmorValue,
    bool ParrySounds,
    int MapChance,
    int Weight,
    string Team,
    int AttackModifierFlags)
{
    /// <summary>Whether this mobile is one the product places as an actor.</summary>
    internal bool IsPublished => Disposition is "published" or "published-variant";

    /// <summary>EnemyBasics caster capability retained for the later compiled spell behavior (task #7083).</summary>
    internal bool RequiresEnemySpellBehavior => CastsMagic;

    /// <summary>The damage range the donor states, or null when the mobile carries none.</summary>
    internal (int Minimum, int Maximum)? DamageRange =>
        MinimumDamage == 0 && MaximumDamage == 0 ? null : (MinimumDamage, MaximumDamage);

    /// <summary>The health range the donor states, or null when the mobile carries none.</summary>
    internal (int Minimum, int Maximum)? HealthRange =>
        MinimumHealth == 0 && MaximumHealth == 0 ? null : (MinimumHealth, MaximumHealth);
}

/// <summary>
/// The published mobile catalog an actor-construction consumer resolves through. Like the magical
/// catalogs it is read from the pack alone: a consumer resolves a mobile's parameters and the actor it
/// belongs to without opening the donor's source table.
/// </summary>
internal sealed record DaggerfallMobileCatalogSet(
    IReadOnlyDictionary<int, DaggerfallMobileDefinition> Mobiles,
    IReadOnlyDictionary<string, DaggerfallMobileDefinition> ByActor,
    IReadOnlyList<string> SourceRecords)
{
    /// <summary>
    /// Every mobile the donor defines and nothing in this product carries. A human mobile is not listed
    /// here: the product publishes the humanoid classes through its careers, so only a record the catalog
    /// itself calls unpublished is a coverage gap.
    /// </summary>
    internal IEnumerable<DaggerfallMobileDefinition> Unpublished =>
        Mobiles.Values.Where(mobile => mobile.Disposition == "unpublished");

    /// <summary>The donor's human mobiles, which this product covers through its careers.</summary>
    internal IEnumerable<DaggerfallMobileDefinition> HumanMobiles =>
        Mobiles.Values.Where(mobile => mobile.Disposition == "human-mobile");

    /// <summary>The mobile record for one published actor, or null when the catalog does not name it.</summary>
    internal DaggerfallMobileDefinition? ForActor(string actorId) => ByActor.GetValueOrDefault(actorId);
}
