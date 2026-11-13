using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The update order. <see cref="AdmittedUpdate"/> is every call one Engine-admitted update makes, in order;
/// <see cref="AdvanceCalendar"/> is every calendar consumer, in order, which rest, travel and quest time share
/// through <see cref="AdvanceElapsedTime"/> and <see cref="AdvanceQuestTime"/>; <see cref="SimulateStep"/> is every
/// call one of its fixed steps makes; and <see cref="ResolveExhaustion"/> is what a collapse does across domains. Each line says the domain it belongs to. Order is behaviour: a call
/// moves only with a reason that names what it changes.
/// </summary>
internal sealed partial class DaggerfallSession
{
    public ProductUpdateResult Update(ProductUpdate update)
    {
        // Slow work the player asks for anywhere in this update (an act deferred behind an enemy hit included)
        // is acknowledged now and staged on the loading screen; this update's own publication carries it.
        _inAdmittedUpdate = true;
        try
        {
            return AdmittedUpdate(update);
        }
        finally
        {
            _inAdmittedUpdate = false;
        }
    }

    /// <summary>
    /// One admitted update. An exception that escapes here faults the lifecycle; the Engine reports it with
    /// its full text and stack, and a resume continues this same session, so nothing is torn down on the way
    /// out. Session resources are released by Dispose.
    /// </summary>
    private ProductUpdateResult AdmittedUpdate(ProductUpdate update)
    {
        ProductUpdateFacts facts = update.Facts;
        ReadOnlySpan<ProductInputEvent> input = update.Input;
        foreach (var appearance in _sites.Appearances) appearance.BeginAdmittedUpdate();         // Scene
        _enemyBehavior.BeginAdmittedUpdate();                                                     // Actors

        // An update admits fixed steps only while the lifecycle runs; a held or paused one carries none.
        if (facts.LifecycleState == ProductLifecycleState.Running && double.IsFinite(facts.FixedDeltaSeconds)
            && facts.FixedDeltaSeconds > 0d && facts.FixedDeltaSeconds <= float.MaxValue)
        {
            float deltaSeconds = (float)facts.FixedDeltaSeconds;
            ProductUpdateState firstStep = new(deltaSeconds);
            // A mode that is not ordinary play interprets no gameplay input and advances no world
            // time. The modal's own semantic actions still apply, because a modal that cannot act is
            // not a modal; everything else - including the entry screen's own action, which the product
            // answers before this runs - is dropped and the presentation still publishes so the player
            // can see the mode they are in.
            _lastInputDevice = InputDevice(input, Math.Min(_tuning.ControllerInput.MovementDeadzone,
                _tuning.ControllerInput.LookDeadzone)) ?? _lastInputDevice;                     // UiProjection
            Cinematics?.Poll();                                                                   // Scene
            _openingCinematics.Poll();                                                            // Scene
            // The donor ends a playing video on any key or button press (DaggerfallVidPlayerWindow,
            // AnyKeyDownIgnoreAxisBinds). The shell draws a video above the product UI, so a press is
            // also the only way to skip that the player can reach while one plays.
            if ((_openingCinematics.IsActive || Cinematics?.ActiveSource is not null) && PressesAnyKey(input))
            {
                if (_openingCinematics.IsActive) _openingCinematics.Skip();                       // Scene
                else Cinematics?.Skip();                                                          // Scene
            }
            if (_mode == ProductMode.Playing) Infections.Poll(_openingCinematics.IsActive);       // Afflictions
            ReconcileSummoningFoes();                                                             // Magic
            // An arrival whose ground the Engine refused holds the world, like a held mode, until that ground is live
            // and the player stands on it: nothing would hold anyone up meanwhile.
            bool groundless = _mode == ProductMode.Playing && !_exterior.RecoverArrivalGround(); // Exterior
            bool playing = _mode == ProductMode.Playing && !groundless && !Summoning.PendingOffer && State.Quests.PendingOffer is null && !LegalModalOpen && Cinematics?.ActiveSource is null && _pendingDispel is null && _pendingIdentify is null && _pendingCreateItem is null;
            bool modal = _mode == ProductMode.Modal || Summoning.PendingOffer || State.Quests.PendingOffer is not null || LegalModalOpen || _pendingDispel is not null || _pendingIdentify is not null || _pendingCreateItem is not null;
            DaggerfallUiPhases phase = _mode == ProductMode.Dead ? DaggerfallUiPhases.Dead
                : playing ? DaggerfallUiPhases.Playing
                : modal ? DaggerfallUiPhases.Modal
                : DaggerfallUiPhases.Held;
            DaggerfallUiInput ui = TakeUiInput(input);
            // The slice that opens an interaction admits no attack. Whichever order the Engine delivers
            // the keys in, swinging on the frame a container opens is an unintended attack.
            bool opensInteraction = playing && OpensInteraction(ui);
            bool elapsedSubmitted = false;
            for (int index = 0; index < input.Length; index++)
            {
                firstStep.Add(input[index]);
                if (ui.IsUiAction(index))
                    AdmitUiAction(ui.ActionAt(index), phase, firstStep, opensInteraction, ref elapsedSubmitted);   // Session: each to its owner
            }

            if (elapsedSubmitted || !playing || _interactions.HoldsWorldOpen)
            {
                // Explicit elapsed time already passed through the one calendar and its consumers, so the same
                // input slice must not also apply an ordinary realtime step or attack. A held mode's own line was
                // set when the mode was entered; an outcome an action set during this update stays on the line
                // rather than being overwritten by it. Reactions to modal-own actions (a loot take above) deliver
                // here rather than waiting for a playing step: the batch is stable and reentrant appends wait
                // for the next one.
                if (elapsedSubmitted) _playerTime.ClearWorldAction();                             // Time
                _input.Neutralize();                                                              // Character
                _locomotion.Neutralize();                                                         // Character
                NeutralizeGuard();                                                                // Combat
                DeliverFacts();                                                                   // Session
                PublishPresentation();                                                            // UiProjection
            }
            else
            {
                ObservePlayerInput(input, (float)facts.HostElapsedSeconds, firstStep);            // Session: the input's list, below
                if (facts.AdmittedStepCount == 0)
                {
                    _camera.Update(State.PlayerControl);                                          // Character
                    ApplyIdleInterfaceActions();                                                  // UiProjection
                    DeliverFacts();                                                               // Session
                    PublishPresentation();                                                        // UiProjection
                }
                else
                {
                    // The message line ages with the world it reports on, so it is advanced here rather than
                    // while a mode holds the world still.
                    Presentation.Advance(deltaSeconds * facts.AdmittedStepCount);                 // UiProjection
                    // The worn set is recomputed before the round advances, so a payload that ticks with the clock
                    // reads the body the player is wearing now rather than the one the previous update saw.
                    State.ItemInstances.ReconcileResidentOwners();                                // Items
                    State.HeldEnchantments.Refresh();                                             // Magic
                    _itemCastTriggers.Refresh();                                                  // Magic
                    ReleasePendingBoundSouls();                                                   // Magic
                    // A standing panel request ages on the same admitted world time as everything else.
                    _interactions.AgePanelRequest(deltaSeconds * facts.AdmittedStepCount);       // Interiors
                    // The tutorial's next page waits on play, not on a menu or a held moment.
                    _tutorial.Age(deltaSeconds * facts.AdmittedStepCount);                        // Quests

                    // Ordering within this one admitted update is clock, calendar consumers (magic rounds among
                    // them), simulation, then locomotion minutes. A normal game minute is one magic round; a
                    // larger admitted interval uses the same lifecycle catch-up path as rest, travel, and prison,
                    // so no second effect timer can drift from the saved calendar.
                    DaggerfallCalendar calendarBefore = _time.Calendar;
                    double remainderBefore = _time.RemainderSeconds;
                    long MinuteAtStep(uint step) => DaggerfallWorldTime.MinuteAtAdmittedOffset(calendarBefore, remainderBefore,
                        deltaSeconds * step * _time.GameSecondsPerRealSecond);
                    _time.Advance(deltaSeconds * facts.AdmittedStepCount);                        // Time
                    (long minuteBefore, long minuteAfter) =
                        AdvanceCalendar(calendarBefore, DaggerfallCalendarAdvanceKind.OrdinaryPlay);   // Session: the calendar's list, below
                    AdvanceWorldEncounters(minuteBefore, minuteAfter);                            // Actors
                    CheckStandingLaw(minuteBefore, minuteAfter);                                  // Society
                    // One admitted update owns one input slice. Later catch-up steps derive only committed
                    // held keyboard/mapped-direction intent; direct axes, direct digital movement, pointer
                    // deltas, and semantic actions do not replay. Simulation and reactions run per step; the
                    // publication below happens once, not once per step.
                    for (uint step = 0; step < facts.AdmittedStepCount; step++)
                    {
                        SimulateStep(step == 0 ? firstStep : new ProductUpdateState(deltaSeconds), facts.Generation,
                            checked(facts.SimulationStep + step), MinuteAtStep(step), MinuteAtStep(checked(step + 1)));   // Session: the step's list
                        DeliverFacts();                                                           // Session
                    }
                    // Locomotion charges the update's minutes once its steps have recorded how they were spent.
                    // A training interval admitted inside a step has already settled its own minutes.
                    _locomotion.AdvanceCalendarMinutes(minuteBefore, minuteAfter, State.Actors.Player.Stats,
                        swimmingFatigueApplies: SwimmingFatigueApplies);                          // Character, Condition
                }
            }
        }

        // Sprite playback consumes the Engine-bound outer update identity. It must not run for each private
        // catch-up simulation step above, and it must not run while the world is holding still.
        if (_mode == ProductMode.Playing && !_interactions.HoldsWorldOpen
            && facts.LifecycleState == ProductLifecycleState.Running
            && facts.AdmittedStepCount > 0)
        {
            foreach (var appearance in _sites.Appearances) appearance.Advance(facts);            // Scene
            _appearance.AdvanceMobileFeedback(facts, State.Actors, State.PlayerControl.Position, ShotBlockedByCover);   // Scene
            // A held value is recomputed before anything this update can read it: impacts resolve, the
            // flight advances and presentation publishes against what the player is wearing now.
            _heldEnchantments.Refresh();                                                          // Magic
            ApplyAttackImpacts();                                                                 // Combat
            // Acts a step held behind an enemy hit on the player run once that hit's frame had its chance.
            ApplyDeferredPlayerActs();                                                            // Character
            UpdateRangedFlight(facts);                                                            // Combat
            // Every fatigue sink this update reached has now settled, so an emptied pool collapses once here.
            ResolveExhaustion();                                                                  // Condition
            PublishPresentation();                                                                // UiProjection
            // The score follows the world this admitted update settled: a site change or a change of day
            // is real once the step applied it, and the director is told once per admitted update.
            AdvanceMusic();                                                                       // Scene
        }
        UpdatePlayerTime(facts);                                                                  // Session: player time's hand-off, below
        // Play time counts the time the world runs in play; menus, the title and held moments do not.
        if (_mode == ProductMode.Playing && facts.LifecycleState == ProductLifecycleState.Running
            && double.IsFinite(facts.FixedDeltaSeconds) && facts.FixedDeltaSeconds > 0)
            _playSeconds += facts.FixedDeltaSeconds * facts.AdmittedStepCount;                    // Session
        bool ambientPlaying = _mode == ProductMode.Playing && !_interactions.HoldsWorldOpen && Cinematics?.ActiveSource is null
            && _pendingDispel is null && _pendingIdentify is null && _pendingCreateItem is null
            && facts.LifecycleState == ProductLifecycleState.Running
            && !PlayerTimeHeld
            && double.IsFinite(facts.FixedDeltaSeconds) && facts.FixedDeltaSeconds > 0;
        SyncWeatherContext(ambientPlaying ? facts.FixedDeltaSeconds * facts.AdmittedStepCount : 0, ambientPlaying);   // Exterior
        // A paused or held update reached no impact boundary, so acts it deferred are not performed.
        _deferredPlayerActs = null;                                                               // Character
        foreach (var appearance in _sites.Appearances) appearance.CompleteAdmittedUpdate();      // Scene
        return ProductUpdateResult.None;
    }

