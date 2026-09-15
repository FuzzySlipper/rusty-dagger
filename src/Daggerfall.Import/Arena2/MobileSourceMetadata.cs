using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Daggerfall.Import.Arena2;

/// <summary>
/// Immutable, source-facing mobile metadata adopted from Daggerfall Unity's
/// <c>EnemyBasics</c> tables. This type deliberately records source facts only;
/// ruleset code decides what a mobile means in a game and Engine services own
/// sprite display and animation playback.
/// </summary>
public static class MobileSourceMetadata
{
    // DFU BlocksFile.ScaleDivisor and MeshReader.GlobalScale, respectively.
    // These are structural source-format conversion factors, not tuning values.
    private const float ScaleDivisor = 256F;
    private const float WorldUnitScale = 0.025F;
    private const sbyte DamageBeatMarker = -1;

    private static readonly ImmutableArray<Arena2MobileFrameRecord> MoveRecords = CreateOrientationRecords(0, 1, 2, 3, 4, false);
    private static readonly ImmutableArray<Arena2MobileFrameRecord> PrimaryAttackRecords = CreateOrientationRecords(5, 6, 7, 8, 9, false);
    private static readonly ImmutableArray<Arena2MobileFrameRecord> HurtRecords = CreateOrientationRecords(10, 11, 12, 13, 14, false);
    private static readonly ImmutableArray<Arena2MobileFrameRecord> IdleRecords = CreateOrientationRecords(15, 16, 17, 18, 19, false);
    private static readonly ImmutableArray<Arena2MobileFrameRecord> RatIdleRecords = CreateOrientationRecords(15, 16, 17, 18, 19, true);
    private static readonly ImmutableArray<Arena2MobileFrameRecord> RangedAttack1Records = CreateOrientationRecords(20, 21, 22, 23, 24, false);
    private static readonly ImmutableArray<Arena2MobileFrameRecord> RangedAttack2Records = CreateOrientationRecords(25, 26, 27, 28, 29, false);

    private static readonly ImmutableArray<Arena2MobileSource> Sources = CreateSources();

    /// <summary>Gets the supported source-mobile entries in stable ID order.</summary>
    public static ImmutableArray<Arena2MobileSource> All => Sources;

    /// <summary>
    /// Looks up a supported source mobile. Unknown IDs remain absent rather than
    /// being assigned a Daggerfall gameplay meaning during import.
    /// </summary>
    public static bool TryGet(Arena2MobileId id, [NotNullWhen(true)] out Arena2MobileSource? source)
    {
        foreach (Arena2MobileSource candidate in Sources)
        {
            if (candidate.Id == id)
            {
                source = candidate;
                return true;
            }
        }

        source = null;
        return false;
    }

