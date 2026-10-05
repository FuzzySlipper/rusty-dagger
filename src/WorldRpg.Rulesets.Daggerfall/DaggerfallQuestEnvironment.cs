using System.Globalization;
using System.Text.RegularExpressions;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestSoundState(long LastPlayedSecond, int Played);

internal static partial class DaggerfallQuestTaskCompiler
{
    private static DaggerfallQuestTaskOperation? CompileEnvironment(string line, int sourceLine)
    {
        if (Regex.Match(line, @"^(climate|season|weather)\s+(base\s+)?([a-z0-9]+)$", RegexOptions.IgnoreCase) is { Success: true } condition)
        {
            var kind = Enum.Parse<DaggerfallQuestTaskOperationKind>(condition.Groups[1].Value, true);
            return new(kind, sourceLine, line, [(condition.Groups[2].Value + condition.Groups[3].Value).Trim().ToLowerInvariant()], [], null);
        }
        if (Regex.Match(line, @"^play\s+(song|video)\s+([a-zA-Z0-9_-]+)$", RegexOptions.IgnoreCase) is { Success: true } media)
            return new(media.Groups[1].Value.Equals("song", StringComparison.OrdinalIgnoreCase) ? DaggerfallQuestTaskOperationKind.PlaySong : DaggerfallQuestTaskOperationKind.PlayVideo,
                sourceLine, line, [media.Groups[2].Value], [], null);
        if (Regex.Match(line, @"^play\s+sound\s+(\w+)\s+(?:every\s+(\d+)\s+minutes\s+(\d+)\s+times|(\d+)\s+(\d+))$", RegexOptions.IgnoreCase) is { Success: true } sound)
            return new(DaggerfallQuestTaskOperationKind.PlaySound, sourceLine, line,
                [sound.Groups[1].Value, sound.Groups[2].Success ? sound.Groups[2].Value : sound.Groups[4].Value,
                    sound.Groups[3].Success ? sound.Groups[3].Value : "0"], [], null);
        return null;
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private Func<DaggerfallQuestTaskOperation, DaggerfallCalendar, bool>? _environmentCondition;
    private Func<DaggerfallQuestRuntimeInstance, DaggerfallQuestTaskOperation, DaggerfallQuestTaskRuntimeState, int, DaggerfallCalendar, bool>? _mediaAction;
    internal void BindEnvironmentActions(Func<DaggerfallQuestTaskOperation, DaggerfallCalendar, bool> condition,
        Func<DaggerfallQuestRuntimeInstance, DaggerfallQuestTaskOperation, DaggerfallQuestTaskRuntimeState, int, DaggerfallCalendar, bool> media)
    { _environmentCondition = condition; _mediaAction = media; }
    bool IDaggerfallQuestTaskLifecycle.EnvironmentCondition(DaggerfallQuestTaskOperation operation, DaggerfallCalendar calendar) =>
        (_environmentCondition ?? throw new NotSupportedException("No quest environment owner is composed."))(operation, calendar);
    bool IDaggerfallQuestTaskLifecycle.MediaAction(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation,
        DaggerfallQuestTaskRuntimeState state, int index, DaggerfallCalendar calendar) =>
        (_mediaAction ?? throw new NotSupportedException("No quest media owner is composed."))(instance, operation, state, index, calendar);
}

internal sealed partial class DaggerfallSession
{
    // The operation remains unfinished in the save until Engine reports completion or the player
    // skips. Restoring an unfinished operation restarts its media through the same presentation owner.
    private (string Instance, int SourceLine)? _questVideoOwner;
    private bool QuestEnvironmentCondition(DaggerfallQuestTaskOperation operation, DaggerfallCalendar calendar)
    {
        string expected = operation.Targets.Single();
        if (operation.Kind == DaggerfallQuestTaskOperationKind.Season)
        {
            if (expected == "fall") expected = "autumn";
            if (!Enum.TryParse<DaggerfallSeason>(expected, true, out var season)) throw new NotSupportedException($"Unknown quest season '{expected}'.");
            return calendar.Season == season;
        }
        if (operation.Kind == DaggerfallQuestTaskOperationKind.Weather)
        {
            if (!Enum.TryParse<DaggerfallWeatherKind>(expected, true, out var weather)) throw new NotSupportedException($"Unknown quest weather '{expected}'.");
            return CurrentWeather == weather;
        }
        if (!(new[] { "base desert", "base mountain", "base swamp", "base temperate", "ocean", "desert", "desert2", "mountain", "rainforest", "swamp", "subtropical", "mountainwoods", "woodlands", "hauntedwoodlands" }).Contains(expected))
            throw new NotSupportedException($"Unknown quest climate '{expected}'.");
        int climate = CurrentClimate;
        if (expected.StartsWith("base ", StringComparison.Ordinal))
            return expected[5..] == (climate switch { 224 or 225 or 229 => "desert", 226 => "mountain", 223 or 227 or 228 => "swamp", 230 or 231 or 232 => "temperate", _ => throw new NotSupportedException($"Unresolved climate {climate}.") });
        string actual = climate switch { 223 => "ocean", 224 => "desert", 225 => "desert2", 226 => "mountain", 227 => "rainforest", 228 => "swamp", 229 => "subtropical", 230 => "mountainwoods", 231 => "woodlands", 232 => "hauntedwoodlands", _ => throw new NotSupportedException($"Unresolved climate {climate}.") };
        return expected == actual;
    }

