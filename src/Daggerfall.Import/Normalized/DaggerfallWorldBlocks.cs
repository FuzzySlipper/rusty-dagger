using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>Which half of the world a published block is.</summary>
public enum DaggerfallWorldBlockKind
{
    /// <summary>An RMB block's exterior: its buildings' outside halves, its own objects and its ground.</summary>
    RmbExterior,

    /// <summary>One RMB building's inside half, which the donor builds in its own frame.</summary>
    RmbInterior,

    /// <summary>An RDB dungeon block.</summary>
    Rdb,
}

/// <summary>
/// One block's collision and navigation, in the Engine's binary Spatial content artifact form and the
/// block's own frame, ready to be placed beside its neighbours by whole navigation cells.
/// </summary>
/// <param name="RelativePath">The artifact's path in the world block publication.</param>
/// <param name="ContentDigest">The artifact's digest.</param>
/// <param name="ByteLength">The artifact's length.</param>
/// <param name="NavigationId">The navigation surface the artifact states.</param>
/// <param name="StaticMeshArtifactId">The static mesh identity it states: the block's own model placements.</param>
public sealed record DaggerfallWorldBlockSpatial(
    string RelativePath,
    ContentDigest ContentDigest,
    long ByteLength,
    string NavigationId,
    string StaticMeshArtifactId);

/// <summary>
/// One placed model: a product-wide mesh, its transform in the block's frame, and what moves it.
/// </summary>
/// <param name="Id">The placement's identity in the block.</param>
/// <param name="ModelId">The ARCH3D model number.</param>
/// <param name="MeshArtifactId">
/// The world media publication's mesh artifact for the model, or null when the archive cannot serve it
/// (<see cref="UnresolvedReason"/> says why): no geometry stands in for a mesh that is not there.
/// </param>
/// <param name="Position">The model origin in the block's right-handed metre frame.</param>
/// <param name="RotationDegrees">
/// The model's source Euler degrees, applied in the donor's <c>Rz * Rx * Ry</c> order to its importer-space
/// points before the right-handed flip: the convention door and action placements already publish.
/// </param>
public sealed record DaggerfallWorldBlockModel(
    string Id,
    string ModelId,
    string? MeshArtifactId,
    NormalizedVector3 Position,
    NormalizedVector3 RotationDegrees)
{
    /// <summary>The RMB building sub-record that places the model, or null for the block's own objects.</summary>
    public int? BuildingIndex { get; init; }

    /// <summary>Why the mesh archive cannot serve the model, when it cannot.</summary>
    public string? UnresolvedReason { get; init; }

    /// <summary>The RDB action that moves the model, if any: its mesh then stays local to an instance transform.</summary>
    public DaggerfallWorldBlockModelAction? Action { get; init; }

    /// <summary>The RDB action door the model is, if it is one.</summary>
    public string? DoorId { get; init; }

    /// <summary>
    /// A static RDB placement's bounds in the block's frame: the extent dungeon map discovery attributes a
    /// surface to the placement by.
    /// </summary>
    public NormalizedBounds? Bounds { get; init; }

    /// <summary>A static RDB placement's two to four map visibility samples, in the block's frame.</summary>
    public IReadOnlyList<NormalizedVector3>? SamplePoints { get; init; }

    /// <summary>A moving RDB placement's (door or action model) bounds in its own model frame.</summary>
    public NormalizedBounds? LocalBounds { get; init; }

    /// <summary>An RDB action model's model-local collision triangles, which move with its instance transform.</summary>
    public DaggerfallWorldBlockCollision? Collision { get; init; }
}

/// <summary>Model-local collision geometry: every drawable plane of the mesh as fan triangles.</summary>
public sealed record DaggerfallWorldBlockCollision(IReadOnlyList<NormalizedVector3> Vertices, IReadOnlyList<NormalizedTriangle> Triangles);

/// <summary>The source facts an RDB action model keeps besides its action node.</summary>
public sealed record DaggerfallWorldBlockModelAction(string ActionId, string Description, int ModelIndex, int RawIndex);

