using System.Numerics;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>Known action flags from the RDB action resource.</summary>
internal enum DaggerfallDungeonActionFlag : byte
{
    Translation = 0x01,
    Rotation = 0x02,
    PositiveX = 0x03,
    NegativeX = 0x04,
    PositiveZ = 0x05,
    NegativeZ = 0x06,
    PositiveY = 0x07,
    NegativeY = 0x08,
    CastSpell = 0x09,
    ShowText = 0x0B,
    ShowTextWithInput = 0x0C,
    Teleport = 0x0E,
    LockDoor = 0x10,
    UnlockDoor = 0x11,
    OpenDoor = 0x12,
    CloseDoor = 0x14,
    Hurt21 = 0x15,
    Hurt22 = 0x16,
    Hurt23 = 0x17,
    Hurt24 = 0x18,
    Hurt25 = 0x19,
    Poison = 0x1A,
    DrainMagicka = 0x1C,
    Dialogue = 0x1D,
    Activate = 0x1E,
    SetGlobalVar = 0x1F,
    DoorText = 0x63,
}

/// <summary>
/// One normalized action node.  The raw values remain alongside the resolved link
/// so diagnostics and later action-family tasks never have to reconstruct source
/// parameters from a product policy decision.
/// </summary>
internal sealed record DaggerfallDungeonActionDefinition(
    string Id,
    int SourceOffset,
    uint TriggerFlag,
    byte ActionFlag,
    byte Axis,
    ushort Duration,
    ushort Magnitude,
    int NextObjectOffset,
    string? NextActionId,
    string? DoorId = null,
    bool IsFlat = false,
    double CooldownSeconds = 0d,
    byte SoundIndex = 0,
    Vector3? SourcePosition = null,
    byte RawIndex = 0,
    DaggerfallDungeonPoisonAction? Poison = null,
    Vector3? NextObjectPosition = null,
    Vector3? NextObjectRotationDegrees = null)
{
    internal DaggerfallDungeonActionDefinition Validate(IReadOnlySet<string> actionIds)
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("A dungeon action requires a stable id.", nameof(Id));
        if (SourceOffset <= 0) throw new ArgumentOutOfRangeException(nameof(SourceOffset));
        if (ActionFlag == (byte)DaggerfallDungeonActionFlag.Poison && Poison is null)
            throw new ArgumentException($"Dungeon Poison action '{Id}' requires explicit source authority or exclusion.", nameof(Poison));
        Poison?.Validate(this);
        if (SourcePosition is Vector3 position && (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)))
            throw new ArgumentOutOfRangeException(nameof(SourcePosition));
        foreach (var point in new[] { NextObjectPosition, NextObjectRotationDegrees })
            if (point is Vector3 v && (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)))
                throw new ArgumentOutOfRangeException(nameof(NextObjectPosition));
        if (NextActionId is not null)
        {
            if (string.IsNullOrWhiteSpace(NextActionId)) throw new ArgumentException("An action link cannot have an empty target id.", nameof(NextActionId));
            if (!actionIds.Contains(NextActionId)) throw new ArgumentException($"Dungeon action '{Id}' links to unknown action '{NextActionId}'.", nameof(NextActionId));
            if (NextObjectOffset <= 0) throw new ArgumentException($"Dungeon action '{Id}' resolves a target while preserving non-link source offset {NextObjectOffset}.", nameof(NextActionId));
        }

        if (!double.IsFinite(CooldownSeconds) || CooldownSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(CooldownSeconds));
        return this;
    }
}

/// <summary>The admitted original-record exclusion; no poison variant was established for it.</summary>
internal sealed record DaggerfallDungeonPoisonAction(string SourceRecord, string Disposition, int? PoisonId = null)
{
    internal void Validate(DaggerfallDungeonActionDefinition action)
    {
        if (Disposition != "source-unresolved" || PoisonId is not null
            || SourceRecord != "N0000007.RDB" || action.SourceOffset != 20287
            || action.ActionFlag != (byte)DaggerfallDungeonActionFlag.Poison || !action.IsFlat || action.TriggerFlag != 2
            || action.Axis != 0 || action.Duration != 0 || action.Magnitude != 0
            || action.SoundIndex != 7 || action.RawIndex != 7
            || action.NextObjectOffset != -2 || action.NextActionId is not null || action.DoorId is not null)
            throw new ArgumentException($"Dungeon Poison action '{action.Id}' has no approved poison identity or source-record exclusion.");
    }
}
