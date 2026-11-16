using Rusty.Engine;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>What the world needs time for in one update, read from the domains that know it.</summary>
/// <param name="Presentation">The title, an opening cinematic or a playing cinematic runs on world time.</param>
/// <param name="Playing">The world is open to play: no panel, modal, offer, defeat or pending choice holds it.</param>
/// <param name="Moving">The player moves, means to, or still settles from movement.</param>
/// <param name="Acting">An action, a strike, a flight, a door or the site is still in motion.</param>
internal readonly record struct DaggerfallPlayerTimeDemand(bool Presentation, bool Playing, bool Moving, bool Acting);

/// <summary>
/// The world's Engine time. A world held by a screen, an offer, a pending choice, a pause or a defeat admits no step,
/// so nothing counted in steps (a cooldown, an enemy's wind-up) ages behind it. Action-driven play: with it chosen,
/// the world runs only while the player moves or acts (or a presentation plays) and holds still otherwise; without it
/// the open world runs at its ordinary rate. It also keeps the world
/// action a panel asked for while no step was due, so the next playing update spends one. The session reads what
/// the world needs time for into a <see cref="DaggerfallPlayerTimeDemand"/>; this owner selects the Engine's
/// gameplay rate from it and never advances a clock.
/// </summary>
internal sealed class DaggerfallPlayerTime(IGameplayTimeService time)
{
    private readonly ActionDrivenTime _time = new(time);

    /// <summary>Whether action-driven play is holding the world still now.</summary>
    internal bool Held => _time.Held;

    /// <summary>
    /// A semantic action (a spell, an item used, moved or dropped, a transport chosen) that must spend a world step
    /// once play resumes, though it arrived while none was due.
    /// </summary>
    internal bool WorldActionPending { get; private set; }

    internal void RequestWorldAction() => WorldActionPending = true;

    /// <summary>A step was spent, or explicit elapsed time already passed, so the pending action has had its time.</summary>
    internal void ClearWorldAction() => WorldActionPending = false;

    /// <summary>Chooses action-driven play or the ordinary rate.</summary>
    internal void Select(bool actionDriven) => _time.Select(actionDriven);

    /// <summary>
    /// Selects the world's rate for this update: held while neither presentation nor play needs time; otherwise
    /// running at <paramref name="rate"/>, or under action-driven play only while presentation plays or play is
    /// moving or acting. True when that changed whether action-driven play holds the world.
    /// </summary>
    internal bool Update(bool actionDriven, DaggerfallPlayerTimeDemand demand, double rate)
    {
        bool wasHeld = _time.Held;
        _time.Select(actionDriven);
        _time.Update(demand.Presentation || demand.Playing && (demand.Moving || demand.Acting), rate,
            worldHeld: !demand.Presentation && !demand.Playing);
        return wasHeld != _time.Held;
    }
}