/// <summary>
/// One door: an RMB exterior building door is a plane of a placed building mesh (texture archive 74 in its
/// climate set); an RDB action door is a whole placed model.
/// </summary>
/// <param name="Id">The door's identity in the block.</param>
/// <param name="ModelId">The placement (<see cref="DaggerfallWorldBlockModel.Id"/>) the door belongs to.</param>
/// <param name="DoorResourceId">The door definition it instantiates.</param>
/// <param name="Position">The door's position in the block's frame.</param>
/// <param name="RotationDegrees">The door's rotation, in the placement convention.</param>
public sealed record DaggerfallWorldBlockDoor(
    string Id,
    string ModelId,
    string DoorResourceId,
    NormalizedVector3 Position,
    NormalizedVector3 RotationDegrees)
{
    /// <summary>For an RMB plane door, the plane's index in its mesh's source planes.</summary>
    public int? Plane { get; init; }

    /// <summary>For an RMB plane door, the source texture the plane carries.</summary>
    public DaggerfallWorldBlockTexture? Texture { get; init; }

    public string Kind { get; init; } = "normal";

    public int StartingLockValue { get; init; }

    public NormalizedDoorAction? Action { get; init; }

    /// <summary>The door's local interaction/collision volume, for a planar exterior door.</summary>
    public NormalizedBounds? CollisionBounds { get; init; }

    /// <summary>The building sub-record an exterior door enters.</summary>
    public int? BuildingIndex { get; init; }
}

/// <summary>
/// Where a static transition door leads: the donor <c>MeshReader</c> door types other than an RMB exterior's
/// building door, which <see cref="DaggerfallWorldBlockDoor"/> carries.
/// </summary>
public enum DaggerfallWorldBlockTransitionKind
{
    /// <summary>A building door seen from inside (archive 74 in its climate set): it leads back outside.</summary>
    BuildingExit,

    /// <summary>A dungeon entrance on an RMB exterior (archive 56, or a non-zero record of archive 331): it leads into the dungeon.</summary>
    DungeonEntrance,

    /// <summary>A dungeon exit in an RDB block (archive 95): it leads out to the location's exterior.</summary>
    DungeonExit,
}

/// <summary>
/// One static transition door: a door plane of a placed model that changes worlds when used, as the donor's
/// <c>GameObjectHelper.GetStaticDoors</c> reads it. The plane stays part of its model's static geometry.
/// </summary>
/// <param name="Id">The door's identity in the block.</param>
/// <param name="ModelId">The placement (<see cref="DaggerfallWorldBlockModel.Id"/>) the plane belongs to.</param>
/// <param name="Kind">Where it leads.</param>
/// <param name="Position">The plane's centre (its opposite source corners' midpoint) in the block's frame.</param>
/// <param name="Normal">The plane's unit normal in the block's frame, the donor's door normal.</param>
/// <param name="Bounds">The plane's extent in the block's frame: where its threshold lies.</param>
/// <param name="Plane">The plane's index in its mesh's source planes.</param>
/// <param name="Texture">The source texture that classifies the plane.</param>
public sealed record DaggerfallWorldBlockTransitionDoor(
    string Id,
    string ModelId,
    DaggerfallWorldBlockTransitionKind Kind,
    NormalizedVector3 Position,
    NormalizedVector3 Normal,
    NormalizedBounds Bounds,
    int Plane,
    DaggerfallWorldBlockTexture Texture);

/// <summary>One state of a city gate: the model it draws and the model-local collision it blocks with.</summary>
/// <param name="ModelId">The ARCH3D model number of this state.</param>
/// <param name="MeshArtifactId">The world media publication's mesh for the model.</param>
/// <param name="LocalBounds">The model's bounds in its own frame.</param>
/// <param name="Collision">The model's model-local collision triangles.</param>
public sealed record DaggerfallWorldBlockGateState(string ModelId, string MeshArtifactId, NormalizedBounds LocalBounds, DaggerfallWorldBlockCollision Collision);

/// <summary>
/// One RMB city gate (ARCH3D 446 open, 447 closed, DFU <c>RMBLayout.IsCityGate</c>): a placement whose model
/// the donor's <c>DaggerfallCityGate</c> swaps between its open and closed variants by the time of day. Its
/// planes are left out of the block's static collision; each state carries its own, posed by the placement.
/// </summary>
/// <param name="Id">The gate's identity in the block.</param>
/// <param name="ModelId">The placement (<see cref="DaggerfallWorldBlockModel.Id"/>) the gate is.</param>
/// <param name="Position">The placement origin in the block's frame.</param>
/// <param name="RotationDegrees">The placement's source Euler degrees, in the model placement convention.</param>
public sealed record DaggerfallWorldBlockGate(
    string Id,
    string ModelId,
    NormalizedVector3 Position,
    NormalizedVector3 RotationDegrees,
    DaggerfallWorldBlockGateState Open,
    DaggerfallWorldBlockGateState Closed);

