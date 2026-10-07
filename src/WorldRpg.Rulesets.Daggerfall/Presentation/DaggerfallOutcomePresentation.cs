using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Daggerfall-specific attack and reward wording; generic presentation state only stores the resolved message.</summary>
internal sealed class DaggerfallOutcomePresentation(
    PresentationState presentation,
    IReadOnlyDictionary<long, DaggerfallActorDefinition> actors,
    DaggerfallTextSet? text = null)
{
    /// <summary>Admitted seconds one published outcome line stays on screen.</summary>
    internal const double MessageLifetimeSeconds = 6d;

    // Whether the published line reports something that happened rather than something that did not.
    private bool _lineIsResult;
    private SoulTrapResolvedFact? _soulTrap;

    /// <summary>
    /// The donor's line when an effect starts on the player. A drain also speaks when it deepens an
    /// existing drain of the same attribute; the others speak only when they first take hold.
    /// Donor: <c>DrainEffect.ShowPlayerDrained</c>, <c>Paralyze</c>, <c>Silence</c>, <c>Slowfall</c>,
    /// <c>Regenerate.Start</c> and <c>ConcealmentEffect.StartConcealment</c>.
    /// </summary>
    internal static string? StartMessage(DaggerfallEffectOutcome effect)
    {
        string key = effect.EffectKey;
        if (key.StartsWith("drain-", StringComparison.Ordinal))
            return effect.Kind is DaggerfallEffectOutcomeKind.Started or DaggerfallEffectOutcomeKind.Refreshed ? "You feel drained." : null;
        if (effect.Kind != DaggerfallEffectOutcomeKind.Started) return null;
        return key switch
        {
            "paralyze" => "You are paralyzed.",
            "silence" => "You are silenced.",
            "slowfall" => "Slow fall active.",
            "regenerate" => "You are regenerating.",
            _ when key.StartsWith("invisibility-", StringComparison.Ordinal) => "You are invisible.",
            _ when key.StartsWith("chameleon-", StringComparison.Ordinal) => "You are blending.",
            _ when key.StartsWith("shadow-", StringComparison.Ordinal) => "You are a shade.",
            _ => null,
        };
    }

    internal void React(IProductFact fact)
    {
        switch (fact)
        {
            case AzurasStarCaptureFact star:
                presentation.AppendOutcome(star.Outcome switch
                {
                    DaggerfallStarCaptureOutcome.Captured => "Soul captured in Azura's Star.",
                    DaggerfallStarCaptureOutcome.Occupied => "Azura's Star is already full.",
                    DaggerfallStarCaptureOutcome.Ineligible => "Azura's Star cannot capture this soul.",
                    _ => "Azura's Star is unavailable.",
                });
                break;
            case SoulTrapResolvedFact trapped:
                _soulTrap = trapped;
                presentation.SetOutcome(trapped.Message);
                _lineIsResult = true;
                break;
            case SpellCastFact { Outcome: DaggerfallCastOutcome.DeliveryCompleted, Absorptions: not null } cast
                when cast.Absorptions.Any(value => value.TargetId == DaggerfallActorIdentity.PlayerEntityId):
                _lineIsResult = true;
                presentation.SetOutcome($"Spell absorbed; restored {cast.Absorptions.Where(value => value.TargetId == DaggerfallActorIdentity.PlayerEntityId).Sum(value => value.RestoredSpellPoints):0} magicka.");
                break;
            case SpellTrackRestoredFact restored when restored.TargetId == DaggerfallActorIdentity.PlayerEntityId:
                _lineIsResult = true;
                presentation.SetOutcome($"Restored {restored.Restored:0} {restored.Track}.");
                break;
            case VitalTransferredFact transferred when transferred.CasterId == DaggerfallActorIdentity.PlayerEntityId
                || transferred.TargetId == DaggerfallActorIdentity.PlayerEntityId:
                _lineIsResult = true;
                string transferLine = transferred.CasterId == DaggerfallActorIdentity.PlayerEntityId
                    ? $"Drained {transferred.ActualLoss:0} {transferred.Track} from {Name(transferred.TargetId)}; restored {transferred.ActualRecovery:0} {transferred.Track}."
                    : $"{Name(transferred.CasterId)} drained {transferred.ActualLoss:0} {transferred.Track} from you; restored {transferred.ActualRecovery:0} {transferred.Track}.";
                if (transferred.TargetDefeated) presentation.AppendOutcome(transferLine);
                else presentation.SetOutcome(transferLine);
                break;
            // The donor tells the player when certain effects take hold of them. A spell the player
            // cast is already reported by its cast line, so only effects from others are announced.
            case MagicEffectFact { Outcome: { TargetId: DaggerfallActorIdentity.PlayerEntityId } effect }
                when effect.CasterId != DaggerfallActorIdentity.PlayerEntityId && StartMessage(effect) is { } started:
                _lineIsResult = true;
                presentation.AppendOutcome(started);
                break;
            case SpellCastFact cast when cast.CasterId == DaggerfallActorIdentity.PlayerEntityId:
                _lineIsResult = true;
                presentation.SetOutcome(cast.Outcome == DaggerfallCastOutcome.DeliveryCompleted
                    ? $"{cast.SpellName ?? cast.SpellKey}: {string.Join(", ", cast.Effects.Select(effect => effect.Outcome).Distinct())}."
                    : $"{cast.SpellName ?? cast.SpellKey ?? "Spell"}: {cast.Outcome}.");
                break;
            case ArtifactResourceTransferredFact transferred:
                _lineIsResult = true;
                presentation.SetOutcome(transferred.Magicka > 0
                    ? $"Mace of Molag Bal transferred {transferred.Magicka:0} magicka."
                    : $"Mace of Molag Bal transferred {transferred.Strength} Strength.");
                break;
            case ActorTransformedFact changed:
                _lineIsResult = true;
                presentation.SetOutcome(changed.Outcome switch
                {
                    DaggerfallWabbajackOutcome.Transformed => $"Wabbajack transformed the target into {changed.ReplacementDefinition}",
                    DaggerfallWabbajackOutcome.ProtectedQuestTarget => "Wabbajack cannot transform a quest target",
                    DaggerfallWabbajackOutcome.AlreadyTransformed => "This target is already transformed",
                    DaggerfallWabbajackOutcome.SourceUnavailable => "The Wabbajack source is no longer available",
                    DaggerfallWabbajackOutcome.UnavailableAppearance => "This site cannot display the Wabbajack replacement",
                    _ => "Wabbajack cannot transform this target",
                });
                break;
            case AttackRejectedFact rejected:
                // A rejection is about an action that did not happen, and a held attack button produces
                // one every update. It therefore yields to a result that is still fresh: the line the
                // player needs to read is what happened, not that the same denied swing was denied
                // again sixteen milliseconds later.
                if (_lineIsResult && presentation.LastOutcome.Length != 0) break;
                _lineIsResult = false;
                presentation.SetOutcome(rejected.Reason switch
                {
                    AttackRejection.MissingPlayerPosition => "No authored player position",
                    // What the perception query compared is diagnostics (the playtest targets readout
                    // carries the last melee query's counts); the player reads only the result.
                    AttackRejection.NoTargetInReach => "Nothing in reach.",
                    AttackRejection.Cooldown => "Cooldown",
                    AttackRejection.AttackInProgress => "Attack in progress",
                    AttackRejection.InsufficientStamina => "Too exhausted to attack",
                    AttackRejection.InsufficientWeaponMaterial => "Weapon material cannot harm this target",
                    AttackRejection.TargetDefeated => "Target already defeated",
                    // An actor that reached its swing with no authored policy is a missing capability,
                    // not a silent miss, so the line says which actor and what is missing.
                    AttackRejection.NoAttackPolicy => rejected.ActorId is { } attacker ? $"No authored attack for {Name(attacker)}" : "No authored attack policy",
                    // An empty quiver is refused and named: the shooter stops shooting rather
                    // than the player reading the refusal as a missed shot.
                    AttackRejection.EmptyQuiver => rejected.ActorId is { } shooter ? $"{Name(shooter)} has no arrows" : "Out of arrows",
                    _ => "Melee request rejected",
                });
                break;
            // The line names whoever the player needs to know about. A swing the player made names the
            // actor it landed on; a swing that landed on the player names the attacker, because "Hit
            // player for 5 damage" tells the player nothing they did not already know and nothing about
            // which of the enemies in front of them is doing it.
            // A shot that met cover is not a miss: nothing about the roll or the target decided it, and
            // the player needs to know the world was in the way.
            case RangedShotBlockedFact blocked:
                _lineIsResult = true;
                presentation.SetOutcome(blocked.AttackerId == DaggerfallActorIdentity.PlayerEntityId
                    ? "Shot blocked by cover"
                    : $"{Name(blocked.AttackerId)}'s shot is blocked by cover");
                break;
            // The donor names no roll or chance for a miss; those stay on the fact for diagnostics.
            case AttackMissedFact missed when Actor(missed.EnemyAttack ? missed.AttackerId : missed.TargetId, out DaggerfallActorDefinition definition):
                _lineIsResult = true;
                presentation.SetOutcome(missed.EnemyAttack && missed.TargetId != DaggerfallActorIdentity.PlayerEntityId
                    ? $"{Name(missed.AttackerId)} missed {Name(missed.TargetId)}"
                    : missed.EnemyAttack
                    ? $"{definition.Id.Value} missed you"
                    : $"Missed {definition.Id.Value}");
                break;
            case AttackHitFact hit when Actor(hit.EnemyAttack ? hit.AttackerId : hit.TargetId, out DaggerfallActorDefinition definition):
                _lineIsResult = true;
                presentation.SetOutcome(hit.EnemyAttack && hit.TargetId != DaggerfallActorIdentity.PlayerEntityId
                    ? $"{Name(hit.AttackerId)} hit {Name(hit.TargetId)} for {DaggerfallFormulaPolicy.DisplayDamage(hit.ActualHealthLost)} damage"
                    : hit.EnemyAttack ? $"{definition.Id.Value} hit you for {DaggerfallFormulaPolicy.DisplayDamage(hit.ActualHealthLost)} damage" : $"Hit {definition.Id.Value} for {DaggerfallFormulaPolicy.DisplayDamage(hit.ActualHealthLost)} damage");
                if (_soulTrap is { AllowsDeath: false } tethered && tethered.TargetId == hit.TargetId)
                { presentation.AppendOutcome(tethered.Message); _soulTrap = null; }
                break;
                // Soul resolution was completed before the canonical health write.
            case ActorDiedFact died when Actor(died.ActorId, out DaggerfallActorDefinition definition):
                _lineIsResult = true;
                presentation.SetOutcome($"Defeated {Name(died.ActorId)} for {DaggerfallFormulaPolicy.DisplayDamage(died.ActualHealthLost)} damage; gained {definition.Rewards.ExperienceReward} XP");
                if (_soulTrap is { } deathTrap && deathTrap.TargetId == died.ActorId)
                { presentation.AppendOutcome(deathTrap.Message); _soulTrap = null; }
                break;
            case LootAwardedFact loot:
                presentation.AppendOutcome($"looted {loot.Quantity} {loot.ItemId}");
                break;
            // The donor reports only the break, never the condition loss that led to it, and names the
            // item rather than the blow. The wording and the plural set both come from owners: the
            // published donor message for the break's authored plurality, which the wear site resolved
            // from the item's native template. The clause appends to the hit that caused it, so the
            // player reads what the swing did and what it cost.
            case EquipmentWornFact { Broken: true } worn:
                presentation.AppendOutcome(BreakLine(worn));
                break;
            case CorpseSearchedEmptyFact:
                presentation.SetOutcome("Corpse is empty");
                break;
        }
    }

    private string Name(long entityId) => Actor(entityId, out DaggerfallActorDefinition definition) ? definition.Id.Value : $"actor {entityId}";

    /// <summary>
    /// The donor's break line: its published message for a singular or plural break, with the item
    /// substituted for the placeholder the message itself declares. A pack that carries no such message
    /// still reports the break in this line's own words rather than dropping what happened.
    /// </summary>
    private string BreakLine(EquipmentWornFact worn)
    {
        DaggerfallTextKey key = new(DaggerfallTextKind.Internal, worn.PluralBreak ? "itemHasBrokenPlural" : "itemHasBroken");
        if (text is null || text.Resolve(key, out DaggerfallTextValue? value) is not DaggerfallTextResolution.Resolved)
            return $"{worn.ItemId} {(worn.PluralBreak ? "have" : "has")} broken";
        return string.Concat(value!.TextRuns).Replace("%s", worn.ItemId, StringComparison.Ordinal);
    }

    private bool Actor(long entityId, out DaggerfallActorDefinition definition) => actors.TryGetValue(entityId, out definition!);
}