    /// <summary>
    /// The calendar's list: one calendar interval, already applied to the clock, delivered to its consumers in
    /// their one order. Ordinary play reaches it from <see cref="AdmittedUpdate"/>; rest, travel, prison and quest
    /// time reach it through <see cref="AdvanceElapsedTime"/> and <see cref="AdvanceQuestTime"/>. A quest step
    /// inside the fan-out (or inside ordinary play's simulation steps) can admit an interval of its own, such as training or a cure; that nested interval runs this fan-out
    /// itself and so reaches every consumer once. Consumers that count minutes or rounds therefore
    /// take this interval's own end point, captured before any nested interval, while consumers
    /// that compare against an absolute date read the live calendar and are idempotent.
    /// </summary>
    /// <param name="before">The calendar before the interval was applied.</param>
    /// <param name="kind">What admitted the interval; see <see cref="DaggerfallCalendarAdvanceKind"/>.</param>
    /// <returns>
    /// The interval's minutes, captured before any nested interval. Ordinary play goes on from here in its
    /// update's list (<see cref="AdmittedUpdate"/>): world encounters, standing law, the steps, then
    /// locomotion's minutes. Every other interval has settled locomotion's minutes here.
    /// </returns>
    private (long Before, long After) AdvanceCalendar(DaggerfallCalendar before, DaggerfallCalendarAdvanceKind kind,
        bool resting = false, bool idleFatigue = true)
    {
        bool ordinaryPlay = kind == DaggerfallCalendarAdvanceKind.OrdinaryPlay;
        // Paid temple blessings own an absolute calendar deadline rather than a generic magic-round
        // lifetime. Observe it before every other consumer so a boundary reached inside a minute is
        // visible immediately and rest/travel cannot outrun the blessing through catch-up limits.
        _ = DaggerfallTempleBlessingEffects.ExpireDue(State.Effects, _time);                      // Economy
        _locomotion.SetAthletics(
            PlayerSpecials.Athleticism,
            State.HeldEnchantments.Talents.Athleticism);                                          // Character
        RefreshPassiveMagery();                                                                   // Character
        DaggerfallCalendar after = _time.Calendar;
        long minuteBefore = before.MinuteNumber, minuteAfter = after.MinuteNumber;
        State.RegionalPrices.AdvanceToDay(_time.Calendar.DayNumber);                              // Economy
        _weather.Advance(_time.Calendar, (minuteAfter - minuteBefore) / 60d);                     // Exterior
        _sites.Projection.Lighting.UpdateAmbient(_time.Calendar, _tuning.Weather.Daylight(_time.Calendar, CurrentWeather));   // Scene
        _exterior.UpdateResidentCityLights();                                                     // Exterior
        State.Quests.AdvanceClocks(State.Variables, before, _time.Calendar);                      // Quests
        // Daily conditions and ordinary source-order operations observe the same admitted calendar
        // after rest, travel, prison, or another interval, including an interval with no clock expiry.
        if (kind is DaggerfallCalendarAdvanceKind.Elapsed or DaggerfallCalendarAdvanceKind.ElapsedDeferringSkills)
        {
            State.Quests.Advance(State.Variables, _time.Calendar);                                // Quests
            State.Quests.ReconcileFoeCommands();                                                  // Quests
        }
        _dialogue?.SynchronizeWorldState();                                                       // Dialogue
        _dialogue?.RefreshEligibility();                                                          // Dialogue
        if (kind is DaggerfallCalendarAdvanceKind.Elapsed or DaggerfallCalendarAdvanceKind.QuestAction)
        {
            State.SkillUses.RaiseSkills(_time.Calendar.ToAbsoluteSeconds());                      // Character
            State.LevelUps.BeginIfEligible();                                                     // Character
        }
        State.Social.AdvanceElapsedMinutes(minuteBefore, minuteAfter);                            // Society
        AdvanceLoans(before, after);                                                              // Economy
        _vampirism.AdvanceQuestOpportunities(before, after);                                      // Afflictions
        _lycanthropy.AdvanceQuestOpportunities(before, after);                                    // Afflictions
        StartDueCriminalInvitations();                                                            // Quests
        ExpireConjuredItems();                                                                    // Magic
        AdvanceRounds(before, after, ordinaryPlay, resting);                                      // Session: the round's list, below
        // An elapsed interval announces the holiday it ends on once, when it completes, rather than
        // every holiday its slices cross (CompleteTimeIncrease).
        if (ordinaryPlay) AnnounceHoliday();                                                      // Time
        if (!ordinaryPlay)
        {
            // Rest suppresses newly incurred idle loss, and training charges its own source fatigue
            // cost instead, while travel, prison, and other elapsed callers still settle every
            // calendar minute through this existing owner. Any accepted movement seconds carried
            // from the prior admitted update are settled before reset.
            _locomotion.AdvanceCalendarMinutes(minuteBefore, minuteAfter, State.Actors.Player.Stats,
                includeIdleFatigue: !resting && idleFatigue, swimmingFatigueApplies: SwimmingFatigueApplies);   // Character, Condition
        }
        return (minuteBefore, minuteAfter);
    }