/// <summary>A source texture: archive and record.</summary>
public sealed record DaggerfallWorldBlockTexture(int Archive, int Record);

/// <summary>One RMB building sub-record as the block states it.</summary>
public sealed record DaggerfallWorldBlockBuilding(int Index, int BuildingType, int FactionId, int Quality, int NameSeed, bool HasInterior);

/// <summary>One FLD ground tile: its four-frame texture record and orientation, read in the location's climate ground set.</summary>
public sealed record DaggerfallWorldBlockGroundTile(int X, int Y, int TextureRecord, bool Rotated, bool Flipped);

/// <summary>One quest spawn or item marker in the block's frame.</summary>
public sealed record DaggerfallWorldBlockQuestMarker(string Id, NormalizedQuestMarkerKind Kind, NormalizedVector3 Position, int SourceOrdinal);

/// <summary>
/// One RDB random-enemy marker in the block's frame. The placing location chooses its enemy: its encounter
/// list's entry at <see cref="EncounterSlot"/>, from the water list when the marker lies below the block's
/// water level.
/// </summary>
/// <param name="EncounterSlot">The slot in the location's encounter list; zero lets the placement choose one of slots 1 to 6.</param>
/// <param name="SpawnDistance">The classic spawn-distance type the placed enemy keeps.</param>
/// <param name="Passive">Whether the enemy starts passive rather than hostile.</param>
/// <param name="ActionId">The marker's node in the block's action graph, when it is one; it stays a marker.</param>
/// <param name="SourceOrdinal">The flat's ordinal in the block's flat records.</param>
/// <param name="ObjectOffset">The flat's source object offset, unique within the block.</param>
public sealed record DaggerfallWorldBlockRandomEnemy(string Id, NormalizedVector3 Position, int EncounterSlot, int SpawnDistance,
    bool Passive, string? ActionId, int SourceOrdinal, int ObjectOffset);

/// <summary>
/// One interior static person. Its classic name seed is <see cref="SourceOffset"/> XOR the building key
/// (block X, Y and building index) plus the location index, so the placing location supplies the rest.
/// </summary>
public sealed record DaggerfallWorldBlockStaticNpc(string Id, NormalizedVector3 Position, int BillboardArchive,
    int BillboardRecord, int FactionId, string? Race, string Gender, int SourceOffset);

