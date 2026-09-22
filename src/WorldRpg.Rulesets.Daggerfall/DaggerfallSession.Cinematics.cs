using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Ruleset-owned entry and story cinematic policy over the admitted Engine video service.</summary>
internal sealed partial class DaggerfallSession
{
    public EntryScreenStartupResult StartEntry()
    {
        EntryScreenStartupResult result = _openingCinematics.Start();
        if (result == EntryScreenStartupResult.Failed)
            Presentation.SetOutcome(_openingCinematics.Failure?.Failure ?? "The opening cinematic could not start.");
        return result;
    }

    public bool TakeEntryReadyForPlay() => _openingCinematics.TakeReady();
}