    /// <summary>
    /// The elapsed interval's list: a rest, travel, prison or other ruleset-owned interval, passed through the one
    /// calendar. A caller resumes <see cref="DaggerfallCalendarAdvance.RemainingSeconds"/> after handling a
    /// consequence; only the portion the calendar accepted reaches the calendar's consumers. An owner that passes
    /// time is handed this as an input, bound to its own kind of interval (a <c>Func&lt;long, ...&gt;</c> the session
    /// builds where it composes that owner), and never calls the session for it.
    /// </summary>
    internal DaggerfallCalendarAdvance AdvanceElapsedTime(long gameSeconds,
        IReadOnlyList<(int Identity, long SecondsFromNow)>? consequences = null,
        bool deferSkillAdvancement = false, bool resting = false, bool idleFatigue = true,
        bool completeTimeIncrease = true)
    {
        DaggerfallCalendar calendarBefore = _time.Calendar;
        DaggerfallCalendarAdvance advance = _time.AdvanceInterval(gameSeconds, consequences ?? []);   // Time
        AdvanceCalendar(calendarBefore,
            deferSkillAdvancement ? DaggerfallCalendarAdvanceKind.ElapsedDeferringSkills : DaggerfallCalendarAdvanceKind.Elapsed,
            resting: resting, idleFatigue: idleFatigue);                                          // Session: the calendar's list
        // Elapsed rest/travel/service time can cross the donor outdoor population's dawn or dusk
        // boundary without an Engine admitted frame. Reconcile through the existing NPC/lifetime
        // owner so source civilians hide, retire, restore and preserve their durable identities.
        if (advance.AppliedSeconds > 0)
            ReconcileNpcProjection();                                                             // Actors
        // No admitted update refills Stamina through elapsed time, so the time itself does.
        if (advance.AppliedSeconds > 0) _condition.Stamina.Fill(State.Actors.Player.Stats);      // Condition
        // Rest and travel admit many slices and complete the time increase once, at their end.
        if (!resting && completeTimeIncrease && advance.AppliedSeconds > 0) CompleteTimeIncrease();   // Session: the increase's end, below
        return advance;
    }