/// <summary>
/// One block's normalized per-block facts, in the block's own right-handed metre frame (origin at the
/// block's near corner, as a site places its block). Only what the block itself states is here: a placing
/// location supplies its grid position, its climate (ground and texture swaps), its dungeon texture table,
/// start block and dungeon type, and its own identities. Static meshes are placements of the world media
/// publication's meshes; no media is carried.
/// </summary>
/// <remarks>
/// Identities follow the site closures' with the placement coordinates left out: a site names a block's
/// door <c>door/{block}/{x}/{y}/{n}</c> where the block names it <c>door/{block}/{n}</c>, and likewise its
/// lights, flats, models and actions. An RMB interior's identities are the interior closure's own.
/// </remarks>
public sealed record DaggerfallWorldBlock(
    string Key,
    DaggerfallWorldBlockKind Kind,
    string SourceKey,
    int SourceOrdinal,
    int? BuildingIndex,
    NormalizedBounds? Bounds,
    DaggerfallWorldBlockSpatial? Spatial,
    IReadOnlyList<DaggerfallWorldBlockModel> Models)
{
    public IReadOnlyList<DaggerfallWorldBlockDoor> Doors { get; init; } = [];

    /// <summary>
    /// The block's static transition doors: an interior's building exits, an RMB exterior's dungeon entrances
    /// and an RDB block's dungeon exits.
    /// </summary>
    public IReadOnlyList<DaggerfallWorldBlockTransitionDoor> TransitionDoors { get; init; } = [];

    /// <summary>An RMB exterior's city gates.</summary>
    public IReadOnlyList<DaggerfallWorldBlockGate> Gates { get; init; } = [];

    /// <summary>The block's first start marker, the one a site closure starts from.</summary>
    public NormalizedMarker? StartMarker { get; init; }

    /// <summary>
    /// Every start marker an RMB exterior places, in source order (<c>marker/start/{n}</c>): the donor's
    /// arrival at a town chooses whichever of a location's start markers lies nearest the side it arrives from.
    /// </summary>
    public IReadOnlyList<NormalizedMarker> StartMarkers { get; init; } = [];

    public NormalizedMarker? EnterMarker { get; init; }

    public IReadOnlyList<DaggerfallWorldBlockQuestMarker> QuestMarkers { get; init; } = [];

    /// <summary>An RMB exterior's building sub-records.</summary>
    public IReadOnlyList<DaggerfallWorldBlockBuilding> Buildings { get; init; } = [];

    /// <summary>An RMB exterior's ground tiles.</summary>
    public IReadOnlyList<DaggerfallWorldBlockGroundTile> Ground { get; init; } = [];

    /// <summary>
    /// An RMB exterior's clear automap cells, CityNavigation's outdoor mask: base64 of a 64-by-64 bit grid,
    /// cell (x, z) at bit <c>x + 64 z</c> (bit <c>i % 8</c> of byte <c>i / 8</c>), z counted from the block's near
    /// edge as the right-handed frame places it.
    /// </summary>
    public string? ClearGround { get; init; }

    /// <summary>An RMB exterior's fixed people.</summary>
    public IReadOnlyList<NormalizedPopulationPlacement> Population { get; init; } = [];

    /// <summary>The RMB interior building's type and faction.</summary>
    public DaggerfallWorldBlockBuilding? InteriorBuilding { get; init; }

    public IReadOnlyList<DaggerfallWorldBlockStaticNpc> StaticNpcs { get; init; } = [];

    public IReadOnlyList<NormalizedPropertyContainer> PropertyContainers { get; init; } = [];

    /// <summary>RDB lights and the light a lamp flat casts.</summary>
    public IReadOnlyList<NormalizedLightPlacement> Lights { get; init; } = [];

    /// <summary>RDB billboards, by their source texture; a dungeon's texture table may remap the archive.</summary>
    public IReadOnlyList<NormalizedBillboardPlacement> Billboards { get; init; } = [];

    /// <summary>RDB fixed mobiles.</summary>
    public IReadOnlyList<NormalizedActorPlacement> Actors { get; init; } = [];

    /// <summary>RDB random treasure markers; the location's dungeon type selects their loot.</summary>
    public IReadOnlyList<NormalizedMarker> Treasures { get; init; } = [];

    /// <summary>RDB random-enemy markers; the location's encounter list selects their enemies.</summary>
    public IReadOnlyList<DaggerfallWorldBlockRandomEnemy> RandomEnemies { get; init; } = [];

    public IReadOnlyList<NormalizedDungeonAction> Actions { get; init; } = [];

    /// <summary>The RDB block's water surface height in its frame (metres, Y up), or null when it has no water.</summary>
    public float? WaterLevel { get; init; }

    /// <summary>The ambient area an RDB block selects, if any.</summary>
    public NormalizedAmbientZoneKind? AmbientZone { get; init; }
}

/// <summary>One published block in the world block index.</summary>
public sealed record DaggerfallWorldBlockEntry(string Key, DaggerfallWorldBlockKind Kind, string SourceKey, int? BuildingIndex, string Document, string? Spatial);

/// <summary>How many blocks of each kind the publication carries.</summary>
public sealed record DaggerfallWorldBlockCounts(int RmbExterior, int RmbInterior, int Rdb);

/// <summary>
/// The world block publication's index: every block a location places, each with its document and spatial
/// artifact, and the RMB records of the block archive no location places, named rather than dropped.
/// </summary>
/// <param name="MeshPublication">The content root of the world media publication whose meshes the models name.</param>
public sealed record DaggerfallWorldBlockIndex(
    string MeshPublication,
    DaggerfallWorldBlockCounts Counts,
    IReadOnlyList<DaggerfallWorldBlockEntry> Blocks,
    IReadOnlyList<string> UnplacedRmbBlocks,
    IReadOnlyList<PublishedSource> Sources);
