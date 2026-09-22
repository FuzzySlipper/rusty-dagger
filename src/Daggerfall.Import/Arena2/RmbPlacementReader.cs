namespace Daggerfall.Import.Arena2;

/// <summary>One placed 3D object: its model, position and rotation.</summary>
/// <param name="ModelId">The model id.</param>
/// <param name="ObjectType">The object type byte.</param>
/// <param name="X">The X position.</param>
/// <param name="Y">The Y position.</param>
/// <param name="Z">The Z position.</param>
/// <param name="YRotation">The Y rotation.</param>
public sealed record RmbModelPlacement(string ModelId, byte ObjectType, int X, int Y, int Z, short YRotation);

/// <summary>One placed flat: its position, texture and faction.</summary>
/// <param name="X">The X position.</param>
/// <param name="Y">The Y position.</param>
/// <param name="Z">The Z position.</param>
/// <param name="TextureArchive">The texture archive.</param>
/// <param name="TextureRecord">The texture record.</param>
/// <param name="FactionId">The NPC faction.</param>
/// <param name="Flags">The flags.</param>
public sealed record RmbFlatPlacement(int X, int Y, int Z, int TextureArchive, int TextureRecord, short FactionId, byte Flags);

/// <summary>One section-3 waypoint: its position.</summary>
/// <param name="X">The X position.</param>
/// <param name="Y">The Y position.</param>
/// <param name="Z">The Z position.</param>
public sealed record RmbSectionPlacement(int X, int Y, int Z);

/// <summary>One placed person: its position, texture and faction.</summary>
/// <param name="X">The X position.</param>
/// <param name="Y">The Y position.</param>
/// <param name="Z">The Z position.</param>
/// <param name="TextureArchive">The texture archive.</param>
/// <param name="TextureRecord">The texture record.</param>
/// <param name="FactionId">The NPC faction.</param>
/// <param name="Flags">The NPC flags.</param>
public sealed record RmbPeoplePlacement(int X, int Y, int Z, int TextureArchive, int TextureRecord, short FactionId, byte Flags);

/// <summary>One placed door: its position, rotation and model.</summary>
/// <param name="X">The X position.</param>
/// <param name="Y">The Y position.</param>
/// <param name="Z">The Z position.</param>
/// <param name="YRotation">The Y rotation at starting position.</param>
/// <param name="OpenRotation">The angle to rotate into open position.</param>
/// <param name="DoorModelIndex">The model index offset from the base door id.</param>
public sealed record RmbDoorPlacement(int X, int Y, int Z, short YRotation, short OpenRotation, byte DoorModelIndex);

/// <summary>One half's placements: every record its counts declare, in the donor's order.</summary>
/// <param name="Models">The 3D object placements.</param>
/// <param name="Flats">The flat placements.</param>
/// <param name="Sections">The section-3 placements.</param>
/// <param name="People">The people placements.</param>
/// <param name="Doors">The door placements.</param>
public sealed record RmbHalfPlacements(
    IReadOnlyList<RmbModelPlacement> Models,
    IReadOnlyList<RmbFlatPlacement> Flats,
    IReadOnlyList<RmbSectionPlacement> Sections,
    IReadOnlyList<RmbPeoplePlacement> People,
    IReadOnlyList<RmbDoorPlacement> Doors);

/// <summary>One building's placements: both halves.</summary>
/// <param name="Exterior">The outside half's placements.</param>
/// <param name="Interior">The inside half's placements.</param>
public sealed record RmbBuildingPlacements(RmbHalfPlacements Exterior, RmbHalfPlacements Interior);

/// <summary>One block's placements: every building plus the block's own objects.</summary>
/// <param name="Buildings">One entry per building sub-record, in header order.</param>
/// <param name="MiscModels">The block's own 3D object placements.</param>
/// <param name="MiscFlats">The block's own flat placements.</param>
public sealed record RmbBlockPlacements(
    IReadOnlyList<RmbBuildingPlacements> Buildings,
    IReadOnlyList<RmbModelPlacement> MiscModels,
    IReadOnlyList<RmbFlatPlacement> MiscFlats);

/// <summary>
/// Decodes RMB placements: the records each half's counts declare, read in the donor's order
/// at the donor's sizes. Unknown and null words are not published; positions, model and texture
/// references, factions, flags and door rotations are. A half whose records run past the bytes
/// the inventory walked is refused rather than decoded from a guessed offset.
/// </summary>
public static class RmbPlacementReader
{
    /// <summary>Decodes one block's placements from the bytes the header summary walked.</summary>
    public static RmbBlockPlacements Read(byte[] bytes, int offset, RmbBlockSummary summary, string source)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        List<RmbBuildingPlacements> buildings = [];
        int position = RmbBlockSummaryReader.HeaderBytes;
        foreach (RmbBuildingSlot slot in summary.Buildings)
        {
            int exteriorStart = offset + position + RmbBlockSummaryReader.SubRecordHeaderBytes;
            RmbHalfPlacements exterior = ReadHalf(bytes, exteriorStart, slot.Exterior, $"{source} building {slot.Index} outside", source);
            int interiorStart = exteriorStart + slot.Exterior.BodyBytes + RmbBlockSummaryReader.SubRecordHeaderBytes;
            RmbHalfPlacements interior = ReadHalf(bytes, interiorStart, slot.Interior, $"{source} building {slot.Index} inside", source);
            buildings.Add(new RmbBuildingPlacements(exterior, interior));
            position += slot.ByteLength;
        }