    /// <summary>A quest-owned elapsed interval through the one calendar, without re-running quest tasks.</summary>
    private void AdvanceQuestTime(long gameSeconds, bool idleFatigue = true)
    {
        DaggerfallCalendar calendarBefore = _time.Calendar;
        var advance = _time.AdvanceInterval(gameSeconds, []);                                     // Time
        AdvanceCalendar(calendarBefore, DaggerfallCalendarAdvanceKind.QuestAction, idleFatigue: idleFatigue);   // Session: the calendar's list
        if (advance.AppliedSeconds > 0) CompleteTimeIncrease();                                   // Session: the increase's end, below
    }

    /// <summary>
    /// The end of one synthetic time increase (a rest, a journey, a service): held items reroll once and the day it
    /// ended on announces its holiday, as the donor shows only the current holiday's text.
    /// </summary>
    private void CompleteTimeIncrease()
    {
        _itemCastTriggers.CompleteTimeIncrease();                                                 // Magic
        AnnounceHoliday();                                                                        // Time
    }

    /// <summary>The holiday the calendar names where the player stands, announced once.</summary>
    private void AnnounceHoliday()
    {
        if (_holidays.Observe(_time.Calendar, _site.Region, _site.ActiveSite?.Kind,
                _definitions.TextPresentation) is { } line)                                       // Time
            Presentation.SetOutcome(line);                                                        // UiProjection
    }

