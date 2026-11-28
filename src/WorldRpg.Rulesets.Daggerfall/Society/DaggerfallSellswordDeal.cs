namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// A seat's sellswords out on a deal in force (#11223): seat <paramref name="Seat"/> (<paramref name="SeatName"/>, whose
/// office faction is <paramref name="OfficeFaction"/>) gives <paramref name="Amount"/> force every
/// <paramref name="EveryWeeks"/> weeks to <paramref name="PartnerName"/>, and sends out sellswords of party-hire spec
/// <paramref name="Spec"/>. They stand in the seat's own halls and in the taverns of <paramref name="Regions"/>, the classic
/// regions either side holds, while the deal lasts.
/// </summary>
internal sealed record DaggerfallSellswordDeal(string Seat, string SeatName, int OfficeFaction, string PartnerName, string Spec,
    IReadOnlyList<int> Regions, int Amount, int EveryWeeks);