    private bool QuestMediaAction(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation,
        DaggerfallQuestTaskRuntimeState task, int index, DaggerfallCalendar calendar)
    {
        try
        {
            switch (operation.Kind)
            {
                case DaggerfallQuestTaskOperationKind.PlaySound:
                    if (!_definitions.QuestSources.Tables.Sounds.Lookup.TryGetValue(operation.Targets[0], out int soundId))
                        throw new NotSupportedException($"Unknown quest sound '{operation.Targets[0]}'.");
                    var clip = _sites.Projection.Inputs.Audio.FirstOrDefault(clip => clip.SourceNumericId == soundId)
                        ?? throw new NotSupportedException($"Quest sound '{operation.Targets[0]}' ({soundId}) has no admitted ordinary audio asset.");
                    long second = calendar.ToAbsoluteSeconds();
                    long interval = checked(long.Parse(operation.Targets[1], CultureInfo.InvariantCulture) * 60);
                    int count = int.Parse(operation.Targets[2], CultureInfo.InvariantCulture);
                    var sound = task.OperationState[index].Sound ?? new(second, 0);
                    task.OperationState[index] = task.OperationState[index] with { Sound = sound };
                    if (count > 0 && sound.Played >= count) return true;
                    if (second - sound.LastPlayedSecond < interval) return false;
                    _appearance.PlayQuestSound(clip.Id, instance.InstanceId, operation.SourceLine, sound.Played + 1);
                    sound = new(second, checked(sound.Played + 1));
                    task.OperationState[index] = task.OperationState[index] with { Sound = sound, UnavailableReason = null };
                    return count > 0 && sound.Played >= count;
                case DaggerfallQuestTaskOperationKind.PlaySong:
                    string track = operation.Targets.Single().ToLowerInvariant();
                    if (_musicBundle?.CanPlay(track) != true || _music is null)
                        throw new NotSupportedException($"Quest song '{track}' has no admitted ordinary audio asset; MIDI playback is excluded.");
                    if (_music.Cue(track) != track) throw new NotSupportedException($"Quest song '{track}' could not start.");
                    return true;
                case DaggerfallQuestTaskOperationKind.PlayVideo:
                    if (!int.TryParse(operation.Targets.Single(), out int number) || number is < 0 or > 9999)
                        throw new NotSupportedException("Quest video requires a source video number from 0 through 9999.");
                    if (!_composition.VideosEnabled) throw new NotSupportedException("Quest video is disabled by the current presentation preference.");
                    if (Cinematics is null) throw new NotSupportedException("Quest video has no admitted Engine cinematic capability.");
                    if (_questVideoOwner == (instance.InstanceId, operation.SourceLine))
                    {
                        if (Cinematics.ActiveSource is not null) return false;
                        var result = Cinematics.TakeResult();
                        _questVideoOwner = null;
                        if (result?.Kind is Rusty.Engine.VideoRealizationFactKind.Completed or Rusty.Engine.VideoRealizationFactKind.Skipped)
                            return true;
                        throw new NotSupportedException(result?.Failure ?? "Quest video ended without a completion result.");
                    }
                    if (Cinematics.ActiveSource is not null) return false;
                    Cinematics.Play($"ANIM{number:0000}.VID");
                    _questVideoOwner = (instance.InstanceId, operation.SourceLine);
                    return false;
                default: throw new NotSupportedException($"Unknown quest media action '{operation.Kind}'.");
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or KeyNotFoundException)
        { throw new NotSupportedException(error.Message, error); }
    }
}