    /// <summary>
    /// The round's list: the calendar minutes of one interval as magic rounds. A normal game minute of ordinary play
    /// is one ordinary round; several minutes, ordinary or elapsed, keep the donor's bounded catch-up inside each
    /// owner.
    /// </summary>
    private void AdvanceRounds(DaggerfallCalendar before, DaggerfallCalendar after, bool ordinaryPlay, bool resting)
    {
        long minuteBefore = before.MinuteNumber;
        long minutes = after.MinuteNumber - minuteBefore;
        if (minutes <= 0) return;

        long roundBefore = State.Effects.MagicRounds;
        AdvancePassiveRounds(roundBefore, minutes);                                               // Character
        _lycanthropy.AdvanceRound(after);                                                         // Afflictions
        _lycanthropy.Refresh();                                                                   // Afflictions
        if (ordinaryPlay && minutes == 1)
        {
            State.Effects.AdvanceOrdinaryRound();                                                 // Magic
            State.HeldEnchantments.AdvanceRounds(1);                                              // Magic
            _itemCastTriggers.AdvanceRounds(1, synthetic: false, roundBefore: roundBefore);       // Magic
            return;
        }

        _ = State.Effects.AdvanceElapsedRounds(minutes);                                          // Magic
        State.HeldEnchantments.AdvanceRounds(checked((int)Math.Min(minutes, int.MaxValue)), synthetic: !ordinaryPlay && !resting);   // Magic
        _itemCastTriggers.AdvanceRounds(minutes, synthetic: !ordinaryPlay && !resting, resting: resting, roundBefore: roundBefore);   // Magic
    }

