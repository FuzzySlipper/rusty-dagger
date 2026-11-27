using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Owns residential interior night routines (#11139): lights out during home sleep hours (22:00-06:00),
/// placing residents in bed spots (without dirtying <see cref="DaggerfallActorLedger"/> or writing save bytes
/// for untouched residents), and waking residents with lights-on, facing, bark, and trespassing crime
/// reporting when the player is caught inside a non-owned residence at night.
/// </summary>
internal sealed class DaggerfallHomeNightRoutine(
    ActorsState actors,
    DaggerfallNpcRegistry npcs,
    PlayerControlState player,
    DaggerfallCrimes crimes,
    Func<DaggerfallInteriorBuilding, bool> ownsInterior,
    Func<bool> isPlayerConcealed,
    Func<bool> hasPlayerLight,
    Action<string> reportOutcome)
{
    internal const int SleepStartHour = 22;
    internal const int SleepEndHour = 6;
    internal const float StandingDetectionRadiusMeters = 7.5F;
    internal const float RunningOrLitDetectionRadiusMeters = 10.5F;
    internal const float MaxSneakDetectionRadiusMeters = 4.0F;
    internal const float MinSneakDetectionRadiusMeters = 1.8F;
    internal const float SameFloorToleranceMeters = 2.6F;

    private readonly HashSet<long> _awakenedActors = [];
    private readonly Dictionary<long, DaggerfallBedSpot> _bedAssignments = [];
    private DaggerfallWorldProfileKey? _activeProfile;
    private bool _lightsOut;
    private bool _intruderDetected;
    private bool _intruderReported;

    internal static bool IsHomeSleepHour(int hour) => hour >= SleepStartHour || hour < SleepEndHour;

    /// <summary>Whether the active residential interior currently has its night lights-out mode active.</summary>
    internal bool LightsOut => _lightsOut;

    /// <summary>Whether an intruder has woken the residents of the active non-owned residence this night.</summary>
    internal bool IntruderDetected => _intruderDetected;

    /// <summary>Whether the waking residents have already reported a trespassing crime for this intrusion.</summary>
    internal bool IntruderReported => _intruderReported;

    /// <summary>Durable IDs of residents awakened by an intruder in the active residence.</summary>
    internal IReadOnlyCollection<long> AwakenedActorIds => _awakenedActors;

    /// <summary>Transient bed-spot assignments for sleeping residents in the active residence.</summary>
    internal IReadOnlyDictionary<long, DaggerfallBedSpot> BedAssignments => _bedAssignments;

    /// <summary>Clears transient residential night-routine state (e.g., on leaving an interior).</summary>
    internal void Reset()
    {
        foreach (ActorState actor in actors.All)
        {
            if (actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
            {
                overlay.IsSleeping = false;
                overlay.SleepingOnBed = false;
                overlay.AwakenedByIntruder = false;
                overlay.PreSleepPosition = null;
                overlay.PreSleepHeading = null;
            }
        }
        _awakenedActors.Clear();
        _bedAssignments.Clear();
        _activeProfile = null;
        _lightsOut = false;
        _intruderDetected = false;
        _intruderReported = false;
    }

    /// <summary>
    /// Reconciles residential lights-out and resident bed sleep states for <paramref name="profile"/>
    /// at the current <paramref name="sky"/> hour. Never mutates <see cref="DaggerfallActorLedger"/>.
    /// </summary>
    internal void Reconcile(
        DaggerfallSiteProfile profile,
        DaggerfallSiteLighting lighting,
        DaggerfallSiteAppearance appearance,
        DaggerfallSkyTime sky,
        bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(lighting);
        ArgumentNullException.ThrowIfNull(appearance);

        if (!IsResidenceInterior(profile, out DaggerfallInteriorBuilding? building))
        {
            if (_lightsOut || _activeProfile is not null)
                Reset();
            lighting.SetInteriorLightsOut(false, appearance, sky);
            return;
        }

        if (_activeProfile != profile.ProfileKey)
        {
            _awakenedActors.Clear();
            _bedAssignments.Clear();
            _activeProfile = profile.ProfileKey;
            _intruderDetected = false;
            _intruderReported = false;
        }

        bool sleepTime = IsHomeSleepHour(sky.Hour);
        List<ActorState> residents = CollectResidents();

        if (!sleepTime)
        {
            WakeAllForMorning(residents, lighting, appearance, sky);
            return;
        }

        if (_intruderDetected && !immediate)
        {
            _lightsOut = false;
            lighting.SetInteriorLightsOut(false, appearance, sky);
            return;
        }

        if (immediate)
        {
            _intruderDetected = false;
            _intruderReported = false;
            _awakenedActors.Clear();
        }

        // Residential sleep hours: lights out and residents asleep in bed spots.
        bool playerOwns = ownsInterior(building!);
        bool hasSleepers = residents.Count > 0;
        _lightsOut = hasSleepers || !playerOwns;
        lighting.SetInteriorLightsOut(_lightsOut, appearance, sky);

        if (!hasSleepers)
            return;

        IReadOnlyList<DaggerfallBedSpot> bedSpots =
            DaggerfallScheduleShelterResolver.ResolveInteriorBedSpots(profile, residents.Count);

        for (int i = 0; i < residents.Count; i++)
        {
            ActorState resident = residents[i];
            if (!resident.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
                continue;
            if (_awakenedActors.Contains(resident.DurableId))
                continue;

            if (!_bedAssignments.TryGetValue(resident.DurableId, out DaggerfallBedSpot spot))
            {
                if (i >= bedSpots.Count)
                    continue;
                spot = bedSpots[i];
                _bedAssignments[resident.DurableId] = spot;
            }

            if (!overlay.IsSleeping)
            {
                overlay.PreSleepPosition ??= resident.Position;
                overlay.PreSleepHeading ??= resident.HeadingYawRadians;
                resident.ApplyPose(new ActorPose(spot.Position, spot.HeadingRadians));
                overlay.IsSleeping = true;
                overlay.SleepingOnBed = spot.IsRealBedMesh;
                overlay.AwakenedByIntruder = false;
            }
        }
    }

    /// <summary>
    /// Steps residential night routine during play: keeps lighting/sleep synced with the clock and
    /// checks whether the player has been caught inside a non-owned residence while residents sleep.
    /// </summary>
    internal void Step(
        DaggerfallSiteProfile profile,
        DaggerfallSiteLighting lighting,
        DaggerfallSiteAppearance appearance,
        DaggerfallSkyTime sky,
        float deltaSeconds)
    {
        if (deltaSeconds < 0f) return;
        if (!IsResidenceInterior(profile, out DaggerfallInteriorBuilding? building))
        {
            if (_lightsOut || _activeProfile is not null)
                Reset();
            return;
        }

        if (_activeProfile != profile.ProfileKey || (!IsHomeSleepHour(sky.Hour) && (_lightsOut || _bedAssignments.Count > 0)))
            Reconcile(profile, lighting, appearance, sky, immediate: false);

        if (!IsHomeSleepHour(sky.Hour) || _intruderDetected || ownsInterior(building!))
            return;
        if (player.Position is not WorldPoint playerPos)
            return;

        ActorState? detector = FindDetectingSleeper(playerPos);
        if (detector is not null)
            TriggerIntruderWake(detector, profile, lighting, appearance, sky, playerPos);
    }

    /// <summary>
    /// Wakes a sleeping resident when the player directly talks to or disturbs them.
    /// In a non-owned residence during sleep hours, triggers the full intruder wake response.
    /// </summary>
    internal bool WakeForInteraction(
        long actorId,
        DaggerfallSiteProfile profile,
        DaggerfallSiteLighting lighting,
        DaggerfallSiteAppearance appearance,
        DaggerfallSkyTime sky)
    {
        if (!actors.TryGet(actorId, out ActorState? resident)
            || resident.IsDefeated
            || !resident.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
            || !overlay.IsSleeping)
        {
            return false;
        }

        WorldPoint playerPos = player.Position ?? resident.Position;
        if (IsResidenceInterior(profile, out DaggerfallInteriorBuilding? building)
            && IsHomeSleepHour(sky.Hour)
            && !ownsInterior(building!))
        {
            TriggerIntruderWake(resident, profile, lighting, appearance, sky, playerPos);
            return true;
        }

        // Owned house or non-intruder wake: simply wake this resident calmly.
        overlay.IsSleeping = false;
        overlay.SleepingOnBed = false;
        FaceTarget(resident, playerPos);
        return true;
    }

    /// <summary>
    /// Wakes sleeping residents within <paramref name="radiusMeters"/> of an interior noise event
    /// (such as bashing a door or disturbing containers in a non-owned house at night).
    /// </summary>
    internal bool NotifyInteriorNoise(
        WorldPoint noisePosition,
        float radiusMeters,
        DaggerfallSiteProfile profile,
        DaggerfallSiteLighting lighting,
        DaggerfallSiteAppearance appearance,
        DaggerfallSkyTime sky)
    {
        if (!IsResidenceInterior(profile, out DaggerfallInteriorBuilding? building)
            || !IsHomeSleepHour(sky.Hour)
            || _intruderDetected
            || ownsInterior(building!))
        {
            return false;
        }

        float effectiveRadius = MathF.Max(0f, radiusMeters);
        ActorState? closest = null;
        float closestDist = float.MaxValue;
        foreach (ActorState resident in CollectResidents())
        {
            if (!resident.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay) || !overlay.IsSleeping)
                continue;
            float dist = resident.Position.HorizontalDistanceTo(noisePosition);
            if (dist <= effectiveRadius && MathF.Abs(resident.Position.Y - noisePosition.Y) <= SameFloorToleranceMeters * 1.5f && dist < closestDist)
            {
                closest = resident;
                closestDist = dist;
            }
        }

        if (closest is null)
            return false;

        TriggerIntruderWake(closest, profile, lighting, appearance, sky, player.Position ?? noisePosition);
        return true;
    }

    private void WakeAllForMorning(
        List<ActorState> residents,
        DaggerfallSiteLighting lighting,
        DaggerfallSiteAppearance appearance,
        DaggerfallSkyTime sky)
    {
        foreach (ActorState resident in residents)
        {
            if (!resident.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
                continue;
            if (overlay.IsSleeping && overlay.PreSleepPosition is WorldPoint standPos)
                resident.ApplyPose(new ActorPose(standPos, overlay.PreSleepHeading ?? resident.HeadingYawRadians));
            overlay.IsSleeping = false;
            overlay.SleepingOnBed = false;
            overlay.AwakenedByIntruder = false;
            overlay.PreSleepPosition = null;
            overlay.PreSleepHeading = null;
        }
        _awakenedActors.Clear();
        _bedAssignments.Clear();
        _lightsOut = false;
        _intruderDetected = false;
        _intruderReported = false;
        lighting.SetInteriorLightsOut(false, appearance, sky);
    }

    private ActorState? FindDetectingSleeper(WorldPoint playerPos)
    {
        bool crouched = player.Motion.Stance == CharacterStance.Crouched;
        Vector3 vel = player.Motion.ControlledVelocity;
        float horizontalSpeed = MathF.Sqrt((vel.X * vel.X) + (vel.Z * vel.Z));
        bool concealed = isPlayerConcealed();
        bool lit = hasPlayerLight();
        int stealth = actors.Player.Stats.GetStat(StatId.Parse("stealth")).ValueInt;

        float detectionRadius;
        if (crouched && !lit)
        {
            detectionRadius = Math.Clamp(
                MaxSneakDetectionRadiusMeters - ((stealth / 100f) * (MaxSneakDetectionRadiusMeters - MinSneakDetectionRadiusMeters)),
                MinSneakDetectionRadiusMeters,
                MaxSneakDetectionRadiusMeters);
        }
        else if (horizontalSpeed > 3.5f || lit)
        {
            detectionRadius = RunningOrLitDetectionRadiusMeters;
        }
        else
        {
            detectionRadius = StandingDetectionRadiusMeters;
        }

        if (concealed && !lit)
            detectionRadius *= 0.45f;

        ActorState? closest = null;
        float closestDist = float.MaxValue;
        foreach (ActorState resident in CollectResidents())
        {
            if (!resident.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay) || !overlay.IsSleeping)
                continue;
            if (MathF.Abs(resident.Position.Y - playerPos.Y) > SameFloorToleranceMeters)
                continue;
            float dist = resident.Position.HorizontalDistanceTo(playerPos);
            if (dist <= detectionRadius && dist < closestDist)
            {
                closest = resident;
                closestDist = dist;
            }
        }
        return closest;
    }

    private void TriggerIntruderWake(
        ActorState primaryDetector,
        DaggerfallSiteProfile profile,
        DaggerfallSiteLighting lighting,
        DaggerfallSiteAppearance appearance,
        DaggerfallSkyTime sky,
        WorldPoint playerPos)
    {
        _intruderDetected = true;
        _lightsOut = false;
        lighting.SetInteriorLightsOut(false, appearance, sky);

        List<long> awakenedIds = [];
        foreach (ActorState resident in CollectResidents())
        {
            if (!resident.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
                continue;
            if (overlay.IsSleeping)
            {
                WorldPoint wakePos = overlay.PreSleepPosition is WorldPoint standPos
                    && standPos.HorizontalDistanceTo(resident.Position) <= 4.0f
                    ? standPos
                    : resident.Position;
                resident.ApplyPose(new ActorPose(wakePos, YawToward(wakePos, playerPos)));
            }
            else
            {
                FaceTarget(resident, playerPos);
            }
            overlay.IsSleeping = false;
            overlay.SleepingOnBed = false;
            overlay.AwakenedByIntruder = true;
            _awakenedActors.Add(resident.DurableId);
            awakenedIds.Add(resident.DurableId);
        }

        string speaker = ResolveSpeakerName(primaryDetector.DurableId);
        reportOutcome($"{speaker} wakes with a start: \"Who's there?! Get out of my house! Guards!\"");

        if (!_intruderReported && profile.Site is { } siteId)
        {
            _intruderReported = true;
            DaggerfallNpc? residentNpc = npcs.TryGet(primaryDetector.DurableId, out DaggerfallNpc? found) ? found : null;
            DaggerfallCrimeVictimContext victim = new(
                ActorId: primaryDetector.DurableId,
                FactionId: residentNpc is { Appearance.FactionId: > 0 } ? residentNpc.Appearance.FactionId : null,
                BuildingKey: residentNpc?.Site.Building,
                DisplayName: residentNpc?.DisplayName,
                Region: siteId.Region,
                Location: residentNpc?.Site.Location);
            crimes.ReportNightIntrusion(primaryDetector.DurableId, siteId.Region, awakenedIds, victim);
        }
    }

    private List<ActorState> CollectResidents()
    {
        List<ActorState> result = [];
        foreach (ActorState actor in actors.All.OrderBy(a => a.DurableId))
        {
            if (actor.DurableId == DaggerfallActorIdentity.PlayerEntityId || actor.IsDefeated)
                continue;
            if (actor.Actor.TryGet<ActorHarmPolicy>(out ActorHarmPolicy? harm) && harm.IsCombatant)
                continue;
            if (actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
                && !string.IsNullOrWhiteSpace(overlay.StagedPartyRole))
                continue;
            if (!npcs.TryGet(actor.DurableId, out DaggerfallNpc? npc)
                || npc.Presence != DaggerfallNpcPresence.Active
                || string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(actor);
        }
        return result;
    }

    private string ResolveSpeakerName(long durableId) =>
        npcs.TryGet(durableId, out DaggerfallNpc? npc) && !string.IsNullOrWhiteSpace(npc.DisplayName)
            ? npc.DisplayName
            : "The resident";

    private static bool IsResidenceInterior(DaggerfallSiteProfile profile, out DaggerfallInteriorBuilding? building)
    {
        if (profile.ProfileKind == DaggerfallWorldProfileKind.Interior
            && profile.InteriorBuilding is { } interior
            && DaggerfallScheduleShelterResolver.IsResidenceBuildingType(interior.BuildingType))
        {
            building = interior;
            return true;
        }
        building = null;
        return false;
    }

    private static void FaceTarget(ActorState actor, WorldPoint target)
    {
        float yaw = YawToward(actor.Position, target);
        actor.ApplyPose(new ActorPose(actor.Position, yaw));
    }

    private static float YawToward(WorldPoint from, WorldPoint toward)
    {
        Vector3 delta = new(toward.X - from.X, 0f, toward.Z - from.Z);
        return delta.LengthSquared() > 1e-4f ? ActorHeading.Yaw(delta) : 0f;
    }
}
