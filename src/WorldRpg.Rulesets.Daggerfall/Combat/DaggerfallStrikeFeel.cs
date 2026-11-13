using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The blow's weight on screen: the view knocks and world time catches for an instant. A world some other owner has
/// slowed or held, such as a playtest's, is left alone.
/// </summary>
/// <param name="worldRate">The rate world time runs at now.</param>
internal sealed class DaggerfallStrikeFeel(DaggerfallStrikeTuning tuning, FirstPersonCameraSystem camera, Func<double> worldRate)
{
    private int _hitStopSteps;

    /// <summary>Whether a landed blow is catching world time now.</summary>
    internal bool HitStopped => _hitStopSteps > 0;

    /// <summary>The rate the blow asks world time to run at.</summary>
    internal double TimeRate => _hitStopSteps > 0 ? tuning.HitStopRate : 1d;

    internal void Feel(ActorStruckFact struck)
    {
        camera.Punch(-(struck.Heavy ? tuning.HeavyPunchDegrees : tuning.PunchDegrees), 0d);
        if (worldRate() == 1d) _hitStopSteps = struck.Heavy ? tuning.HeavyHitStopSteps : tuning.HitStopSteps;
    }

    /// <summary>One admitted step: the knock settles and the catch counts down.</summary>
    internal void Settle(double seconds)
    {
        camera.SettlePunch(seconds, tuning.PunchSettleRate);
        if (_hitStopSteps > 0) _hitStopSteps--;
    }
}