    /// <summary>
    /// One simulation step: input, world time, and reactions. Publication is the caller's:
    /// the admitted update publishes once after all its steps, and direct callers publish
    /// with the step.
    /// </summary>
    private void SimulateStep(ProductUpdateState update, ulong generation, ulong simulationStep,
        long? swimmingMinuteBefore = null, long? swimmingMinuteAfter = null)
    {
        if (LegalModalOpen) return;
        _playerTime.ClearWorldAction();                                                           // Time
        _latestUpdateGeneration = generation;                                                     // Session
        _latestSimulationStep = simulationStep;                                                   // Session
        if (State.DungeonActions.TryGetValue(_activeProfileKey, out DaggerfallDungeonActionGraph? actionGraph))
            actionGraph.Advance(update.DeltaSeconds);                                             // Interiors
        DaggerfallMolagBalEffects.Reconcile(State.Effects, MolagBalEquipped);                     // Magic
        _combat.AdvanceEquipment(update.DeltaSeconds);                                            // Combat
        State.Kit.AttackExecution.ObserveTimeline(generation, simulationStep);                    // Combat

        // The player's movement, with the doors, gates, triggers and terrain it meets at the point it meets them.
        PlayerStep player = ApplyPlayerControls(update);                                          // Character
        State.Transport.Reconcile(State.Inventory.Read(), new DaggerfallTransportAccessContext(
            IsIndoor: _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior,
            IsDungeon: _activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon));            // Travel
        BeginLocomotion(ref player, update);                                                      // Character
        _doors.Advance(update.DeltaSeconds);                                                      // Interiors
        _sites.Projection.AdvanceMotion(update.DeltaSeconds);                                     // Exterior
        _exterior.SyncCityGates();                                                                // Exterior
        player.Environment = _exterior.CharacterEnvironment(State.PlayerControl.Motion);         // Exterior
        ReconcileTriggers(player.Environment, simulationStep, actionGraph);                       // Character
        ShapeLocomotion(ref player, update, generation, simulationStep);                          // Character: water, climbing, levitation
        player.Locomotion = Ride(player.Locomotion, update, player.ActiveWater is not null);      // Travel
        ConstrainLocomotion(ref player);                                                          // Character: control restrictions, slowfall
        player.Movement = _spatial.Step(State.PlayerControl, update, player.Environment, player.Locomotion.Controls);   // Character
        if (player.Movement is { } ridden)
            State.Transport.Ride.Moved(ridden.Displacement, update.DeltaSeconds, ridden.Motion.Grounded);   // Travel
        if (player.Movement is not null && _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior)
        {
            _exterior.HoldPlayerInsideWorldEdge();                                                // Exterior
            _exterior.FollowPlayerOnTheRoad();                                                    // Exterior
            // A player who has dropped beneath the terrain is stood back on it rather than falling for ever.
            _ = _exterior.RecoverPlayerBelowTerrain();                                            // Exterior
            // A terrain cell can remain resident while the authored location closure is unloaded.
            // Its live wilderness actors still use the admitted terrain and actor owners.
        }
        if (player.Movement is not null)
            ReconcileTriggers(player.Environment, simulationStep, actionGraph);                   // Character
        if (player.Movement is not null && State.DungeonDiscoveries.TryGetValue(_activeProfileKey, out DaggerfallDungeonDiscovery? discovery))
            _dungeonVisibility.Observe(discovery, State.PlayerControl, _doors, player.Environment, simulationStep,
                _tuning.Camera.EyeHeight, _exterior.LocalCompensation);                           // Interiors
        CompleteLocomotion(ref player, update, swimmingMinuteBefore, swimmingMinuteAfter);        // Character: swimming, landing, climbing
        if (player.Movement is { } acceptedMovement && player.PositionBefore is { } previousPosition)
            _appearance.AdvanceFootsteps(previousPosition, acceptedMovement.Transform.Translation,
                player.MotionBefore.Grounded, State.PlayerControl.Motion.Grounded, player.WasSwimming, player.Swimming.Swimming,
                player.ActiveWater is not null && !player.Swimming.Swimming,
                State.Transport.IsOnFoot && !player.Climb.Climbing && !player.Levitation.IsLevitating,
                _time.Calendar.Season == DaggerfallSeason.Winter && CurrentClimate is not (224 or 225 or 227 or 229),
                State.Transport.Mode);                                                            // Scene
        if (player.Landing is { } landed && DaggerfallFormulaPolicy.IsHardLanding(landed.Distance) && !player.Swimming.Swimming && !player.SlowfallDescending)
            _appearance.PlayGameplaySound(92, "hard-landing");                                    // Scene
        if (player.Swimming.Drowning)
            AppendDamage(_vitality.ResolveDrowning(State.Actors.Player.Actor), DaggerfallDamageCause.Drowning, 0);   // Condition
        if (_vitality.ResolveLanding(State.Actors.Player.Actor, player.Landing, State.Effects.PreventsFallDamage(DaggerfallActorIdentity.PlayerEntityId)) is { } fall)
            AppendDamage(fall, DaggerfallDamageCause.Fall, 0);                                    // Condition
        // Trigger geometry is the row each trigger last reconciled with. Actor navigation asks those rows
        // whether an actor stands in water, so after the origin moves they are reconciled again in the new
        // frame before any actor steps. The player's place relative to the water is unchanged by it.
        if (_exterior.RebaseExteriorIfNeeded())                                                   // Exterior
            ReconcileTriggers(_exterior.CharacterEnvironment(State.PlayerControl.Motion), simulationStep, actionGraph);   // Character
        if (player.Movement is not null && player.Alive && _sites.ActivateLocationAtPlayer())    // Interiors: the crossing
            ReconcileTriggers(_exterior.CharacterEnvironment(State.PlayerControl.Motion), simulationStep, null);   // Character
        _camera.SettlePunch(update.DeltaSeconds, _tuning.Attacks.Strike.PunchSettleRate);        // Combat
        if (_hitStopSteps > 0) _hitStopSteps--;                                                   // Combat
        _camera.Update(State.PlayerControl, _appearance.MountEyeLift(_tuning.Camera.EyeHeight)); // Character

        // An unloaded location has already destroyed its own actors (roster UnloadSite), so the
        // steps below see only live actors: wilderness spawns and resident neighbouring locations.
        // Location-scoped consumers (law, civilians, NPC projection, quest placements) keep their
        // own loaded-location guards.
        if (!player.Alive || State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= 0d) return;
        _ = _encounters.MaterializePending(_activeProfileKey.LogicalId, pending => PlaceEncounter(pending, simulationStep));   // Actors
        UpdateLaw(update.DeltaSeconds);                                                           // Society
        if (LegalModalOpen) return;
        _reactions.Step(State.Actors, update.DeltaSeconds, ThrownLanding);                       // Actors
        StepHazards(update.DeltaSeconds);                                                         // Combat
        _enemyBehavior.Update(State.PlayerControl, generation, simulationStep, update.DeltaSeconds, _facts);   // Actors
        // Enemy attack-start facts must reach presentation before the post-enemy actions below.
        // A hit marker is consumed by the outer admitted update after this simulation step; if
        // that swing is already in flight, keep input and progression behind its same boundary.
        // Immediate (no damage-frame) swings are resolved here so a lethal result also stops the
        // remainder of this step. DeliverFacts remains a stable-batch boundary; any facts emitted
        // by these reactions wait for the existing caller boundary.
        DeliverFacts();                                                                           // Session
        ApplyAttackImpacts();                                                                     // Combat
        // Enemy reactions resolve inside this admitted step. A lethal reaction owns the rest of
        // the step: do not toggle equipment, attack, activate a target, or advance
        // quests after the player has been defeated. DeliverFacts still runs at the caller boundary,
        // so the mode transition and death presentation are published from the committed outcome.
        if (State.Actors.Player.IsDefeated
            || State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= 0d)
            return;
        PerformOrDeferPlayerActs(update);                                                         // Character
        StepPlayerAttack(update.DeltaSeconds, generation, simulationStep);                        // Combat
        // The guard follows the step's acts, so a swing begun this step keeps the guard down.
        StepGuard(update.DeltaSeconds, generation, simulationStep);                               // Combat
        _condition.Stamina.Step(State.Actors.Player.Stats, update.DeltaSeconds);                  // Condition
        // Tasks consume the state committed by this admitted step. Clock actions mutate only the
        // quest clock state; elapsed duration is still consumed by the calendar owner.
        State.Quests.Advance(State.Variables, _time.Calendar, update.DeltaSeconds);               // Quests
        _dialogue?.SynchronizeWorldState();                                                       // Dialogue
        if (_exterior.ActiveLocationLoaded) State.Quests.AdmitPlacements(_sites.Projection.Inputs, this);   // Quests
        State.Quests.ReconcileFoeCommands();                                                      // Quests
        ReconcileNpcProjection();                                                                 // Actors
        UpdateCivilianPopulation(simulationStep, update.DeltaSeconds);                            // Actors
        _dialogue?.RefreshEligibility();                                                          // Dialogue
    }

