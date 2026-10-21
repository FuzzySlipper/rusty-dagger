using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession : ISaveDescribingGameSession
{
    /// <summary>What a save slot shows about this game: the character, their level, where they are, the date and the time played.</summary>
    public SaveDescription DescribeSave() => new(
        State.Character.Identity.Name,
        State.Progression.Level,
        _site.ActiveSite?.Name,
        $"{_time.Calendar.DescribeDate()}, {_time.Calendar.DescribeTime()}",
        _playSeconds);
}
