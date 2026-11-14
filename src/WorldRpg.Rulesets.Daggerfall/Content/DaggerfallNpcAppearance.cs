namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One NPC's appearance: what the world sees.</summary>
/// <param name="Race">The race name.</param>
/// <param name="Gender">Male or Female.</param>
/// <param name="BillboardArchive">The billboard texture archive.</param>
/// <param name="BillboardRecord">The billboard texture record.</param>
/// <param name="NameSeed">The seed the name derives from.</param>
/// <param name="FactionId">The faction the NPC answers to, or zero.</param>
public readonly record struct DaggerfallNpcAppearance(string Race, string Gender, int BillboardArchive, int BillboardRecord, int NameSeed, int FactionId);