        List<RmbModelPlacement> miscModels = [];
        int cursor = offset + position;
        for (int index = 0; index < summary.Misc3dObjects; index++)
        {
            miscModels.Add(ReadModel(bytes, cursor + (index * RmbObjectCounts.ModelRecordBytes), $"{source} misc object {index}", source));
        }

        cursor += summary.Misc3dObjects * RmbObjectCounts.ModelRecordBytes;
        List<RmbFlatPlacement> miscFlats = [];
        for (int index = 0; index < summary.MiscFlatObjects; index++)
        {
            miscFlats.Add(ReadFlat(bytes, cursor + (index * RmbObjectCounts.FlatRecordBytes), $"{source} misc flat {index}", source));
        }

        return new RmbBlockPlacements(buildings, miscModels, miscFlats);
    }

    private static RmbHalfPlacements ReadHalf(byte[] bytes, int start, RmbObjectCounts counts, string what, string source)
    {
        List<RmbModelPlacement> models = [];
        int cursor = start;
        for (int index = 0; index < counts.Objects; index++)
        {
            models.Add(ReadModel(bytes, cursor, $"{what} object {index}", source));
            cursor += RmbObjectCounts.ModelRecordBytes;
        }

        List<RmbFlatPlacement> flats = [];
        for (int index = 0; index < counts.Flats; index++)
        {
            flats.Add(ReadFlat(bytes, cursor, $"{what} flat {index}", source));
            cursor += RmbObjectCounts.FlatRecordBytes;
        }

        List<RmbSectionPlacement> sections = [];
        for (int index = 0; index < counts.Sections; index++)
        {
            int at = cursor + (index * RmbObjectCounts.SectionRecordBytes);
            Require(bytes, at, RmbObjectCounts.SectionRecordBytes, $"{what} section {index}", source);
            sections.Add(new RmbSectionPlacement(ReadInt(bytes, at), ReadInt(bytes, at + 4), ReadInt(bytes, at + 8)));
        }

        cursor += counts.Sections * RmbObjectCounts.SectionRecordBytes;
        List<RmbPeoplePlacement> people = [];
        for (int index = 0; index < counts.People; index++)
        {
            int at = cursor + (index * RmbObjectCounts.PeopleRecordBytes);
            Require(bytes, at, RmbObjectCounts.PeopleRecordBytes, $"{what} person {index}", source);
            int archive = ReadUShort(bytes, at + 12) >> 7;
            people.Add(new RmbPeoplePlacement(
                ReadInt(bytes, at), ReadInt(bytes, at + 4), ReadInt(bytes, at + 8),
                archive, ReadUShort(bytes, at + 12) & 0x7f, ReadShort(bytes, at + 14), bytes[at + 16]));
        }

        cursor += counts.People * RmbObjectCounts.PeopleRecordBytes;
        List<RmbDoorPlacement> doors = [];
        for (int index = 0; index < counts.Doors; index++)
        {
            int at = cursor + (index * RmbObjectCounts.DoorRecordBytes);
            Require(bytes, at, RmbObjectCounts.DoorRecordBytes, $"{what} door {index}", source);
            doors.Add(new RmbDoorPlacement(
                ReadInt(bytes, at), ReadInt(bytes, at + 4), ReadInt(bytes, at + 8),
                ReadShort(bytes, at + 12), ReadShort(bytes, at + 14), bytes[at + 16]));
        }

        return new RmbHalfPlacements(models, flats, sections, people, doors);
    }

    private static RmbModelPlacement ReadModel(byte[] bytes, int at, string what, string source)
    {
        Require(bytes, at, RmbObjectCounts.ModelRecordBytes, what, source);
        short objectId1 = ReadShort(bytes, at);
        string modelId = ((objectId1 * 100) + bytes[at + 2]).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new RmbModelPlacement(
            modelId, bytes[at + 3],
            ReadInt(bytes, at + 36), ReadInt(bytes, at + 40), ReadInt(bytes, at + 44),
            ReadShort(bytes, at + 52));
    }

    private static RmbFlatPlacement ReadFlat(byte[] bytes, int at, string what, string source)
    {
        Require(bytes, at, RmbObjectCounts.FlatRecordBytes, what, source);
        return new RmbFlatPlacement(
            ReadInt(bytes, at), ReadInt(bytes, at + 4), ReadInt(bytes, at + 8),
            ReadUShort(bytes, at + 12) >> 7, ReadUShort(bytes, at + 12) & 0x7f,
            ReadShort(bytes, at + 14), bytes[at + 16]);
    }

    private static void Require(byte[] bytes, int at, int length, string what, string source)
    {
        if (at < 0 || length < 0 || at + length > bytes.Length)
        {
            throw new Arena2FormatException(source, at, $"RMB {what} runs past the supplied bytes.");
        }
    }

    private static int ReadInt(byte[] bytes, int at) =>
        bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24);

    private static short ReadShort(byte[] bytes, int at) => (short)(bytes[at] | (bytes[at + 1] << 8));

    private static ushort ReadUShort(byte[] bytes, int at) => (ushort)(bytes[at] | (bytes[at + 1] << 8));
}