    /// <summary>
    /// One step outside an admitted update, for callers that drive the session step by step. It has no
    /// animation boundary of its own, so its end is that boundary.
    /// </summary>
    internal void Update(ProductUpdateState update)
    {
        _enemyBehavior.BeginAdmittedUpdate();                                                     // Actors
        ObservePlayerInput(update.Inputs.ToArray(), update.DeltaSeconds, update);                 // Session: the input's list, below
        SimulateStep(update, 0, 0);                                                               // Session: the step's list
        DeliverFacts();                                                                           // Session
        ApplyDeferredPlayerActs();                                                                // Character
        ResolveExhaustion();                                                                      // Condition
        PublishPresentation();                                                                    // UiProjection
        AdvanceMusic();                                                                           // Scene
    }

    /// <summary>
    /// The collapse's list: what an emptied Fatigue pool does, in order, once every fatigue sink of the admitted
    /// update has settled. Condition decides whether the player collapses and how the collapse ends; the other
    /// lines are its consequences in their own domains. Out of water with no enemy near, the player drops for one
    /// hour, recovers one rest hour and tallies Medical; in water or near an enemy the collapse is fatal. Either
    /// outcome leaves the pool non-empty or the player dead, so a pool that stays empty never collapses twice.
    /// </summary>
    internal void ResolveExhaustion()
    {
        if (_mode != ProductMode.Playing || State.Actors.Player.IsDefeated
            || !_condition.Exhausted(State.Actors.Player.Stats)) return;                         // Condition
        // The donor cancels movement as the player drops.
        _input.Neutralize();                                                                      // Character
        _locomotion.Neutralize();                                                                 // Character
        NeutralizeGuard();                                                                        // Combat
        State.Transport.Ride.Reset();                                                             // Travel
        bool inWater = State.Swimming.IsSwimming;
        bool enemiesNearby = HasNearbyRestEnemy();                                                // Actors
        if (inWater || enemiesNearby)
        {
            var (death, line) =
                _condition.CollapseFatally(State.Actors.Player.Actor, inWater, _definitions.TextPresentation);   // Condition
            AppendDamage(death, DaggerfallDamageCause.Exhaustion, 0);                             // Session
            DeliverFacts();                                                                       // Session
            // The collapse text stands over the death's own damage line.
            Presentation.SetOutcome(line);                                                        // UiProjection
            return;
        }
        _ = AdvanceElapsedTime(DaggerfallRestPolicy.SecondsPerRestHour, idleFatigue: false);      // Time
        // A consequence of the hour itself (a poison round, a quest) may have ended the player.
        DeliverFacts();                                                                           // Session
        if (State.Actors.Player.IsDefeated) return;
        (bool rapidHealing, bool noRegeneration) = _rest.CharacterTraits(PlayerSpecials, _activeProfileKey.Kind);   // Time
        if (!_condition.RecoverFromCollapse(State.Actors.Player.Stats, rapidHealing, noRegeneration)) return;   // Condition
        State.SkillUses.Record(new DaggerfallSkillUse("medical", DaggerfallSkillUseReason.MedicalRest,
            DaggerfallSkillUseOutcome.Accepted));                                                 // Character
        DeliverFacts();                                                                           // Session
        Presentation.SetOutcome(_condition.AwakeningLine(_definitions.TextPresentation));         // UiProjection
    }

    /// <summary>
    /// The vampire's transformation list: what a ready vampire infection's permanent change does across domains, once
    /// its cinematic has played. Any refusal leaves the infection intact and retryable, as the infection owner asks.
    /// The player wakes in a cemetery of the region with the hostiles banished, fourteen days later after dusk, a
    /// vampire of the infection region's clan, and is told so.
    /// </summary>
    private DaggerfallInfectionConsumption BecomeVampire(DaggerfallInfectionTransition transition)
    {
        // The infection region remains the clan input even if the player travelled while incubating the disease.
        if (!_vampirism.TryClan(transition.InfectionRegion, out int clan, out string? unavailable))
            return new(false, unavailable);                                                       // Afflictions
        if (_sites.AwakenInCemetery(transition.InfectionRegion,
                count => _vampirism.CemeteryIndex(transition.Instance, count)) is { } refused)
            return new(false, refused);                                                           // Interiors
        _roster.BanishHostiles();                                                                 // Actors
        // Synthetic time uses the existing calendar fan-out without an encounter request, and like the
        // donor's raised clock it charges no per-minute fatigue loss.
        AdvanceQuestTime(_vampirism.AwakeningSeconds, idleFatigue: false);                        // Time
        if (!_vampirism.Become(transition, clan)) return new(false, "A racial override is already active.");   // Afflictions
        Presentation.SetOutcome(_definitions.TextPresentation.Resolve(
            new(DaggerfallTextKind.Resource, "401"), DungeonTextContext()).Text);                 // Text, UiProjection
        return new(true, null);
    }

