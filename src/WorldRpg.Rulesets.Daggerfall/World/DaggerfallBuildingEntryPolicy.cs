using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The facts one exterior building door is judged by: the placed building's source type, faction and
/// quality, the current hour and holiday, and the player's standing with the building.
/// </summary>
internal sealed record DaggerfallBuildingEntryFacts(
    int BuildingType,
    int FactionId,
    int Quality,
    int Hour,
    int HolidayId,
    bool OwnedByPlayer = false,
    bool ActiveQuestBuilding = false,
    bool GuildHallAccessAnytime = false,
    bool GuildMember = false,
    bool OwnsShip = false);

/// <summary>Why a closed public building is closed, for the Information-mode notice.</summary>
internal readonly record struct DaggerfallBuildingClosedNotice(bool GuildHall, int OpensAtHour, int ClosesAtHour);

/// <summary>
/// Whether a building's entrance admits the player now, the lock strength a locked entrance offers to
/// lockpicking, bashing and Open, and the closed notice Information mode shows for it.
/// </summary>
internal readonly record struct DaggerfallBuildingEntry(bool Unlocked, int LockValue, DaggerfallBuildingClosedNotice? Closed);

/// <summary>
/// Daggerfall's building-entry rule (the donor's <c>PlayerActivate.BuildingIsUnlocked</c>,
/// <c>IsBuildingOpen</c>, <c>GetBuildingLockValue</c> and the closed notice of <c>ActivateBuilding</c>).
/// A building's entrance is not the RMB door's own lock: it is locked or open by building type and
/// hour, and a locked one resists at the building's quality over two.
/// </summary>
internal static class DaggerfallBuildingEntryPolicy
{
    internal const int HouseForSale = 1;
    internal const int GuildHall = 11;
    internal const int Temple = 14;
    internal const int Tavern = 15;
    internal const int Palace = 16;
    internal const int House1 = 17;
    internal const int House2 = 18;
    internal const int House4 = 20;
    internal const int Ship = 24;
    /// <summary>The calendar's holiday identity for Sun's Rest, when every shop is closed.</summary>
    internal const int SunsRestHoliday = 31;

    // PlayerActivate.openHours/closeHours by source building type. Zero-to-zero is the always-locked
    // House1; a close hour of 25 never closes.
    private static readonly int[] OpenHours = [7, 8, 9, 8, 0, 9, 10, 10, 9, 6, 9, 11, 9, 9, 0, 0, 10, 0, 6, 6, 6, 6, 6, 6, 0];
    private static readonly int[] CloseHours = [22, 16, 19, 15, 25, 21, 19, 20, 18, 23, 23, 23, 20, 20, 25, 25, 16, 0, 18, 18, 18, 18, 18, 18, 25];

    /// <summary>The donor's public opening hours for a building type (<c>IsBuildingOpen</c>).</summary>
    internal static bool IsOpenHour(int buildingType, int hour) =>
        buildingType >= 0 && buildingType < OpenHours.Length
        && hour >= OpenHours[buildingType] && hour < CloseHours[buildingType];

    /// <summary>The donor's residences (<c>RMBLayout.IsResidence</c>), named only "Residence".</summary>
    internal static bool IsResidence(int buildingType) => buildingType is >= House1 and <= House4;

    /// <summary>
    /// The donor's quality-over-two building lock. The door owner treats a zero lock as no lock, so a
    /// locked building of quality one still offers the least real lock.
    /// </summary>
    internal static int LockValue(int quality) => Math.Max(1, Math.Max(0, quality) / 2);

    internal static DaggerfallBuildingEntry Evaluate(DaggerfallBuildingEntryFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Hour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(facts), "An entry hour must be a calendar hour.");
        bool unlocked = IsUnlocked(facts);
        int type = facts.BuildingType;
        // ActivateBuilding shows the hours only for a closed store or guild hall below Temple, never a house for sale.
        DaggerfallBuildingClosedNotice? closed = !unlocked && type is >= 0 and < Temple && type != HouseForSale
            ? new(type == GuildHall, OpenHours[type], CloseHours[type])
            : null;
        return new(unlocked, unlocked ? 0 : LockValue(facts.Quality), closed);
    }

    private static bool IsUnlocked(DaggerfallBuildingEntryFacts facts)
    {
        int type = facts.BuildingType;
        // A player-owned house is always open, as is a residence an active quest has placed something in.
        if (facts.OwnedByPlayer) return true;
        if (facts.ActiveQuestBuilding && type is >= House1 and <= 22) return true;
        if (type == GuildHall) return facts.GuildHallAccessAnytime || IsOpenHour(type, facts.Hour);
        // A Thieves Guild or Dark Brotherhood house admits only that guild's members.
        if (type == House2 && facts.FactionId != 0) return facts.GuildMember;
        if (type is >= House1 and <= House4) return IsOpenHour(type, facts.Hour);
        if (DaggerfallNpcServiceFacts.IsShop(type)) return facts.HolidayId != SunsRestHoliday && IsOpenHour(type, facts.Hour);
        if (type is >= 0 and <= Palace) return IsOpenHour(type, facts.Hour);
        return type == Ship && facts.OwnsShip;
    }
}