    /// <summary>
    /// Gets the classic eight-sector source record order and mirroring flags.
    /// This is archive-layout metadata only; it does not evaluate a camera,
    /// choose a direction, or play an animation.
    /// </summary>
    public static ImmutableArray<Arena2MobileFrameRecord> GetFrameRecords(Arena2MobileFrameGroup group)
    {
        return group switch
        {
            Arena2MobileFrameGroup.Move => MoveRecords,
            Arena2MobileFrameGroup.PrimaryAttack => PrimaryAttackRecords,
            Arena2MobileFrameGroup.Hurt => HurtRecords,
            Arena2MobileFrameGroup.Idle => IdleRecords,
            Arena2MobileFrameGroup.RatIdle => RatIdleRecords,
            Arena2MobileFrameGroup.RangedAttack1 => RangedAttack1Records,
            Arena2MobileFrameGroup.RangedAttack2 => RangedAttack2Records,
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, "The source frame group is not known."),
        };
    }

    /// <summary>
    /// Converts one Arena2 texture-record size to world metres using the
    /// source-format scale fields. It does not crop art or create a runtime
    /// sprite; normalization owns those decisions.
    /// </summary>
    public static Arena2RecordWorldSize GetRecordWorldSize(short width, short height, short scaleX, short scaleY)
    {
        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A source record width cannot be negative.");
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "A source record height cannot be negative.");
        }

        float adjustedWidth = width * (1F + (scaleX / ScaleDivisor));
        float adjustedHeight = height * (1F + (scaleY / ScaleDivisor));
        return new(adjustedWidth * WorldUnitScale, adjustedHeight * WorldUnitScale);
    }

    private static ImmutableArray<Arena2MobileSource> CreateSources()
    {
        ImmutableArray<Arena2MobileSource> sources =
        [
            CreateMobile(0, "Rat", 255, new(new(401), 1), Attack([0, 1, 2, DamageBeatMarker, 3, 4, 5]),
                new(Arena2MobileFrameGroup.RatIdle), new(null, "EnemyRatMove", "EnemyRatBark", "EnemyRatAttack", ParrySounds: false, BloodIndex: 0, MapChance: 0)),
            CreateMobile(1, "Imp", 256, new(new(406), 5), Attack([0, 1, 2, DamageBeatMarker, 3, 1]),
                new(Arena2MobileFrameGroup.Move, MoveFramesPerSecond: 10F), new("D", "EnemyImpMove", "EnemyImpBark", "EnemyImpAttack", ParrySounds: false, BloodIndex: 0, MapChance: 1)),
            CreateMobile(3, "GiantBat", 258, new(new(401), 0), Attack([0, 1, DamageBeatMarker, 2, 3]),
                new(Arena2MobileFrameGroup.Move, MoveFramesPerSecond: 10F), new(null, "EnemyGiantBatMove", "EnemyGiantBatBark", "EnemyGiantBatAttack", ParrySounds: false, BloodIndex: 0, MapChance: 0)),
            CreateMobile(4, "GrizzlyBear", 259, new(new(401), 2), Attack([0, 1, 2, DamageBeatMarker, 3, 0]),
                links: new(null, "EnemyBearMove", "EnemyBearBark", "EnemyBearAttack", ParrySounds: false, BloodIndex: 0, MapChance: 0)),
            CreateMobile(7, "Orc", 262, new(new(96), 2), Attack([0, 1, 2, DamageBeatMarker, 3, 4, DamageBeatMarker, 5, 0], Alternate(50, [4, DamageBeatMarker, 5, 0])),
                links: new("A", "EnemyOrcMove", "EnemyOrcBark", "EnemyOrcAttack", ParrySounds: true, BloodIndex: 0, MapChance: 0)),
            CreateMobile(15, "SkeletalWarrior", 270, new(new(306), 1), Attack([0, 1, 2, 3, DamageBeatMarker, 4, 5]),
                links: new("H", "EnemySkeletonMove", "EnemySkeletonBark", "EnemySkeletonAttack", ParrySounds: true, BloodIndex: 2, MapChance: 1)),
            CreateMobile(138, "Thief", 484, new(new(380), 1), Attack([0, 1, DamageBeatMarker, 2, 3, 4, DamageBeatMarker, 5, 0], Alternate(33, [4, 4, DamageBeatMarker, 5, 0, 0]), Alternate(33, [4, DamageBeatMarker, 5, 0, 0, 1, DamageBeatMarker, 2, 3, 4, DamageBeatMarker, 5, 0])),
                // The thief's ranged declaration is the donor's RangedAttack1Anims group: the humanoid
                // bow sequence whose marked frame is the donor's shoot moment, not a damage frame.
                links: new("O", "EnemyHumanMove", "EnemyHumanBark", "EnemyHumanAttack", ParrySounds: true, BloodIndex: 0, MapChance: 2),
                rangedAttackFrames: RangedAttack([3, 2, 0, 0, 0, DamageBeatMarker, 1, 1, 2, 3])),
            CreateMobile(141, "Archer", 482, new(new(380), 1), Attack([0, 1, DamageBeatMarker, 2, 3, 4, DamageBeatMarker, 5], Alternate(50, [3, 4, DamageBeatMarker, 5, 0])),
                // The archer carries the same donor RangedAttack1Anims sequence as the thief.
                links: new("C", "EnemyHumanMove", "EnemyHumanBark", "EnemyHumanAttack", ParrySounds: true, BloodIndex: 0, MapChance: 0),
                rangedAttackFrames: RangedAttack([3, 2, 0, 0, 0, DamageBeatMarker, 1, 1, 2, 3])),
        ];

        HashSet<Arena2MobileId> ids = [];
        foreach (Arena2MobileSource source in sources)
        {
            if (!ids.Add(source.Id))
            {
                throw new InvalidOperationException($"The mobile source table contains duplicate ID {source.Id.Value}.");
            }
        }

        return sources;
    }

    private static Arena2MobileSource CreateMobile(
        byte id,
        string sourceName,
        ushort textureArchive,
        Arena2MobileCorpseSource? corpse,
        Arena2MobileAttackSequence attackSequence,
        Arena2MobileAnimationSource? animation = null,
        Arena2MobileForeignLinks? links = null,
        ImmutableArray<sbyte>? rangedAttackFrames = null)
    {
        return new(new(id), sourceName, new(textureArchive), corpse, attackSequence, rangedAttackFrames, animation ?? Arena2MobileAnimationSource.Ordinary, links ?? Unlinked(id));
    }

    /// <summary>
    /// The donor's table carries these facts for every mobile it lists, so a supported
    /// mobile without them still names what it is missing rather than claiming silence.
    /// </summary>
    private static Arena2MobileForeignLinks Unlinked(byte id) =>
        throw new InvalidOperationException($"Supported source mobile {id} must carry the donor's loot, vocal, blood and map-chance facts.");

    private static Arena2MobileAttackSequence Attack(sbyte[] primaryFrames, params Arena2MobileAttackAlternate[] alternates)
    {
        ValidateFrames(primaryFrames, nameof(primaryFrames));
        return new(primaryFrames.ToImmutableArray(), alternates.ToImmutableArray());
    }

    private static ImmutableArray<sbyte> RangedAttack(sbyte[] frames)
    {
        ValidateFrames(frames, nameof(frames));
        return frames.ToImmutableArray();
    }

    private static Arena2MobileAttackAlternate Alternate(byte chance, sbyte[] frames)
    {
        if (chance is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chance), chance, "A source alternate chance must be positive.");
        }

        ValidateFrames(frames, nameof(frames));
        return new(chance, frames.ToImmutableArray());
    }

    private static void ValidateFrames(ReadOnlySpan<sbyte> frames, string parameterName)
    {
        if (frames.IsEmpty)
        {
            throw new ArgumentException("A source attack sequence must contain at least one record-frame value.", parameterName);
        }

        foreach (sbyte frame in frames)
        {
            if (frame < DamageBeatMarker)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Source attack frames may be non-negative indices or the -1 source damage-beat marker.");
            }
        }
    }

    private static ImmutableArray<Arena2MobileFrameRecord> CreateOrientationRecords(ushort front, ushort frontRight, ushort right, ushort backRight, ushort back, bool invertSideMirroring)
    {
        return
        [
            new(front, false),
            new(frontRight, invertSideMirroring),
            new(right, invertSideMirroring),
            new(backRight, invertSideMirroring),
            new(back, false),
            new(backRight, !invertSideMirroring),
            new(right, !invertSideMirroring),
            new(frontRight, !invertSideMirroring),
        ];
    }
}