    /// <summary>
    /// The rest's list: one rest choice, applied by the rest owner through <paramref name="advanceTime"/>, then what
    /// it ends in other domains. A caller must not pass time separately before it.
    /// </summary>
    internal DaggerfallRestResult ApplyRest(DaggerfallRestRequest request, DaggerfallRestEligibility eligibility,
        Func<long, DaggerfallRestTimeAdvance> advanceTime, bool rapidHealing = false, bool noRegeneration = false)
    {
        DaggerfallRestResult result = _rest.Apply(request, eligibility, State.Actors.Player.Stats, this, advanceTime,
            rapidHealing, noRegeneration, () => _rest.CharacterTraits(PlayerSpecials, _activeProfileKey.Kind));   // Time
        if (result.Accepted && result.ElapsedSeconds > 0) CompleteTimeIncrease();                // Session: the increase's end
        _restPresentation.Publish(result);                                                        // UiProjection
        Presentation.SetOutcome(result.Message ?? (result.Accepted ? "Rest complete." : "Rest refused."));   // UiProjection
        return result;
    }

    /// <summary>The input's list: one update's input slice, observed by each owner that reads it.</summary>
    private void ObservePlayerInput(ReadOnlySpan<ProductInputEvent> input, float lookSeconds, ProductUpdateState firstStep)
    {
        _input.Observe(State.PlayerControl, input, lookSeconds, firstStep.PlanarIntent);         // Character
        foreach (var action in firstStep.RequestedActions) _input.Request(action);               // Character
        _locomotion.Observe(input);                                                               // Character
        _guard.Observe(input, _controls.PadBindings);                                             // Combat
        _attackClock.Observe(input, _controls.PadBindings);                                       // Combat
    }

    /// <summary>
    /// Player time's hand-off: what the world needs time for this update, read from the domain that knows each part,
    /// for Time's owner to select the world's rate from.
    /// </summary>
    private void UpdatePlayerTime(ProductUpdateFacts facts)
    {
        bool presentation = _mode == ProductMode.Title || _openingCinematics.IsActive || Cinematics?.ActiveSource is not null;
        bool playing = _mode == ProductMode.Playing && !_interactions.HoldsWorldOpen
            && !State.Actors.Player.IsDefeated && !LegalModalOpen && !Summoning.PendingOffer
            && State.Quests.PendingOffer is null && _pendingDispel is null
            && _pendingIdentify is null && _pendingCreateItem is null;
        CharacterMotion motion = State.PlayerControl.Motion;
        // Slowfall uses Engine vertical drive too, but it is still passive descent. Only
        // the ruleset's actual hover/support modes let an idle airborne player hold time.
        bool falling = !motion.Grounded && !State.Effects.GrantsLevitation(DaggerfallActorIdentity.PlayerEntityId)
            && !State.Swimming.IsSwimming && !_climbing.IsAttached;
        // Ignore numerical controller residue smaller than a centimetre per second.
        bool movement = _input.ObservedPlanarIntent != Vector2.Zero || _locomotion.HasPendingJump
            || _locomotion.ObservedInput.JumpHeld
            || (_locomotion.ObservedInput.CrouchHeld && (_spatial.IsVerticalDriven || State.Swimming.IsSwimming))
            || _locomotion.HasPendingStanceChange
            // Ground adhesion retains downward controller velocity while standing. Only planar
            // momentum needs settling here; airborne gravity is covered by falling above.
            || falling || new Vector2(motion.ControlledVelocity.X, motion.ControlledVelocity.Z).LengthSquared() > .0001f
            || motion.ExternalVelocity.LengthSquared() > .0001f;
        bool action = _playerTime.WorldActionPending || _input.HasPendingActions || _guard.WantsTime || _attackClock.Busy || !_combat.PlayerWeaponReady
            || State.Actors.Player.Attack.ReadyAtStep > facts.SimulationStep
            || _combat.HasPlayerRangedFlight
            || Casting.PendingRangedFlights.Any(flight => flight.CasterId == DaggerfallActorIdentity.PlayerEntityId)
            || _doors.AnyMoving
            || _sites.Projection.Motion.IsMoving;
        if (_playerTime.Update(ActionDrivenPlay, new(presentation, playing, movement, action),
                _hitStopSteps > 0 ? _tuning.Attacks.Strike.HitStopRate : 1d))                    // Time
            PublishPresentation();                                                                // UiProjection
    }

    /// <summary>Condition's swimming roll for the player as the session holds them now.</summary>
    private bool SwimmingFatigueApplies(long minute) =>
        _condition.SwimmingFatigueApplies(State.Character.Race.Id, State.Actors.Player.Stats, minute);
}
