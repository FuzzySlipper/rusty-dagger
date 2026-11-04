namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The sound a negated harm makes at whoever it reached: turned aside, or taken on armour or a ward.</summary>
internal enum DaggerfallOutcomeCue { Deflected, Absorbed }

/// <summary>
/// The harm presentation seam (<see cref="DaggerfallRulesSeams.HarmPresentation"/>): which outcome sound, if any,
/// a delivered spell effect's negation makes. The presentation owners play it; this owner only decides it.
/// </summary>
internal interface IDaggerfallHarmPresentation
{
    DaggerfallOutcomeCue? SpellOutcomeCue(DaggerfallCastOutcome outcome);
}