/// <summary>Typed classic mobile ID from an Arena2/RDB source record.</summary>
public readonly record struct Arena2MobileId(byte Value);

/// <summary>Typed TEXTURE archive ID from an Arena2 source record.</summary>
public readonly record struct Arena2TextureArchiveId(ushort Value);

/// <summary>Archive and record identifying a classic corpse-marker texture.</summary>
public readonly record struct Arena2MobileCorpseSource(Arena2TextureArchiveId TextureArchive, ushort Record);

/// <summary>
/// One source attack sequence. Frame values are unselected source data: -1 is
/// the classic damage-beat marker and all non-negative values are record-frame
/// indices. No damage timing or animation playback is performed here.
/// </summary>
public sealed class Arena2MobileAttackSequence
{
    internal Arena2MobileAttackSequence(ImmutableArray<sbyte> primaryFrames, ImmutableArray<Arena2MobileAttackAlternate> alternates)
    {
        PrimaryFrames = primaryFrames;
        Alternates = alternates;
    }

    /// <summary>Primary source record-frame values in their original order.</summary>
    public ImmutableArray<sbyte> PrimaryFrames { get; }

    /// <summary>Unselected source alternate sequences in their original order.</summary>
    public ImmutableArray<Arena2MobileAttackAlternate> Alternates { get; }
}

/// <summary>
/// The non-media links the donor's own enemy table carries for a mobile: the loot table
/// key it rolls from, the vocal cue names it plays, its blood splash index and its map
/// chance. These are source facts; which cue a game plays and how loot is authored belong
/// to the ruleset and content tasks that consume them.
/// </summary>
public sealed record Arena2MobileForeignLinks(
    string? LootTableKey,
    string MoveSoundCue,
    string BarkSoundCue,
    string AttackSoundCue,
    bool ParrySounds,
    int BloodIndex,
    int MapChance)
{
    /// <summary>The classic loot keys a donor table may name, from the archive's own tables.</summary>
    public const int MaximumMapChance = 100;

    public void Validate()
    {
        if (LootTableKey is not null && string.IsNullOrWhiteSpace(LootTableKey))
        {
            throw new ArgumentException("A mobile loot table key must be absent or non-empty.", nameof(LootTableKey));
        }

        foreach ((string name, string cue) in ((string Name, string Value)[])[
            (nameof(MoveSoundCue), MoveSoundCue),
            (nameof(BarkSoundCue), BarkSoundCue),
            (nameof(AttackSoundCue), AttackSoundCue)])
        {
            if (string.IsNullOrWhiteSpace(cue))
            {
                // Name the cue that failed: a consumer fixing link data needs to know which
                // of the three it is.
                throw new ArgumentException($"A mobile vocal cue must name the donor clip it plays ({name}).", name);
            }
        }

        if (BloodIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BloodIndex), BloodIndex, "A mobile blood index cannot be negative.");
        }

        if (MapChance is < 0 or > MaximumMapChance)
        {
            throw new ArgumentOutOfRangeException(nameof(MapChance), MapChance, $"A mobile map chance is a percentage from 0 to {MaximumMapChance}.");
        }
    }
}

