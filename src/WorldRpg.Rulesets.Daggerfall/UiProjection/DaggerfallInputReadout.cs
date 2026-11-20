using System.Globalization;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The input readout's line: the player's state now, and the latest input refused or dropped while it is readable.</summary>
internal sealed record DaggerfallInputReadoutView(string State, string? Refusal);

/// <summary>
/// The developer's input readout (<c>hud.readout</c>), off by default and never saved: the player's live animation and
/// combat state read from the owners that decide it (the weapon, the attack clock and its press waiting, admission's
/// pending blow and recovery, the guard, the spell ready, and the first-person arms' clip with any clip fading out
/// under it), and the latest refusal from <see cref="DaggerfallInputRefusals"/>. It reads only; the HUD draws it.
/// </summary>
/// <param name="held">The 3D first-person weapon drawn now, or null for none or the classic sprite.</param>
/// <param name="weaponDrawn">Whether the player's weapon is drawn.</param>
internal sealed class DaggerfallInputReadout(DaggerfallPlayerAttack attack, ActorsState actors, DaggerfallContactGuard guard,
    DaggerfallCasting casting, DaggerfallDefinitions definitions, Func<DaggerfallHeldViewmodel?> held, Func<bool> weaponDrawn,
    DaggerfallInputRefusals refusals) : IDeveloperReadoutCommands
{
    public bool On { get; set; }

    /// <summary>The readout while it is on, or null.</summary>
    internal DaggerfallInputReadoutView? Read() => On ? new(State(), refusals.Latest?.ToString()) : null;

    private string State()
    {
        List<string> parts = [weaponDrawn() ? "weapon drawn" : "weapon sheathed"];
        DaggerfallAttackClockReading clock = attack.Clock.Read();
        parts.Add(clock.Attack is null ? "attack ready"
            : clock.Duration is double duration ? Invariant($"{clock.Attack} {clock.Phase} {clock.Seconds:0.00}/{duration:0.00} s")
            : Invariant($"{clock.Attack} {clock.Phase} {clock.Seconds:0.00} s"));
        if (clock.Chain > 0) parts.Add($"chain {clock.Chain}");
        if (clock.Waiting is { } waiting) parts.Add($"press waiting: {waiting}");
        if (actors.Player.Actor.Get<AttackState>().Pending is not null) parts.Add("blow pending");
        if (attack.RecoverySecondsLeft > 0d) parts.Add(Invariant($"recovery {attack.RecoverySecondsLeft:0.00} s"));
        parts.Add(guard.Raised ? "guard up" : "guard down");
        parts.Add(casting.ReadyFor(actors.Player.DurableId) is { } ready
            ? $"spell ready: {definitions.Magic.Spells[ready.SpellKey].Name.TrimStart('!')}" : "no spell ready");
        if (held() is { } arms)
        {
            var clip = arms.Clip;
            string playing = clip.Playing is { } frame
                ? Invariant($"{frame.Clip}{(frame.Phase is { } phase ? $" {phase.ToString().ToLowerInvariant()}" : "")} at {frame.Time:0.00}")
                : "no clip";
            parts.Add(clip.From is { } from ? Invariant($"arms: {playing}, blending from {from} {clip.Blend:0.00}") : $"arms: {playing}");
        }
        else if (weaponDrawn()) parts.Add("arms: classic sprite");
        return string.Join(" · ", parts);
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
