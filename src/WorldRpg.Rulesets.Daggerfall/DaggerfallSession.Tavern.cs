using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallTavernView? CurrentTavern(string revision) =>
        _dialogue?.CurrentNpc(revision) is { } npc && npc.Services.Contains(DaggerfallTavernService.ServiceName)
        && CurrentInteriorBuilding()?.BuildingType == 15 ? State.Tavern.Read() : null;

    private void BuyTavernMeal(DaggerfallPlayerUiAction action)
    {
        if (action.Revision is not { } revision || action.Key is not { } item || action.Amount is not { } price
            || CurrentTavern(revision) is null || _dialogue?.CurrentNpc(revision) is not { } npc)
        { Presentation.SetOutcome("That tavern keeper is no longer available."); return; }
        Presentation.SetOutcome(State.Tavern.Buy(new($"tavern:{revision}:{item}:{_time.Calendar.ToAbsoluteSeconds() / 60}",
            new(npc.DurableId, npc.Site, DaggerfallTavernService.ServiceName)), item, price));
    }
}