/// <summary>One unselected source alternate attack sequence and its Dice100 threshold.</summary>
public readonly record struct Arena2MobileAttackAlternate(byte Chance, ImmutableArray<sbyte> Frames);

/// <summary>One supported source mobile and its immutable archive metadata.</summary>
public sealed class Arena2MobileSource
{
    internal Arena2MobileSource(
        Arena2MobileId id,
        string sourceName,
        Arena2TextureArchiveId textureArchive,
        Arena2MobileCorpseSource? corpse,
        Arena2MobileAttackSequence attackSequence,
        ImmutableArray<sbyte>? rangedAttackFrames,
        Arena2MobileAnimationSource animation,
        Arena2MobileForeignLinks links)
    {
        Id = id;
        SourceName = sourceName;
        TextureArchive = textureArchive;
        Corpse = corpse;
        AttackSequence = attackSequence;
        RangedAttackFrames = rangedAttackFrames;
        ArgumentNullException.ThrowIfNull(animation);
        animation.Validate();
        Animation = animation;
        ArgumentNullException.ThrowIfNull(links);
        links.Validate();
        Links = links;
    }

    /// <summary>Classic mobile ID.</summary>
    public Arena2MobileId Id { get; }

    /// <summary>DFU source-table name, retained for importer provenance.</summary>
    public string SourceName { get; }

    /// <summary>Classic live-mobile texture archive.</summary>
    public Arena2TextureArchiveId TextureArchive { get; }

    /// <summary>Optional separate classic corpse-marker texture source.</summary>
    public Arena2MobileCorpseSource? Corpse { get; }

    /// <summary>Uninterpreted classic attack record-frame metadata.</summary>
    public Arena2MobileAttackSequence AttackSequence { get; }

    /// <summary>
    /// Optional unselected ranged-attack record-frame values in donor order. Presence is the
    /// donor's HasRangedAttack1 fact, and -1 keeps its damage-beat meaning: in the donor's
    /// ranged sequence that marked frame is the moment the missile is launched. The donor's
    /// separate RangedAttack2 group names no adopted mobile, so no mobile declares it here.
    /// </summary>
    public ImmutableArray<sbyte>? RangedAttackFrames { get; }

    /// <summary>
    /// Source-backed rest selection and effective animation cadence. This
    /// preserves archive playback separately from the classic mobile behavior
    /// that selects a rest group or fly cadence.
    /// </summary>
    public Arena2MobileAnimationSource Animation { get; }

    /// <summary>
    /// The donor's loot, vocal, blood and map-chance facts for this mobile. Which cue
    /// plays and how loot is generated are decisions for the tasks that consume them.
    /// </summary>
    public Arena2MobileForeignLinks Links { get; }
}

/// <summary>
/// Pinned classic mobile animation semantics adopted from Daggerfall Unity's
/// EnemyBasics behavior. Values here retain source/game facts for offline
/// normalization; the runtime only consumes the emitted normalized result.
/// </summary>
public sealed record Arena2MobileAnimationSource(
    Arena2MobileFrameGroup PreferredRestGroup,
    float? MoveFramesPerSecond = null)
{
    public static Arena2MobileAnimationSource Ordinary { get; } = new(Arena2MobileFrameGroup.Idle);

    public void Validate()
    {
        if (PreferredRestGroup is not (Arena2MobileFrameGroup.Move or Arena2MobileFrameGroup.Idle or Arena2MobileFrameGroup.RatIdle))
        {
            throw new ArgumentOutOfRangeException(nameof(PreferredRestGroup), "A mobile preferred rest group must be a mobile or idle source group.");
        }

        ValidateFramesPerSecond(MoveFramesPerSecond, nameof(MoveFramesPerSecond));
    }

    public float? EffectiveFramesPerSecond(Arena2MobileFrameGroup group) => group switch
    {
        Arena2MobileFrameGroup.Move => MoveFramesPerSecond,
        _ => null,
    };

    private static void ValidateFramesPerSecond(float? value, string name)
    {
        if (value is <= 0F || (value is not null && !float.IsFinite(value.Value)))
        {
            throw new ArgumentOutOfRangeException(name, "An effective mobile frame rate must be finite and positive.");
        }
    }
}

/// <summary>Named source record ranges; these are not runtime animation states.</summary>
public enum Arena2MobileFrameGroup
{
    Move,
    PrimaryAttack,
    Hurt,
    Idle,
    RatIdle,
    RangedAttack1,
    RangedAttack2,
}

/// <summary>One texture record in an eight-sector source mapping.</summary>
public readonly record struct Arena2MobileFrameRecord(ushort Record, bool IsHorizontallyMirrored);

/// <summary>World size derived from one source record before art cropping or atlas packing.</summary>
public readonly record struct Arena2RecordWorldSize(float WidthMeters, float HeightMeters);
