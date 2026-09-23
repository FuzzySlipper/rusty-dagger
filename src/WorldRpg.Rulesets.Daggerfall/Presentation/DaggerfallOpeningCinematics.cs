using Rusty.Engine;
using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The donor's new-game narrative ordering. Playback duration and terminal observation remain
/// Engine-owned; this policy only advances an explicitly named sequence once per terminal result.
/// </summary>
internal sealed class DaggerfallOpeningCinematics(DaggerfallCinematicPresentation? presentation, bool videosEnabled)
{
    private static readonly string[] Sources = ["ANIM0000.VID", "ANIM0011.VID", "DAG2.VID"];
    private readonly DaggerfallCinematicPresentation? _presentation = presentation;
    private readonly bool _videosEnabled = videosEnabled;
    private int _next;
    private bool _started;
    private bool _completed;
    private bool _readyToEnterPlay;
    private bool _failed;

    internal bool IsActive => _started && !_completed && !_failed;
    internal DaggerfallCinematicResult? Failure { get; private set; }

    internal EntryScreenStartupResult Start()
    {
        if (_completed) return EntryScreenStartupResult.ReadyForPlay;
        if (_failed) return EntryScreenStartupResult.Failed;
        if (_started) return EntryScreenStartupResult.Waiting;
        _started = true;

        // This is deliberately an explicit caller-provided setting. An absent content lease does not
        // stand in for disabled videos, because that would turn broken media admission into success.
        if (!_videosEnabled)
        {
            _completed = true;
            _readyToEnterPlay = true;
            return EntryScreenStartupResult.ReadyForPlay;
        }
        if (_presentation is null)
        {
            Fail("Cinematic playback is enabled but the admitted cinematic content is unavailable.");
            return EntryScreenStartupResult.Failed;
        }

        return PlayNext() ? EntryScreenStartupResult.Waiting : EntryScreenStartupResult.Failed;
    }

    internal void Poll()
    {
        if (!IsActive || _presentation is null) return;
        DaggerfallCinematicResult? result = _presentation.TakeResult();
        if (result is null) return;
        string expected = Sources[_next];
        if (!string.Equals(result.Source, expected, StringComparison.Ordinal))
        {
            Fail($"Expected cinematic '{expected}' but received the terminal result for '{result.Source}'.");
            return;
        }
        if (result.Kind is VideoRealizationFactKind.Failed)
        {
            Failure = result;
            _failed = true;
            return;
        }
        if (result.Kind is not (VideoRealizationFactKind.Completed or VideoRealizationFactKind.Skipped))
        {
            Fail($"Cinematic '{result.Source}' returned unsupported terminal result '{result.Kind}'.");
            return;
        }

        _next++;
        if (_next == Sources.Length)
        {
            _completed = true;
            _readyToEnterPlay = true;
            return;
        }
        _ = PlayNext();
    }

    internal bool TakeReady()
    {
        if (!_readyToEnterPlay) return false;
        _readyToEnterPlay = false;
        return true;
    }

    internal void Skip()
    {
        if (IsActive) _presentation!.Skip();
    }

    private bool PlayNext()
    {
        try
        {
            _presentation!.Play(Sources[_next]);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException)
        {
            Fail(error.Message);
            return false;
        }
    }

    private void Fail(string message)
    {
        Failure = new(Sources[Math.Min(_next, Sources.Length - 1)], VideoRealizationFactKind.Failed, message);
        _failed = true;
    }
}
