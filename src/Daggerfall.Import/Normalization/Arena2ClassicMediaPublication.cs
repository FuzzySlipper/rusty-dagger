using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// Caller-owned bytes for the finite set of classic media selected for the
/// Daggerfall compatibility pack. This type does not name a directory and
/// therefore remains suitable for offline import tools and tests.
/// </summary>
public sealed record Arena2ClassicMediaInputs(
    byte[] Weapon01Cif,
    byte[] Weapon02Cif,
    byte[] Weapon04Cif,
    byte[] Weapon05Cif,
    byte[] Weapon06Cif,
    byte[] Weapon07Cif,
    byte[] Weapon08Cif,
    byte[] Weapon09Cif,
    byte[] Weapon10Cif,
    byte[] ArtPalette,
    byte[] Texture380,
    byte[] Palette,
    byte[] DaggerSound,
    byte[] Main00I0Img,
    byte[] Main03I0Img,
    byte[] Main04I0Img,
    byte[] Main05I0Img,
    byte[] Inve00I0Img,
    byte[] Info00I0Img,
    byte[] Die00I0Img,
    byte[] Chgn00I0Img,
    byte[] Pick02I0Img,
    byte[] Pick03I0Img,
    byte[] Pris00I0Img,
    byte[] Titl00I0Img,
    byte[] Book00I0Img,
    byte[] Rest00I0Img,
    byte[] Shop00I0Img,
    byte[] Gild00I0Img,
    byte[] Bank00I0Img,
    byte[] Rest01I0Img,
    byte[] Rest02I0Img,
    byte[] Inve08I0Img,
    byte[] Inve10I0Img,
    byte[] Inve11I0Img,
    byte[] Inve12I0Img,
    byte[] Inve14I0Img,
    byte[] Gild01I0Img,
    byte[] Texture207,
    byte[] Texture216,
    byte[] Texture234,
    byte[] Texture245,
    byte[] Font0003Fnt,
    // The four font tables outside the original single-font closure, appended here rather than in
    // numeric order so the existing positions above stay stable: every parameter is a byte array,
    // so an insertion in the middle would misorder silently instead of failing to compile.
    byte[] Weapon00Cif,
    byte[] Weapon03Cif,
    byte[] Weapon11Cif,
    byte[] Font0000Fnt,
    byte[] Font0001Fnt,
    byte[] Font0002Fnt,
    byte[] Font0004Fnt,
    // The map and travel artwork travels as one file list rather than one parameter per file:
    // sixty-nine decoded files would bury the fixed inputs, and a list keeps the admitted set
    // exactly the files the tool enumerates. The two palettes stay fixed parameters beside the
    // art palette because every map image reads through one of the three.
    IReadOnlyList<MapMediaInput> MapMedia,
    byte[] FmapPalCol,
    byte[] MapPalCol);

/// <summary>Quotas for bounded, deterministic classic-media regeneration.</summary>
public sealed record Arena2ClassicMediaPublicationOptions(
    int MaximumAtlasDimension = 4096,
    long MaximumSourceBytes = 16L * 1024 * 1024,
    long MaximumArtifactBytes = 16L * 1024 * 1024,
    long MaximumTotalArtifactBytes = 64L * 1024 * 1024,
    int AuthoredUiMaximumDimension = 256,
    long AuthoredUiMaximumArtifactBytes = 512L * 1024)
{
    internal void Validate()
    {
        if (MaximumAtlasDimension <= 0
            || MaximumSourceBytes <= 0
            || MaximumArtifactBytes <= 0
            || MaximumTotalArtifactBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumAtlasDimension), "Classic-media publication quotas must be positive.");
        }

        // An authored UI artifact is decoration the DOM paints, not a source plate: it is published at
        // a size a projection can carry, and a source that cannot be brought inside the bound is a
        // refusal rather than a silent multi-megabyte snapshot.
        if (AuthoredUiMaximumDimension <= 0 || AuthoredUiMaximumArtifactBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AuthoredUiMaximumDimension), "Authored UI publication bounds must be positive.");
        }
    }
}

/// <summary>
/// Typed, loaded presentation choices for the admitted classic-media closure.
/// Omitted collections resolve to the compatibility profile; source-format
/// decoding, frame layout, hashes, and dimensions always regenerate.
/// </summary>
public sealed record Arena2ClassicMediaProfile(
    string? WeaponMediaId = null,
    IReadOnlyList<ClassicWeaponActionPresentation>? WeaponActions = null,
    IReadOnlyList<ClassicEffectPresentation>? Effects = null,
    IReadOnlyList<ClassicUiImagePresentation>? UiImages = null,
    IReadOnlyList<ClassicInventoryIconPresentation>? InventoryIcons = null,
    string? FontMediaId = null,
    IReadOnlyList<ClassicMediaPresentation>? Presentation = null,
    ClassicAuthoredUiManifestInput? AuthoredUiManifest = null,
    IReadOnlyList<ClassicAuthoredUiAsset>? AuthoredUiAssets = null,
    IReadOnlyList<AuthoredMediaOverlay>? AuthoredOverlays = null);

/// <summary>Adjustable visual and timing handles for one fixed dagger action record.</summary>
public sealed record ClassicWeaponActionPresentation(
    ClassicDaggerWeaponAction Action,
    ClassicWeaponScreenAlignment Alignment,
    float ScreenOffset,
    ClassicSpriteTiming Timing);

/// <summary>Adjustable semantic naming and playback handles for one fixed TEXTURE.380 effect record.</summary>
public sealed record ClassicEffectPresentation(ClassicEffect Effect, string MediaId, ClassicSpriteTiming Timing);

/// <summary>Adjustable semantic mapping from a UI role to one admitted IMG input.</summary>
public sealed record ClassicUiImagePresentation(ClassicUiImage Image, string MediaId, string SourceFile);

/// <summary>Adjustable inventory item mapping within the admitted classic texture archive closure.</summary>
public sealed record ClassicInventoryIconPresentation(string ItemId, string MediaId, int TextureArchive, int SourceRecordOrdinal);

/// <summary>
/// Optional authored presentation values for one generated or preserved media
/// artifact. These never carry generated atlas layout, dimensions, paths, or
/// digests.
/// </summary>
public sealed record ClassicMediaPresentation(
    string MediaId,
    string? DisplayName = null,
    NormalizedVector2? Pivot = null,
    NormalizedVector2? DisplaySize = null,
    IReadOnlyList<int>? Sequence = null);

/// <summary>
/// A tracked original UI PNG supplied by the caller. SourceLabel and
/// RelativePath are portable logical paths, never local filesystem paths.
/// The import tool may construct these values from its authored-asset manifest.
/// </summary>
public sealed record ClassicAuthoredUiAsset(
    string Id,
    string RelativePath,
    string SourceLabel,
    byte[] PngBytes,
    string Generator,
    string Prompt);

/// <summary>
/// Portable bytes for the tracked authored-UI manifest. The normalizer retains
/// its content address as provenance; a tool interprets it before constructing
/// the typed <see cref="ClassicAuthoredUiAsset"/> records.
/// </summary>
public sealed record ClassicAuthoredUiManifestInput(string SourceLabel, byte[] Bytes);

/// <summary>Provenance and output identity for one preserved authored UI artifact.</summary>
public sealed record ClassicAuthoredUiAssetManifest(
    string Id,
    string RelativePath,
    string SourceLabel,
    string Generator,
    string Prompt);

/// <summary>Semantic actions in the classic dagger CIF action table.</summary>
public enum ClassicDaggerWeaponAction
{
    Idle,
    StrikeDown,
    StrikeDownLeft,
    StrikeLeft,
    StrikeRight,
    StrikeDownRight,
    StrikeUp,
}

/// <summary>Classic screen-side placement preserved as source interpretation, not renderer state.</summary>
public enum ClassicWeaponScreenAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>The fixed semantic effect entries selected from TEXTURE.380.</summary>
public enum ClassicEffect
{
    Blood0,
    Blood1,
    Blood2,
    MagicSparkle,
}

/// <summary>The fixed melee sounds selected from DAGGER.SND.</summary>
public enum ClassicDaggerAudioClip
{
    Swing,
    Hit1,
    Hit2,
    Hit3,
    Hit4,
    Hit5,
}

/// <summary>The classic chrome images preserved by the compact UI pack.</summary>
public enum ClassicUiImage
{
    HudChromeMain,
    HudVitalHealth,
    HudVitalFatigue,
    HudVitalMagicka,
    InventoryChrome,
    CharacterSheetChrome,

    /// <summary>The book reader's page panel (donor <c>DaggerfallBookReaderWindow</c>).</summary>
    BookReader,

    /// <summary>The rest dialog's type panel (donor <c>DaggerfallRestWindow</c>).</summary>
    RestPanel,

    /// <summary>The trade window's haggling cost panel (donor <c>DaggerfallTradeWindow</c>).</summary>
    MerchantCostPanel,

    /// <summary>The guild service popup's base panel (donor <c>DaggerfallGuildServicePopupWindow</c>).</summary>
    GuildServicePanel,

    /// <summary>The guild service popup's member panel, which replaces the base art for a member.</summary>
    GuildMemberPanel,

    /// <summary>The rest dialog's "hours past" counter (donor <c>DaggerfallRestWindow</c>).</summary>
    RestHoursPastPanel,

    /// <summary>The rest dialog's "hours remaining" counter (donor <c>DaggerfallRestWindow</c>).</summary>
    RestHoursRemainingPanel,

    /// <summary>The trade window's buy button bar (donor <c>DaggerfallTradeWindow</c>).</summary>
    MerchantBuyButtons,

    /// <summary>The trade window's sell button bar.</summary>
    MerchantSellButtons,

    /// <summary>The trade window's sell-for-gold button bar.</summary>
    MerchantSellGoldButtons,

    /// <summary>The trade window's repair button bar.</summary>
    MerchantRepairButtons,

    /// <summary>The trade window's identify button bar.</summary>
    MerchantIdentifyButtons,

    /// <summary>The banking window's panel (donor <c>DaggerfallBankingWindow</c>).</summary>
    BankPanel,

    /// <summary>
    /// The screen the product shows when the player dies: a full-screen image that carries its own
    /// palette, unlike the other UI images, which is why the publication asks the source for one.
    /// </summary>
    ScreenDeath,

    /// <summary>The character-generation screen, which carries its own palette.</summary>
    CharacterGenerationScreen,

    /// <summary>The pick screen, which carries its own palette. No donor window this repository has read names its file, so its consumer is unconfirmed.</summary>
    PickScreen02,

    /// <summary>The start window's menu background (donor <c>DaggerfallStartWindow</c>), which carries its own palette.</summary>
    StartMenuScreen,

    /// <summary>The prison screen shown while the player serves time (donor <c>DaggerfallCourtWindow</c>), which carries its own palette.</summary>
    PrisonScreen,

    /// <summary>The title screen, which carries its own palette.</summary>
    TitleScreen,
}

/// <summary>
/// The semantic screen a published UI image fills. A slot is what a consumer binds
/// ("the book reader's page", "the rest dialog"), so it is published beside the
/// media identity rather than inferred from a file name at each call site.
/// </summary>
public enum ClassicUiSlot
{
    HudChrome,
    HudVitalHealth,
    HudVitalFatigue,
    HudVitalMagicka,
    Inventory,
    CharacterSheet,
    Book,
    Rest,
    Merchant,
    Guild,
    Bank,
    Death,

    /// <summary>The character-generation screen.</summary>
    CharacterGeneration,

    /// <summary>The class and background pick screen the player chooses from.</summary>
    Pick,

    /// <summary>The start window's menu background.</summary>
    StartMenu,

    /// <summary>The prison screen shown while the player serves time.</summary>
    Prison,

    /// <summary>The title screen.</summary>
    Title,
}

/// <summary>A discoverable frame cadence and repeat policy; it owns no playback.</summary>
public sealed record ClassicSpriteTiming(float FramesPerSecond, bool Loop)
{
    internal void Validate()
    {
        if (!float.IsFinite(FramesPerSecond) || FramesPerSecond <= 0F)
        {
            throw new ArgumentOutOfRangeException(nameof(FramesPerSecond), "Classic sprite timing must be finite and positive.");
        }
    }
}

/// <summary>One contiguous action range within the regenerated dagger atlas.</summary>
public sealed record ClassicWeaponActionManifest(
    ClassicDaggerWeaponAction Action,
    int SourceRecordOrdinal,
    int FrameStart,
    int FrameCount,
    ClassicWeaponScreenAlignment Alignment,
    float ScreenOffset,
    ClassicSpriteTiming Timing,
    short SourceXOffset,
    short SourceYOffset,
    IReadOnlyList<int>? Sequence = null)
{
    internal void Validate(int totalFrames)
    {
        if (!Enum.IsDefined(Action) || !Enum.IsDefined(Alignment)
            || FrameStart < 0 || FrameCount <= 0 || FrameStart > totalFrames - FrameCount
            || !float.IsFinite(ScreenOffset))
        {
            throw new ArgumentOutOfRangeException(nameof(FrameStart), "Classic weapon action facts are invalid.");
        }

        ArgumentNullException.ThrowIfNull(Timing);
        Timing.Validate();
        if (Sequence is { Count: 0 } || Sequence?.Any(frame => frame < 0 || frame >= totalFrames) == true)
        {
            throw new ArgumentOutOfRangeException(nameof(Sequence), "Classic weapon action sequences must identify generated atlas frames.");
        }
    }
}

/// <summary>One generated first-person weapon resource and its source-normalized actions.</summary>
public sealed record ClassicWeaponMediaManifest(string ResourceId, IReadOnlyList<ClassicWeaponActionManifest> Actions);

/// <summary>Typed source interpretation for a selected TEXTURE.380 effect atlas.</summary>
public sealed record ClassicEffectManifest(ClassicEffect Effect, string MediaId, int SourceRecordOrdinal, ClassicSpriteTiming Timing)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(Effect) || SourceRecordOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SourceRecordOrdinal));
        }

        NormalizedImportDocument.RequireLogicalId(MediaId, nameof(MediaId));
        ArgumentNullException.ThrowIfNull(Timing);
        Timing.Validate();
    }
}

/// <summary>Source identity retained for one offline WAV emission.</summary>
public sealed record ClassicAudioManifest(ClassicDaggerAudioClip Clip, string MediaId, int SourceRecordOrdinal, uint SourceNumericId, uint SampleRate)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(Clip) || SourceRecordOrdinal < 0 || SampleRate != SoundArchive.SampleRate)
        {
            throw new ArgumentOutOfRangeException(nameof(SourceRecordOrdinal));
        }

        NormalizedImportDocument.RequireLogicalId(MediaId, nameof(MediaId));
    }
}

/// <summary>Classic IMG source facts for a regenerated UI PNG.</summary>
public sealed record ClassicUiImageManifest(
    ClassicUiImage Image,
    string MediaId,
    string SourceFile,
    short SourceXOffset,
    short SourceYOffset,
    bool IsHeaderless,
    bool OwnEmbeddedPalette = false)
{
    /// <summary>
    /// The semantic screen this image fills. It is derived rather than configured, because a role and
    /// its slot disagreeing would publish an image no consumer could bind to the screen it feeds.
    /// </summary>
    public ClassicUiSlot Slot => SlotOf(Image);

    internal static ClassicUiSlot SlotOf(ClassicUiImage image) => image switch
    {
        ClassicUiImage.HudChromeMain => ClassicUiSlot.HudChrome,
        ClassicUiImage.HudVitalHealth => ClassicUiSlot.HudVitalHealth,
        ClassicUiImage.HudVitalFatigue => ClassicUiSlot.HudVitalFatigue,
        ClassicUiImage.HudVitalMagicka => ClassicUiSlot.HudVitalMagicka,
        ClassicUiImage.InventoryChrome => ClassicUiSlot.Inventory,
        ClassicUiImage.CharacterSheetChrome => ClassicUiSlot.CharacterSheet,
        ClassicUiImage.BookReader => ClassicUiSlot.Book,
        ClassicUiImage.RestPanel => ClassicUiSlot.Rest,
        ClassicUiImage.MerchantCostPanel => ClassicUiSlot.Merchant,
        ClassicUiImage.GuildServicePanel => ClassicUiSlot.Guild,
        ClassicUiImage.GuildMemberPanel => ClassicUiSlot.Guild,
        ClassicUiImage.BankPanel => ClassicUiSlot.Bank,
        ClassicUiImage.RestHoursPastPanel => ClassicUiSlot.Rest,
        ClassicUiImage.RestHoursRemainingPanel => ClassicUiSlot.Rest,
        ClassicUiImage.MerchantBuyButtons => ClassicUiSlot.Merchant,
        ClassicUiImage.MerchantSellButtons => ClassicUiSlot.Merchant,
        ClassicUiImage.MerchantSellGoldButtons => ClassicUiSlot.Merchant,
        ClassicUiImage.MerchantRepairButtons => ClassicUiSlot.Merchant,
        ClassicUiImage.MerchantIdentifyButtons => ClassicUiSlot.Merchant,
        ClassicUiImage.ScreenDeath => ClassicUiSlot.Death,
        ClassicUiImage.CharacterGenerationScreen => ClassicUiSlot.CharacterGeneration,
        ClassicUiImage.PickScreen02 => ClassicUiSlot.Pick,
        ClassicUiImage.StartMenuScreen => ClassicUiSlot.StartMenu,
        ClassicUiImage.PrisonScreen => ClassicUiSlot.Prison,
        ClassicUiImage.TitleScreen => ClassicUiSlot.Title,
        _ => throw new ArgumentOutOfRangeException(nameof(image), image, "An admitted UI image with no slot is an artifact no consumer can bind."),
    };

    internal void Validate()
    {
        if (!Enum.IsDefined(Image))
        {
            throw new ArgumentOutOfRangeException(nameof(Image));
        }

        NormalizedImportDocument.RequireLogicalId(MediaId, nameof(MediaId));
        NormalizedImportDocument.RequireLogicalPath(SourceFile, nameof(SourceFile));
    }
}

/// <summary>One exact Daggerfall Unity donor mapping for an admitted inventory icon.</summary>
public sealed record ClassicInventoryIconManifest(string ItemId, string MediaId, int TextureArchive, int SourceRecordOrdinal)
{
    internal void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(ItemId, nameof(ItemId));
        NormalizedImportDocument.RequireLogicalId(MediaId, nameof(MediaId));
        if (TextureArchive < 0 || SourceRecordOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TextureArchive));
        }
    }
}

/// <summary>One glyph's regenerated atlas cell and source advance metric.</summary>
public sealed record ClassicFontGlyphMetric(int GlyphIndex, int X, int Y, ushort Advance, ushort SourceDataOffset);

/// <summary>One supplied map art file: the name the inventory documents and its bytes.</summary>
/// <param name="FileName">The file's name.</param>
/// <param name="Bytes">The file's bytes.</param>
public sealed record MapMediaInput(string FileName, byte[] Bytes);

/// <summary>One published map or travel image: its identity, shape and source binding.</summary>
/// <param name="MediaId">The published media identity.</param>
/// <param name="Kind">The map art family the file belongs to.</param>
/// <param name="Regions">The regions the image draws, empty when it draws none.</param>
/// <param name="Binding">The donor call site, or the reason no call site was found.</param>
/// <param name="SourceFile">The supplied file.</param>
/// <param name="Width">The image width.</param>
/// <param name="Height">The image height.</param>
/// <param name="Cutout">Whether the donor draws the image with index-zero cutout rather than opaque.</param>
public sealed record ClassicMapMediaManifest(
    string MediaId,
    MapArtKind Kind,
    IReadOnlyList<int> Regions,
    string Binding,
    string SourceFile,
    int Width,
    int Height,
    bool Cutout)
{
    internal void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(MediaId, nameof(MediaId));
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "A published map image names a family the contract does not declare.");
        }

        NormalizedImportDocument.RequireLogicalPath(SourceFile, nameof(SourceFile));
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), Width, $"Map image '{MediaId}' states no shape.");
        }

        foreach (int region in Regions)
        {
            if (region is < 0 or > 61)
            {
                throw new ArgumentOutOfRangeException(nameof(Regions), region, $"Map image '{MediaId}' draws no classic region.");
            }
        }
    }
}

/// <summary>One classic region with the map images that draw it.</summary>
/// <param name="Region">The zero-based region.</param>
/// <param name="MediaIds">The published media identities that draw it, empty when no art does.</param>
public sealed record ClassicMapRegionManifest(int Region, IReadOnlyList<string> MediaIds)
{
    internal void Validate()
    {
        if (Region is < 0 or > 61)
        {
            throw new ArgumentOutOfRangeException(nameof(Region), Region, "A published map region is outside the 62 classic regions.");
        }

        foreach (string mediaId in MediaIds)
        {
            NormalizedImportDocument.RequireLogicalId(mediaId, nameof(mediaId));
        }
    }
}

/// <summary>Typed, renderer-free classic glyph metrics and generated atlas identity.</summary>
public sealed record ClassicFontManifest(
    string MediaId,
    string Use,
    ushort FixedWidth,
    ushort FixedHeight,
    IReadOnlyList<ClassicFontGlyphMetric> Glyphs)
{
    internal void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(MediaId, nameof(MediaId));
        if (string.IsNullOrWhiteSpace(Use))
        {
            throw new ArgumentException($"Classic font '{MediaId}' states no consumer use.", nameof(Use));
        }

        if (FixedWidth is 0 or > 16 || FixedHeight is 0 or > 16 || Glyphs is null || Glyphs.Count != Arena2FormatConstants.FntGlyphCount)
        {
            throw new ArgumentOutOfRangeException(nameof(Glyphs), "Classic font metrics must describe all 240 16px glyphs.");
        }

        if (Glyphs.Select(glyph => glyph.GlyphIndex).Distinct().Count() != Glyphs.Count
            || Glyphs.Any(glyph => glyph.GlyphIndex < 0 || glyph.GlyphIndex >= Glyphs.Count || glyph.X < 0 || glyph.Y < 0 || glyph.Advance > 16))
        {
            throw new InvalidOperationException("Classic font glyph metrics are malformed.");
        }
    }
}

/// <summary>
/// The published byte closure plus typed semantic metadata. The caller may
/// place <see cref="Artifacts"/> directly in an <see cref="ImportPublicationPlan"/>;
/// no filesystem operation occurs here.
/// </summary>
public sealed record Arena2ClassicMediaPublication(
    IReadOnlyList<ImportPublicationArtifact> Artifacts,
    NormalizedMediaManifest MediaManifest,
    IReadOnlyList<LogicalSourceRecord> Sources,
    LogicalSourceRecord? AuthoredUiManifestSource,
    IReadOnlyList<ClassicWeaponMediaManifest> WeaponMedia,
    IReadOnlyList<ClassicEffectManifest> Effects,
    IReadOnlyList<ClassicAudioManifest> Audio,
    IReadOnlyList<ClassicUiImageManifest> UiImages,
    IReadOnlyList<ClassicInventoryIconManifest> InventoryIcons,
    ClassicFontManifest Font,
    IReadOnlyList<ClassicFontManifest> Fonts,
    IReadOnlyList<ClassicMapMediaManifest> MapMedia,
    IReadOnlyList<ClassicMapRegionManifest> MapRegions,
    IReadOnlyList<ClassicAuthoredUiAssetManifest> AuthoredUiAssets)
{
    /// <summary>The logical source path the numeric sound archive is read under, in this publication and by any catalog of it.</summary>
    public const string DaggerSoundSourcePath = "arena2/DAGGER.SND";

    private const int WeaponReferenceWidth = 320;
    private const int WeaponReferenceHeight = 200;
    private const int FontCellSize = 16;

    private static readonly WeaponActionSource[] DaggerWeaponActionSources =
    [
        new(ClassicDaggerWeaponAction.Idle, 0, ClassicWeaponScreenAlignment.Right, 0.04F, true, 1),
        new(ClassicDaggerWeaponAction.StrikeDown, 1, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeDownLeft, 2, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeLeft, 3, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeRight, 4, ClassicWeaponScreenAlignment.Left, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeDownRight, 5, ClassicWeaponScreenAlignment.Left, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeUp, 6, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
    ];

    private static readonly WeaponActionSource[] GeneralWeaponActionSources =
    [
        new(ClassicDaggerWeaponAction.Idle, 0, ClassicWeaponScreenAlignment.Right, 0F, true, 1),
        new(ClassicDaggerWeaponAction.StrikeDown, 1, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeDownLeft, 2, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeLeft, 3, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeRight, 4, ClassicWeaponScreenAlignment.Left, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeDownRight, 5, ClassicWeaponScreenAlignment.Left, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeUp, 6, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
    ];

    private static readonly WeaponActionSource[] StaffWeaponActionSources =
    [
        new(ClassicDaggerWeaponAction.Idle, 0, ClassicWeaponScreenAlignment.Right, 0.02F, true, 1),
        new(ClassicDaggerWeaponAction.StrikeDown, 1, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeDownLeft, 2, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeLeft, 3, ClassicWeaponScreenAlignment.Center, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeRight, 4, ClassicWeaponScreenAlignment.Center, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeDownRight, 5, ClassicWeaponScreenAlignment.Left, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeUp, 6, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
    ];

    private static readonly WeaponActionSource[] UnarmedWeaponActionSources =
    [
        new(ClassicDaggerWeaponAction.Idle, 0, ClassicWeaponScreenAlignment.Center, 0.15F, true, 1),
        new(ClassicDaggerWeaponAction.StrikeDown, 1, ClassicWeaponScreenAlignment.Center, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeDownLeft, 2, ClassicWeaponScreenAlignment.Center, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeLeft, 3, ClassicWeaponScreenAlignment.Center, 0F, false, 5, [0, 1, 2, 3, 4, 2, 1, 0]),
        new(ClassicDaggerWeaponAction.StrikeRight, 4, ClassicWeaponScreenAlignment.Center, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeDownRight, 5, ClassicWeaponScreenAlignment.Left, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeUp, 6, ClassicWeaponScreenAlignment.Left, 0F, false, 5),
    ];

    /// <summary>
    /// The donor's werecreature strikes, in its alignment changes. The archive carries a wield image
    /// plus six five-frame records exactly like the general weapons; only the alignments and offsets
    /// differ, which is why this table exists separately rather than reusing the general one.
    /// </summary>
    private static readonly WeaponActionSource[] WerecreatureWeaponActionSources =
    [
        new(ClassicDaggerWeaponAction.Idle, 0, ClassicWeaponScreenAlignment.Center, 0.02F, true, 1),
        new(ClassicDaggerWeaponAction.StrikeDown, 1, ClassicWeaponScreenAlignment.Right, 0.2F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeDownLeft, 2, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeLeft, 3, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeRight, 4, ClassicWeaponScreenAlignment.Right, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeDownRight, 5, ClassicWeaponScreenAlignment.Left, 0F, false, 5),
        new(ClassicDaggerWeaponAction.StrikeUp, 6, ClassicWeaponScreenAlignment.Left, 0.2F, false, 5),
    ];

    private static readonly WeaponActionSource[] BowWeaponActionSources =
    [
        new(ClassicDaggerWeaponAction.Idle, 0, ClassicWeaponScreenAlignment.Right, 0F, true, 1),
        new(ClassicDaggerWeaponAction.StrikeDown, 0, ClassicWeaponScreenAlignment.Right, 0F, false, 7),
        new(ClassicDaggerWeaponAction.StrikeDownLeft, 0, ClassicWeaponScreenAlignment.Right, 0F, false, 7),
        new(ClassicDaggerWeaponAction.StrikeLeft, 0, ClassicWeaponScreenAlignment.Right, 0F, false, 7),
        new(ClassicDaggerWeaponAction.StrikeRight, 0, ClassicWeaponScreenAlignment.Right, 0F, false, 7),
        new(ClassicDaggerWeaponAction.StrikeDownRight, 0, ClassicWeaponScreenAlignment.Right, 0F, false, 7),
        new(ClassicDaggerWeaponAction.StrikeUp, 0, ClassicWeaponScreenAlignment.Right, 0F, false, 4),
    ];

    /// <summary>
    /// Every font table the corpus carries, in file order, with the donor's consumer for each: the
    /// large, title, small and default faces the interface reads, and the fifth table the donor
    /// loads but no interface selects, which keeps its binding explicitly as unused.
    /// </summary>
    private static readonly FontSource[] FontSources =
    [
        new("FONT0000.FNT", "font.classic.0000", "large"),
        new("FONT0001.FNT", "font.classic.0001", "title"),
        new("FONT0002.FNT", "font.classic.0002", "small"),
        new("FONT0003.FNT", "font.classic.0003", "default"),
        new("FONT0004.FNT", "font.classic.0004", "unused"),
    ];

    private static byte[] RequireFont(SourceBytes source, string fileName) => fileName switch
    {
        "FONT0000.FNT" => source.Font0000Fnt,
        "FONT0001.FNT" => source.Font0001Fnt,
        "FONT0002.FNT" => source.Font0002Fnt,
        "FONT0003.FNT" => source.Font0003Fnt,
        "FONT0004.FNT" => source.Font0004Fnt,
        _ => throw new ArgumentOutOfRangeException(nameof(fileName), "The requested source is not an admitted classic font table."),
    };

    /// <summary>
    /// Every weapon archive the corpus carries, in file order. The donor names a consumer for nine of
    /// them through its weapon-type mapping plus the werecreature form; WEAPON00.CIF and WEAPON03.CIF
    /// are longblade-shaped archives no donor reader addresses, so they follow the general action
    /// layout their record and frame counts match and keep file-derived identities no item references.
    /// </summary>
    private static readonly WeaponMediaSource[] WeaponMediaSources =
    [
        new("WEAPON00.CIF", "weapon.00", GeneralWeaponActionSources),
        new("WEAPON01.CIF", "weapon.staff", StaffWeaponActionSources),
        new("WEAPON02.CIF", "weapon.dagger.steel", DaggerWeaponActionSources),
        new("WEAPON03.CIF", "weapon.03", GeneralWeaponActionSources),
        new("WEAPON04.CIF", "weapon.longblade", GeneralWeaponActionSources),
        new("WEAPON05.CIF", "weapon.mace", GeneralWeaponActionSources),
        new("WEAPON06.CIF", "weapon.flail", GeneralWeaponActionSources),
        new("WEAPON07.CIF", "weapon.warhammer", GeneralWeaponActionSources),
        new("WEAPON08.CIF", "weapon.axe", GeneralWeaponActionSources),
        new("WEAPON09.CIF", "weapon.bow", BowWeaponActionSources),
        new("WEAPON10.CIF", "weapon.unarmed", UnarmedWeaponActionSources),
        new("WEAPON11.CIF", "weapon.werecreature", WerecreatureWeaponActionSources),
    ];

    internal static void ValidateCanonicalWeaponAction(string resourceId, ClassicWeaponActionManifest action)
    {
        WeaponMediaSource weapon = resourceId.StartsWith("weapon.dagger", StringComparison.Ordinal)
            ? WeaponMediaSources.Single(source => source.ResourceId == "weapon.dagger.steel")
            : WeaponMediaSources.SingleOrDefault(source => source.ResourceId == resourceId)
                ?? throw new InvalidOperationException($"Classic weapon resource '{resourceId}' is not in the admitted source closure.");
        WeaponActionSource expected = weapon.Actions.Single(source => source.Action == action.Action);
        if (action.SourceRecordOrdinal != expected.SourceRecordOrdinal || action.FrameCount != expected.FrameCount
            || !SequencesEqual(action.FrameStart, action.Sequence, expected.Sequence))
        {
            throw new InvalidOperationException("Classic weapon action facts differ from the admitted Arena2 source interpretation.");
        }
    }

    internal static bool IsAdmittedWeaponResource(string resourceId) =>
        resourceId.StartsWith("weapon.dagger", StringComparison.Ordinal)
        || WeaponMediaSources.Any(source => source.ResourceId == resourceId);

    private static bool SequencesEqual(int frameStart, IReadOnlyList<int>? actual, IReadOnlyList<int>? relativeExpected)
    {
        if (relativeExpected is null) return actual is null;
        return actual is { } sequence && sequence.Count == relativeExpected.Count
            && sequence.SequenceEqual(relativeExpected.Select(frame => checked(frameStart + frame)));
    }

    private static readonly EffectSource[] EffectSources =
    [
        new(ClassicEffect.Blood0, "effect.blood.0", 0),
        new(ClassicEffect.Blood1, "effect.blood.1", 1),
        new(ClassicEffect.Blood2, "effect.blood.2", 2),
        new(ClassicEffect.MagicSparkle, "effect.sparkle.magic", 3),
    ];

    /// <summary>Returns the fixed TEXTURE.380 record retained by an effect's canonical source identity.</summary>
    internal static int ExpectedEffectSourceRecordOrdinal(ClassicEffect effect) => EffectSources
        .SingleOrDefault(source => source.Effect == effect)?.SourceRecordOrdinal
        ?? throw new ArgumentOutOfRangeException(nameof(effect), "The effect is not part of the fixed classic source closure.");

    private static readonly AudioSource[] AudioSources =
    [
        new(ClassicDaggerAudioClip.Swing, "audio.melee.dagger.swing", 106),
        new(ClassicDaggerAudioClip.Hit1, "audio.melee.hit.1", 108),
        new(ClassicDaggerAudioClip.Hit2, "audio.melee.hit.2", 109),
        new(ClassicDaggerAudioClip.Hit3, "audio.melee.hit.3", 110),
        new(ClassicDaggerAudioClip.Hit4, "audio.melee.hit.4", 111),
        new(ClassicDaggerAudioClip.Hit5, "audio.melee.hit.5", 112),
    ];

    private static readonly UiImageSource[] UiImageSources =
    [
        new(ClassicUiImage.HudChromeMain, "hud.chrome.main", "MAIN00I0.IMG", false),
        new(ClassicUiImage.HudVitalHealth, "hud.vital.health", "MAIN03I0.IMG", false),
        new(ClassicUiImage.HudVitalFatigue, "hud.vital.fatigue", "MAIN04I0.IMG", false),
        new(ClassicUiImage.HudVitalMagicka, "hud.vital.magicka", "MAIN05I0.IMG", false),
        new(ClassicUiImage.InventoryChrome, "window.inventory.chrome", "INVE00I0.IMG", true),
        new(ClassicUiImage.CharacterSheetChrome, "window.character-sheet.chrome", "INFO00I0.IMG", true),

        // The one screen named so far is a mode's rather than a window's: the product shows it when the
        // player dies, and nothing named it before because no consumer had asked for it.
        new(ClassicUiImage.ScreenDeath, "screen.death", "DIE_00I0.IMG", true),
        // These five carry a 768-byte palette after the canvas like the death screen does, so each is
        // published in its own colours rather than paired with an external palette.
        new(ClassicUiImage.CharacterGenerationScreen, "screen.character-generation", "CHGN00I0.IMG", true),
        new(ClassicUiImage.PickScreen02, "screen.pick.02", "PICK02I0.IMG", true),
        new(ClassicUiImage.StartMenuScreen, "screen.start-menu", "PICK03I0.IMG", true),
        new(ClassicUiImage.PrisonScreen, "screen.prison", "PRIS00I0.IMG", true),
        new(ClassicUiImage.TitleScreen, "screen.title", "TITL00I0.IMG", true),

        // Service screens and panels, each named by the donor window that reads it: the book reader's
        // page, the rest dialog's type panel, the trade window's cost panel, the guild service popup
        // and the banking window.
        new(ClassicUiImage.BookReader, "window.book.reader", "BOOK00I0.IMG", false),
        new(ClassicUiImage.RestPanel, "window.rest.panel", "REST00I0.IMG", false),
        new(ClassicUiImage.MerchantCostPanel, "window.merchant.cost", "SHOP00I0.IMG", false),
        new(ClassicUiImage.GuildServicePanel, "window.guild.service", "GILD00I0.IMG", false),
        new(ClassicUiImage.BankPanel, "window.bank.panel", "BANK00I0.IMG", false),

        // A slot names a screen; a screen the donor composes from several images publishes each part
        // under its own media identity, because the panel and its counters are not interchangeable.
        new(ClassicUiImage.RestHoursPastPanel, "window.rest.hours-past", "REST01I0.IMG", false),
        new(ClassicUiImage.RestHoursRemainingPanel, "window.rest.hours-remaining", "REST02I0.IMG", false),
        new(ClassicUiImage.MerchantBuyButtons, "window.merchant.buttons.buy", "INVE08I0.IMG", false),
        new(ClassicUiImage.MerchantSellButtons, "window.merchant.buttons.sell", "INVE10I0.IMG", false),
        new(ClassicUiImage.MerchantSellGoldButtons, "window.merchant.buttons.sell-gold", "INVE11I0.IMG", false),
        new(ClassicUiImage.MerchantRepairButtons, "window.merchant.buttons.repair", "INVE12I0.IMG", false),
        new(ClassicUiImage.MerchantIdentifyButtons, "window.merchant.buttons.identify", "INVE14I0.IMG", false),
        new(ClassicUiImage.GuildMemberPanel, "window.guild.member", "GILD01I0.IMG", false),
    ];

    private static readonly InventoryIconSource[] InventoryIconSources =
    [
        new("iron-dagger", 234, 5), new("iron-tanto", 234, 22), new("iron-wakazashi", 234, 26),
        new("iron-shortsword", 234, 19), new("iron-broadsword", 234, 2), new("iron-saber", 234, 17),
        new("iron-katana", 234, 10), new("iron-longsword", 234, 12), new("iron-mace", 234, 14),
        new("iron-battle-axe", 234, 0), new("iron-claymore", 234, 4), new("iron-dai-katana", 234, 7),
        new("iron-staff", 234, 21), new("iron-flail", 234, 8), new("iron-warhammer", 234, 25),
        new("iron-war-axe", 234, 24), new("iron-short-bow", 234, 16), new("iron-long-bow", 234, 11),
        new("iron-helm", 245, 27), new("iron-cuirass", 245, 3), new("iron-right-pauldron", 245, 22),
        new("iron-left-pauldron", 245, 17), new("iron-gauntlets", 245, 8), new("iron-greaves", 245, 10),
        new("iron-boots", 245, 0), new("buckler", 245, 33), new("round-shield", 245, 34),
        new("kite-shield", 245, 35), new("tower-shield", 245, 36), new("gold-piece", 216, 1), new("arrow", 207, 16),
    ];

    /// <summary>
    /// The clips this publication emits, stated in the sound catalog's own admission vocabulary. The
    /// catalog an importer publishes beside these artifacts is built from this list, so its admitted
    /// entries name artifacts this publication actually produced rather than a second table kept in
    /// agreement by hand.
    /// </summary>
    public IReadOnlyList<DaggerfallSoundAdmission> SoundAdmissions =>
        [.. Audio.Select(audio => new DaggerfallSoundAdmission(audio.SourceRecordOrdinal, audio.MediaId))];

    /// <summary>Regenerates selected classic art with the compatibility presentation profile.</summary>
    public static Arena2ClassicMediaPublication Create(Arena2ClassicMediaInputs inputs, Arena2ClassicMediaPublicationOptions? options = null) =>
        Create(inputs, new Arena2ClassicMediaProfile(), options);

    /// <summary>
    /// Regenerates selected classic art from caller-owned Arena2 bytes and an
    /// explicit typed presentation profile. This method performs no filesystem
    /// access; authored PNGs are supplied in <paramref name="profile"/>.
    /// </summary>
    public static Arena2ClassicMediaPublication Create(
        Arena2ClassicMediaInputs inputs,
        Arena2ClassicMediaProfile profile,
        Arena2ClassicMediaPublicationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(profile);
        Arena2ClassicMediaPublicationOptions effectiveOptions = options ?? new();
        effectiveOptions.Validate();
        if (effectiveOptions.MaximumAtlasDimension < WeaponReferenceWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The classic dagger atlas needs at least a 320px atlas dimension.");
        }

        ResolvedProfile resolved = ApplyAuthoredEffectTiming(ResolveProfile(profile), profile.AuthoredOverlays);
        SourceBytes source = SourceBytes.From(inputs, effectiveOptions.MaximumSourceBytes);
        Arena2Palette artPalette = PaletteDecoder.Decode(source.ArtPalette, "arena2/ART_PAL.COL");
        Arena2Palette palette = PaletteDecoder.Decode(source.Palette, "arena2/PAL.PAL");
        List<GeneratedMediaArtifact> generated = [];

        List<ClassicWeaponMediaManifest> weaponMedia = [];
        foreach (WeaponMediaSource weaponSource in WeaponMediaSources)
        {
            string resourceId = weaponSource.ResourceId == "weapon.dagger.steel" ? resolved.WeaponMediaId : weaponSource.ResourceId;
            IReadOnlyDictionary<ClassicDaggerWeaponAction, ClassicWeaponActionPresentation>? profileActions = weaponSource.ResourceId == "weapon.dagger.steel"
                ? resolved.WeaponActions
                : null;
            (GeneratedMediaArtifact weapon, ClassicWeaponActionManifest[] actions) = BuildWeapon(
                source.RequireWeapon(weaponSource.FileName),
                $"arena2/{weaponSource.FileName}",
                resourceId,
                weaponSource.Actions,
                profileActions,
                artPalette,
                effectiveOptions);
            generated.Add(weapon);
            weaponMedia.Add(new(resourceId, actions));
        }
        generated.AddRange(BuildEffects(source.Texture380, palette, resolved, effectiveOptions, out ClassicEffectManifest[] effects));
        generated.AddRange(BuildAudio(source.DaggerSound, effectiveOptions, out ClassicAudioManifest[] audio));
        generated.AddRange(BuildUi(source, artPalette, resolved, effectiveOptions, out ClassicUiImageManifest[] uiImages));
        generated.AddRange(BuildInventoryIcons(source, artPalette, resolved, effectiveOptions, out ClassicInventoryIconManifest[] inventoryIcons));
        List<ClassicFontManifest> fonts = [];
        foreach (FontSource fontSource in FontSources)
        {
            (GeneratedMediaArtifact fontArtifact, ClassicFontManifest font) = BuildFont(
                RequireFont(source, fontSource.FileName), fontSource.FileName, fontSource.MediaId, fontSource.Use, effectiveOptions);
            generated.Add(fontArtifact);
            fonts.Add(font);
        }

        ClassicFontManifest defaultFont = fonts.SingleOrDefault(font => StringComparer.Ordinal.Equals(font.MediaId, resolved.FontMediaId))
            ?? throw new InvalidOperationException($"The default font '{resolved.FontMediaId}' is not an admitted classic font.");
        List<ClassicMapMediaManifest> mapMedia = [];
        List<ClassicMapRegionManifest> mapRegions = [];
        generated.AddRange(BuildMapMedia(source, artPalette, effectiveOptions, out ClassicMapMediaManifest[] maps, out ClassicMapRegionManifest[] regionMaps));
        mapMedia.AddRange(maps);
        mapRegions.AddRange(regionMaps);
        generated.AddRange(BuildAuthoredUi(resolved, effectiveOptions, out ClassicAuthoredUiAssetManifest[] authoredUiAssets));

        if (generated.Sum(artifact => artifact.Bytes.LongLength) > effectiveOptions.MaximumTotalArtifactBytes)
        {
            throw new InvalidOperationException("Classic media closure exceeds the total encoded-byte quota.");
        }

        NormalizedMediaManifest mediaManifest = MediaManifestNormalizer.Normalize(
            generated,
            BuildOverlays(resolved, profile.AuthoredOverlays),
            effectiveOptions.MaximumArtifactBytes);
        ImportPublicationArtifact[] artifacts = generated
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact => new ImportPublicationArtifact(artifact.RelativePath, artifact.Bytes, mediaId: artifact.Id))
            .ToArray();
        LogicalSourceRecord? authoredManifestSource = CreateAuthoredManifestSource(resolved.AuthoredUiManifest, effectiveOptions.MaximumSourceBytes);
        return new(
            artifacts,
            mediaManifest,
            MergeSources(source.LogicalSources, resolved.AuthoredUiAssets, authoredManifestSource),
            authoredManifestSource,
            weaponMedia,
            effects,
            audio,
            uiImages,
            inventoryIcons,
            defaultFont,
            fonts,
            mapMedia,
            mapRegions,
            authoredUiAssets);
    }

    private static IEnumerable<GeneratedMediaArtifact> BuildMapMedia(
        SourceBytes source,
        Arena2Palette artPalette,
        Arena2ClassicMediaPublicationOptions options,
        out ClassicMapMediaManifest[] manifests,
        out ClassicMapRegionManifest[] regions)
    {
        Arena2Palette fmapPalette = PaletteDecoder.Decode(source.FmapPalCol, "arena2/FMAP_PAL.COL");
        Arena2Palette mapPalette = PaletteDecoder.Decode(source.MapPalCol, "arena2/MAP.PAL");
        MapArtInventory inventory = MapArtInventory.Enumerate(
            [.. source.MapMedia.Select(file => (file.FileName, $"arena2/{file.FileName}", file.Bytes))],
            "arena2");

        List<GeneratedMediaArtifact> result = [];
        List<ClassicMapMediaManifest> semantic = [];
        foreach (MapArtRecord record in inventory.Records.Where(record => record.Disposition == MapArtDisposition.Decoded))
        {
            byte[] bytes = source.MapMedia.Single(file => StringComparer.Ordinal.Equals(file.FileName, record.FileName)).Bytes;
            IndexedImg image = MapArtInventory.DecodeImage(bytes, $"arena2/{record.FileName}");
            Arena2Palette palette = record.Palette switch
            {
                "FMAP_PAL.COL" => fmapPalette,
                "MAP.PAL" => mapPalette,
                _ => artPalette,
            };
            // Most map canvases are opaque: the donor draws region maps and travel chrome without
            // cutout. Five images load through its cutout path instead, so index zero punches
            // through for exactly those: the automap pair and town caption in the automap windows,
            // the race-select world map, and the travel popup.
            PaletteAlphaMode alpha = CutoutMapFiles.Contains(record.FileName) ? PaletteAlphaMode.IndexZeroTransparent : PaletteAlphaMode.Opaque;
            Rgba32[] colors = palette.ToRgba(image.Pixels.Span, alpha);
            byte[] rgba = new byte[checked(colors.Length * 4)];
            for (int index = 0; index < colors.Length; index++)
            {
                int target = index * 4;
                rgba[target] = colors[index].Red;
                rgba[target + 1] = colors[index].Green;
                rgba[target + 2] = colors[index].Blue;
                rgba[target + 3] = colors[index].Alpha;
            }

            string mediaId = $"map.{Path.GetFileNameWithoutExtension(record.FileName).ToLowerInvariant()}";
            byte[] png = DeterministicPngEncoder.EncodeRgba8(image.Width, image.Height, rgba);
            RequireArtifactQuota(png, options, mediaId);
            result.Add(new(mediaId, NormalizedMediaKind.UserInterface, $"media/maps/{Slug(mediaId)}.png", png, image.Width, image.Height, null, "image/png"));
            ClassicMapMediaManifest manifest = new(mediaId, record.Kind, record.Regions, record.Binding, $"arena2/{record.FileName}", image.Width, image.Height, alpha == PaletteAlphaMode.IndexZeroTransparent);
            manifest.Validate();
            semantic.Add(manifest);
        }

        List<ClassicMapRegionManifest> regionMaps = [];
        for (int region = 0; region < 62; region++)
        {
            int claimed = region;
            ClassicMapRegionManifest regionMap = new(region, [.. semantic.Where(image => image.Regions.Contains(claimed)).Select(image => image.MediaId).OrderBy(id => id, StringComparer.Ordinal)]);
            regionMap.Validate();
            regionMaps.Add(regionMap);
        }

        manifests = [.. semantic.OrderBy(image => image.MediaId, StringComparer.Ordinal)];
        regions = [.. regionMaps];
        return result;
    }

    private static (GeneratedMediaArtifact Artifact, ClassicWeaponActionManifest[] Actions) BuildWeapon(
        ReadOnlySpan<byte> weaponBytes,
        string sourceName,
        string resourceId,
        IReadOnlyList<WeaponActionSource> actionSources,
        IReadOnlyDictionary<ClassicDaggerWeaponAction, ClassicWeaponActionPresentation>? profileActions,
        Arena2Palette palette,
        Arena2ClassicMediaPublicationOptions options)
    {
        WeaponCifArchive weapon = WeaponCifArchive.Parse(weaponBytes, sourceName);

        List<DecodedSpriteFrame> frames = [];
        List<ClassicWeaponActionManifest> actions = [];
        Dictionary<string, int> frameStarts = new(StringComparer.Ordinal);
        foreach (WeaponActionSource sourceMapping in actionSources)
        {
            WeaponCifRecordInfo info = weapon.GetRecordInfo(sourceMapping.SourceRecordOrdinal);
            IReadOnlyList<int> frameSourceRecords = sourceMapping.FrameSourceRecords ?? [sourceMapping.SourceRecordOrdinal];
            int availableFrames = frameSourceRecords.Sum(record => weapon.GetRecordInfo(record).FrameCount);
            if (availableFrames < sourceMapping.FrameCount)
            {
                throw new Arena2FormatException(sourceName, 0, $"weapon action '{sourceMapping.Action}' requires {sourceMapping.FrameCount} frames, got {availableFrames}");
            }

            ClassicWeaponActionPresentation mapping = profileActions?.GetValueOrDefault(sourceMapping.Action)
                ?? new(sourceMapping.Action, sourceMapping.Alignment, sourceMapping.ScreenOffset, new ClassicSpriteTiming(10F, sourceMapping.Loop));
            string frameSourceKey = string.Join(',', frameSourceRecords);
            if (!frameStarts.TryGetValue(frameSourceKey, out int start))
            {
                start = frames.Count;
                foreach (int sourceRecord in frameSourceRecords)
                {
                    WeaponCifRecordInfo frameInfo = weapon.GetRecordInfo(sourceRecord);
                    for (int frame = 0; frame < frameInfo.FrameCount; frame++)
                    {
                        IndexedWeaponCifFrame decoded = weapon.DecodeFrame(sourceRecord, frame);
                        frames.Add(new($"{resourceId}/{frames.Count:D2}", WeaponReferenceWidth, WeaponReferenceHeight,
                            PlaceWeaponFrame(decoded, palette, mapping.Alignment, mapping.ScreenOffset)));
                    }
                }

                frameStarts.Add(frameSourceKey, start);
            }

            IReadOnlyList<int>? sequence = sourceMapping.Sequence?.Select(frame => checked(start + frame)).ToArray();

            ClassicWeaponActionManifest action = new(
                sourceMapping.Action,
                sourceMapping.SourceRecordOrdinal,
                start,
                sourceMapping.FrameCount,
                mapping.Alignment,
                mapping.ScreenOffset,
                mapping.Timing,
                info.XOffset,
                info.YOffset,
                sequence);
            action.Validate(frames.Count);
            actions.Add(action);
        }

        NormalizedSpriteAtlas atlas = SpriteAtlasNormalizer.Normalize(
            frames,
            SpriteAtlasOptions.FixedCellGrid(options.MaximumAtlasDimension, WeaponReferenceWidth, WeaponReferenceHeight, bottomAlign: true));
        return (GeneratedMediaArtifact.FromAtlas(resourceId, NormalizedMediaKind.WeaponSprite, $"media/combat/{Slug(resourceId)}-atlas.png", atlas), actions.ToArray());
    }

    private static IEnumerable<GeneratedMediaArtifact> BuildEffects(
        ReadOnlySpan<byte> textureBytes,
        Arena2Palette palette,
        ResolvedProfile profile,
        Arena2ClassicMediaPublicationOptions options,
        out ClassicEffectManifest[] manifests)
    {
        TextureArchive texture = TextureArchive.Parse(textureBytes, "arena2/TEXTURE.380");
        List<GeneratedMediaArtifact> result = [];
        List<ClassicEffectManifest> semantic = [];
        foreach (EffectSource source in EffectSources)
        {
            ClassicEffectPresentation mapping = profile.Effects[source.Effect];
            TextureRecordInfo info = texture.GetRecordInfo(source.SourceRecordOrdinal);
            List<DecodedSpriteFrame> frames = [];
            for (int frame = 0; frame < info.FrameCount; frame++)
            {
                IndexedTextureFrame decoded = texture.DecodeFrame(source.SourceRecordOrdinal, frame);
                frames.Add(DecodedSpriteFrame.FromPalette(
                    $"{mapping.MediaId}/{frame:D2}", decoded.Width, decoded.Height, decoded.Pixels.Span, palette, PaletteAlphaMode.IndexZeroTransparent));
            }

            NormalizedSpriteAtlas atlas = SpriteAtlasNormalizer.Normalize(frames, SpriteAtlasOptions.Grid(options.MaximumAtlasDimension));
            result.Add(GeneratedMediaArtifact.FromAtlas(mapping.MediaId, NormalizedMediaKind.EffectSprite, $"media/effects/{Slug(mapping.MediaId)}-atlas.png", atlas));
            ClassicEffectManifest manifest = new(source.Effect, mapping.MediaId, source.SourceRecordOrdinal, mapping.Timing);
            manifest.Validate();
            semantic.Add(manifest);
        }

        manifests = semantic.ToArray();
        return result;
    }

    private static IEnumerable<GeneratedMediaArtifact> BuildAudio(
        ReadOnlySpan<byte> soundBytes,
        Arena2ClassicMediaPublicationOptions options,
        out ClassicAudioManifest[] manifests)
    {
        SoundArchive sounds = SoundArchive.Parse(soundBytes, DaggerSoundSourcePath);
        List<GeneratedMediaArtifact> result = [];
        List<ClassicAudioManifest> semantic = [];
        foreach (AudioSource source in AudioSources)
        {
            Arena2PcmClip clip = sounds.GetClip(source.SourceRecordOrdinal);
            byte[] wave = sounds.CreateWave(source.SourceRecordOrdinal);
            RequireArtifactQuota(wave, options, source.MediaId);
            result.Add(new(source.MediaId, NormalizedMediaKind.Audio, $"media/audio/{Slug(source.MediaId)}.wav", wave, 0, 0, null, "audio/wav"));
            ClassicAudioManifest manifest = new(source.Clip, source.MediaId, source.SourceRecordOrdinal, clip.NumericId, SoundArchive.SampleRate);
            manifest.Validate();
            semantic.Add(manifest);
        }

        manifests = semantic.ToArray();
        return result;
    }

    private static IEnumerable<GeneratedMediaArtifact> BuildUi(
        SourceBytes sources,
        Arena2Palette palette,
        ResolvedProfile profile,
        Arena2ClassicMediaPublicationOptions options,
        out ClassicUiImageManifest[] manifests)
    {
        List<GeneratedMediaArtifact> result = [];
        List<ClassicUiImageManifest> semantic = [];
        foreach (ClassicUiImagePresentation mapping in profile.UiImages.Values.OrderBy(value => value.Image))
        {
            (IndexedImg image, Arena2Palette? ownPalette) = sources.DecodeUi(mapping.SourceFile);

            // A screen is drawn with the palette it carries; everything else uses the publication's.
            byte[] png = EncodePalettePng(image.Width, image.Height, image.Pixels.Span, ownPalette ?? palette);
            RequireArtifactQuota(png, options, mapping.MediaId);
            result.Add(new(mapping.MediaId, NormalizedMediaKind.UserInterface, $"media/ui/{Slug(mapping.MediaId)}.png", png, image.Width, image.Height, null, "image/png"));
            // A screen that carries its own palette is published in it, and the record says so: the
            // colours are the file's own trailing palette scaled by four, not the shared art palette.
            ClassicUiImageManifest manifest = new(
                mapping.Image, mapping.MediaId, mapping.SourceFile, image.XOffset, image.YOffset, image.IsHeaderless, ownPalette is not null);
            manifest.Validate();
            semantic.Add(manifest);
        }

        manifests = semantic.ToArray();
        return result;
    }

    private static IEnumerable<GeneratedMediaArtifact> BuildInventoryIcons(
        SourceBytes sources,
        Arena2Palette palette,
        ResolvedProfile profile,
        Arena2ClassicMediaPublicationOptions options,
        out ClassicInventoryIconManifest[] manifests)
    {
        Dictionary<int, TextureArchive> archives = new()
        {
            [207] = TextureArchive.Parse(sources.Texture207, "arena2/TEXTURE.207"),
            [216] = TextureArchive.Parse(sources.Texture216, "arena2/TEXTURE.216"),
            [234] = TextureArchive.Parse(sources.Texture234, "arena2/TEXTURE.234"),
            [245] = TextureArchive.Parse(sources.Texture245, "arena2/TEXTURE.245"),
        };
        List<GeneratedMediaArtifact> result = [];
        List<ClassicInventoryIconManifest> semantic = [];
        foreach (ClassicInventoryIconPresentation source in profile.InventoryIcons.Values.OrderBy(value => value.ItemId, StringComparer.Ordinal))
        {
            IndexedTextureFrame frame = archives[source.TextureArchive].DecodeFrame(source.SourceRecordOrdinal, 0);
            byte[] png = EncodePalettePng(frame.Width, frame.Height, frame.Pixels.Span, palette);
            RequireArtifactQuota(png, options, source.MediaId);
            result.Add(new(source.MediaId, NormalizedMediaKind.UserInterface, $"media/ui/inventory-icons/inventory-icon-{source.ItemId}.png", png, frame.Width, frame.Height, null, "image/png"));
            ClassicInventoryIconManifest manifest = new(source.ItemId, source.MediaId, source.TextureArchive, source.SourceRecordOrdinal);
            manifest.Validate();
            semantic.Add(manifest);
        }

        manifests = semantic.ToArray();
        return result;
    }

    private static (GeneratedMediaArtifact Artifact, ClassicFontManifest Manifest) BuildFont(
        ReadOnlySpan<byte> fontBytes,
        string sourceName,
        string mediaId,
        string use,
        Arena2ClassicMediaPublicationOptions options)
    {
        FntFont font = FntDecoder.Decode(fontBytes, sourceName);
        if (font.Glyphs.Count != Arena2FormatConstants.FntGlyphCount)
        {
            throw new Arena2FormatException(sourceName, 0, $"classic font requires {Arena2FormatConstants.FntGlyphCount} glyphs");
        }

        const int columns = 16;
        int width = columns * FontCellSize;
        int height = checked(((font.Glyphs.Count + columns - 1) / columns) * FontCellSize);
        if (width > options.MaximumAtlasDimension || height > options.MaximumAtlasDimension)
        {
            throw new InvalidOperationException("Classic font atlas exceeds the atlas dimension quota.");
        }

        byte[] rgba = new byte[checked(width * height * 4)];
        List<NormalizedAtlasFrame> frames = new(font.Glyphs.Count);
        List<ClassicFontGlyphMetric> glyphs = new(font.Glyphs.Count);
        for (int index = 0; index < font.Glyphs.Count; index++)
        {
            FntGlyph glyph = font.Glyphs[index];
            int x = (index % columns) * FontCellSize;
            int y = (index / columns) * FontCellSize;
            for (int row = 0; row < FontCellSize; row++)
            {
                for (int column = 0; column < FontCellSize; column++)
                {
                    if (!glyph.IsSet(column, row)) continue;
                    int target = ((y + row) * width + x + column) * 4;
                    rgba[target] = byte.MaxValue;
                    rgba[target + 1] = byte.MaxValue;
                    rgba[target + 2] = byte.MaxValue;
                    rgba[target + 3] = byte.MaxValue;
                }
            }

            frames.Add(new($"{mediaId}/{index:D3}", index, x, y, FontCellSize, FontCellSize, FontCellSize, FontCellSize, false));
            glyphs.Add(new(index, x, y, glyph.Width, glyph.DataOffset));
        }

        byte[] png = DeterministicPngEncoder.EncodeRgba8(width, height, rgba);
        RequireArtifactQuota(png, options, mediaId);
        NormalizedSpriteAtlas atlas = new(width, height, png, ContentDigest.Compute(png), frames);
        GeneratedMediaArtifact artifact = GeneratedMediaArtifact.FromAtlas(mediaId, NormalizedMediaKind.Font, $"media/fonts/{Slug(mediaId)}-atlas.png", atlas);
        ClassicFontManifest manifest = new(mediaId, use, font.FixedWidth, font.FixedHeight, glyphs);
        manifest.Validate();
        return (artifact, manifest);
    }

    private static IEnumerable<GeneratedMediaArtifact> BuildAuthoredUi(
        ResolvedProfile profile,
        Arena2ClassicMediaPublicationOptions options,
        out ClassicAuthoredUiAssetManifest[] manifests)
    {
        List<GeneratedMediaArtifact> artifacts = [];
        List<ClassicAuthoredUiAssetManifest> metadata = [];
        foreach (ClassicAuthoredUiAsset asset in profile.AuthoredUiAssets.OrderBy(asset => asset.Id, StringComparer.Ordinal))
        {
            (int width, int height) = ReadPngDimensions(asset.PngBytes, asset.Id);
            if (width > options.MaximumAtlasDimension || height > options.MaximumAtlasDimension)
            {
                throw new InvalidOperationException($"Authored UI asset '{asset.Id}' exceeds the image dimension quota.");
            }

            // The operator's file is the source, not the published artifact. Decoration that the DOM
            // paints is published at a bounded size, derived here rather than copied, so the delivered
            // tree carries something a snapshot can afford and the oversized plate stays outside it.
            DeterministicPngImage source = DeterministicPngReader.ReadRgba8(asset.PngBytes, asset.Id);
            DeterministicPngImage published = DeterministicImageResample.FitWithin(source, options.AuthoredUiMaximumDimension, asset.Id);
            byte[] bytes = source == published ? asset.PngBytes : DeterministicPngEncoder.EncodeRgba8(published.Width, published.Height, published.Rgba);
            if (bytes.LongLength > options.AuthoredUiMaximumArtifactBytes)
            {
                throw new InvalidOperationException(
                    $"Authored UI asset '{asset.Id}' publishes as {bytes.LongLength} bytes, past the {options.AuthoredUiMaximumArtifactBytes}-byte bound for DOM decoration; normalize the source instead of shipping it.");
            }

            RequireArtifactQuota(bytes, options, asset.Id);
            artifacts.Add(new(asset.Id, NormalizedMediaKind.UserInterface, asset.RelativePath, bytes, published.Width, published.Height, null, "image/png"));
            metadata.Add(new(asset.Id, asset.RelativePath, asset.SourceLabel, asset.Generator, asset.Prompt));
        }

        manifests = metadata.ToArray();
        return artifacts;
    }

    private static IReadOnlyList<AuthoredMediaOverlay> BuildOverlays(ResolvedProfile profile, IReadOnlyList<AuthoredMediaOverlay>? authoredOverlays)
    {
        IReadOnlyDictionary<string, ClassicMediaPresentation> visual = profile.Presentation;
        List<AuthoredMediaOverlay> overlays = [];
        foreach (ClassicEffectPresentation effect in profile.Effects.Values.OrderBy(effect => effect.Effect))
        {
            visual.TryGetValue(effect.MediaId, out ClassicMediaPresentation? presentation);
            overlays.Add(new(
                effect.MediaId,
                true,
                presentation?.DisplayName,
                presentation?.Pivot,
                presentation?.DisplaySize,
                effect.Timing.FramesPerSecond,
                effect.Timing.Loop,
                presentation?.Sequence));
        }

        foreach (ClassicMediaPresentation presentation in visual.Values.Where(presentation => !profile.Effects.Values.Any(effect => StringComparer.Ordinal.Equals(effect.MediaId, presentation.MediaId))).OrderBy(presentation => presentation.MediaId, StringComparer.Ordinal))
        {
            overlays.Add(new(
                presentation.MediaId,
                true,
                presentation.DisplayName,
                presentation.Pivot,
                presentation.DisplaySize,
                null,
                null,
                presentation.Sequence));
        }

        if (authoredOverlays is not null)
        {
            Dictionary<string, AuthoredMediaOverlay> byId = overlays.ToDictionary(overlay => overlay.Id, StringComparer.Ordinal);
            foreach (AuthoredMediaOverlay overlay in authoredOverlays)
            {
                ArgumentNullException.ThrowIfNull(overlay);
                if (!overlay.IsAuthored)
                {
                    throw new ArgumentException("External classic media overlays must be authored values.", nameof(authoredOverlays));
                }
                if (byId.TryGetValue(overlay.Id, out AuthoredMediaOverlay? existing))
                {
                    byId[overlay.Id] = existing with
                    {
                        DisplayName = overlay.DisplayName ?? existing.DisplayName,
                        Pivot = overlay.Pivot ?? existing.Pivot,
                        DisplaySize = overlay.DisplaySize ?? existing.DisplaySize,
                        FramesPerSecond = overlay.FramesPerSecond ?? existing.FramesPerSecond,
                        Loop = overlay.Loop ?? existing.Loop,
                        Sequence = overlay.Sequence ?? existing.Sequence,
                        FrameRects = overlay.FrameRects ?? existing.FrameRects,
                        StateTimings = overlay.StateTimings ?? existing.StateTimings,
                        ActionTimings = overlay.ActionTimings ?? existing.ActionTimings,
                    };
                }
                else
                {
                    byId.Add(overlay.Id, overlay);
                }
            }

            return byId.Values.OrderBy(overlay => overlay.Id, StringComparer.Ordinal).ToArray();
        }

        return overlays;
    }

    private static ResolvedProfile ApplyAuthoredEffectTiming(ResolvedProfile profile, IReadOnlyList<AuthoredMediaOverlay>? authoredOverlays)
    {
        if (authoredOverlays is null)
        {
            return profile;
        }

        Dictionary<ClassicEffect, ClassicEffectPresentation> effects = profile.Effects.ToDictionary(pair => pair.Key, pair => pair.Value);
        Dictionary<ClassicDaggerWeaponAction, ClassicWeaponActionPresentation> weaponActions = profile.WeaponActions.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (AuthoredMediaOverlay overlay in authoredOverlays)
        {
            ArgumentNullException.ThrowIfNull(overlay);
            if (!overlay.IsAuthored)
            {
                throw new ArgumentException("External classic media overlays must be authored values.", nameof(authoredOverlays));
            }

            if (overlay.StateTimings is { Count: > 0 })
            {
                throw new ArgumentException("Classic media does not publish named sprite states for authored timing.", nameof(authoredOverlays));
            }

            if (StringComparer.Ordinal.Equals(overlay.Id, profile.WeaponMediaId)
                && (overlay.FramesPerSecond is not null || overlay.Loop is not null || overlay.Sequence is not null))
            {
                throw new ArgumentException("Classic weapon timing and sequence are action-specific and cannot be supplied as resource-wide overlays.", nameof(authoredOverlays));
            }

            if (StringComparer.Ordinal.Equals(overlay.Id, profile.WeaponMediaId))
            {
                ApplyWeaponActionTimings(weaponActions, overlay.ActionTimings, nameof(authoredOverlays));
                continue;
            }

            ClassicEffectPresentation[] matched = effects.Values
                .Where(effect => StringComparer.Ordinal.Equals(effect.MediaId, overlay.Id))
                .ToArray();
            if (matched.Length == 0)
            {
                if (overlay.ActionTimings is { Count: > 0 })
                {
                    throw new ArgumentException("An action timing overlay must identify a classic effect resource.", nameof(authoredOverlays));
                }
                continue;
            }

            if (overlay.FramesPerSecond is not null || overlay.Loop is not null)
            {
                ClassicSpriteTiming current = matched[0].Timing;
                if (matched.Any(effect => effect.Timing != current))
                {
                    throw new ArgumentException("A shared classic effect media resource must have compatible timings before a resource-wide timing overlay can be applied.", nameof(authoredOverlays));
                }

                ClassicSpriteTiming updated = new(overlay.FramesPerSecond ?? current.FramesPerSecond, overlay.Loop ?? current.Loop);
                updated.Validate();
                foreach (ClassicEffectPresentation effect in matched)
                {
                    effects[effect.Effect] = effect with { Timing = updated };
                }
            }

            ApplyEffectActionTimings(effects, overlay.Id, overlay.ActionTimings, nameof(authoredOverlays));
        }

        return profile with { Effects = effects, WeaponActions = weaponActions };
    }

    private static void ApplyWeaponActionTimings(
        IDictionary<ClassicDaggerWeaponAction, ClassicWeaponActionPresentation> actions,
        IReadOnlyList<AuthoredMediaActionTiming>? timings,
        string parameterName)
    {
        if (timings is null) return;
        foreach (AuthoredMediaActionTiming timing in timings)
        {
            if (!Enum.TryParse(timing.Name, ignoreCase: false, out ClassicDaggerWeaponAction action) || !actions.TryGetValue(action, out ClassicWeaponActionPresentation? current))
            {
                throw new ArgumentException("An authored classic weapon action timing must name a generated weapon action.", parameterName);
            }

            ClassicSpriteTiming updated = new(timing.FramesPerSecond ?? current.Timing.FramesPerSecond, timing.Loop ?? current.Timing.Loop);
            updated.Validate();
            actions[action] = current with { Timing = updated };
        }
    }

    private static void ApplyEffectActionTimings(
        IDictionary<ClassicEffect, ClassicEffectPresentation> effects,
        string mediaId,
        IReadOnlyList<AuthoredMediaActionTiming>? timings,
        string parameterName)
    {
        if (timings is null) return;
        foreach (AuthoredMediaActionTiming timing in timings)
        {
            if (!Enum.TryParse(timing.Name, ignoreCase: false, out ClassicEffect effect)
                || !effects.TryGetValue(effect, out ClassicEffectPresentation? current)
                || !StringComparer.Ordinal.Equals(current.MediaId, mediaId))
            {
                throw new ArgumentException("An authored classic effect timing must name an effect published by the selected resource.", parameterName);
            }

            ClassicSpriteTiming updated = new(timing.FramesPerSecond ?? current.Timing.FramesPerSecond, timing.Loop ?? current.Timing.Loop);
            updated.Validate();
            effects[effect] = current with { Timing = updated };
        }
    }

    private static IReadOnlyList<LogicalSourceRecord> MergeSources(
        IReadOnlyList<LogicalSourceRecord> arena2Sources,
        IReadOnlyList<ClassicAuthoredUiAsset> authoredUiAssets,
        LogicalSourceRecord? authoredManifestSource)
    {
        List<LogicalSourceRecord> result = [.. arena2Sources];
        if (authoredManifestSource is not null)
        {
            result.Add(authoredManifestSource);
        }

        foreach (ClassicAuthoredUiAsset asset in authoredUiAssets)
        {
            result.Add(new(
                LogicalSourceRecord.CurrentSchemaVersion,
                $"authored-ui/{asset.SourceLabel}",
                ContentDigest.Compute(asset.PngBytes),
                asset.PngBytes.LongLength,
                1));
        }

        NormalizedImportDocument.ValidateUnique(result, source => source.SourcePath, "classic media logical source");
        foreach (LogicalSourceRecord source in result)
        {
            source.Validate();
        }

        return result.OrderBy(source => source.SourcePath, StringComparer.Ordinal).ToArray();
    }

    private static LogicalSourceRecord? CreateAuthoredManifestSource(ClassicAuthoredUiManifestInput? input, long maximumSourceBytes)
    {
        if (input is null) return null;
        RequirePortableSourceBytes(input.SourceLabel, input.Bytes, maximumSourceBytes, nameof(input));
        return new(
            LogicalSourceRecord.CurrentSchemaVersion,
            $"authored-ui/{input.SourceLabel}",
            ContentDigest.Compute(input.Bytes),
            input.Bytes.LongLength,
            1);
    }

    private static ResolvedProfile ResolveProfile(Arena2ClassicMediaProfile profile)
    {
        IReadOnlyList<ClassicWeaponActionPresentation> weaponActions = profile.WeaponActions ?? DaggerWeaponActionSources
            .Select(source => new ClassicWeaponActionPresentation(source.Action, source.Alignment, source.ScreenOffset, new ClassicSpriteTiming(10F, source.Loop)))
            .ToArray();
        IReadOnlyList<ClassicEffectPresentation> effects = profile.Effects ?? EffectSources
            .Select(source => new ClassicEffectPresentation(source.Effect, source.MediaId, new ClassicSpriteTiming(10F, false)))
            .ToArray();
        IReadOnlyList<ClassicUiImagePresentation> uiImages = profile.UiImages ?? UiImageSources
            .Select(source => new ClassicUiImagePresentation(source.Image, source.MediaId, source.FileName))
            .ToArray();
        IReadOnlyList<ClassicInventoryIconPresentation> inventoryIcons = profile.InventoryIcons ?? InventoryIconSources
            .Select(source => new ClassicInventoryIconPresentation(source.ItemId, $"inventory.icon.{source.ItemId}", source.TextureArchive, source.SourceRecordOrdinal))
            .ToArray();
        IReadOnlyList<ClassicMediaPresentation> presentation = profile.Presentation ?? [];
        IReadOnlyList<ClassicAuthoredUiAsset> authoredUiAssets = profile.AuthoredUiAssets ?? [];

        Dictionary<ClassicDaggerWeaponAction, ClassicWeaponActionPresentation> weapons = IndexExact(
            weaponActions,
            action => action.Action,
            Enum.GetValues<ClassicDaggerWeaponAction>(),
            "classic weapon action");
        foreach (ClassicWeaponActionPresentation action in weapons.Values)
        {
            if (!Enum.IsDefined(action.Alignment) || !float.IsFinite(action.ScreenOffset))
            {
                throw new ArgumentOutOfRangeException(nameof(profile), "Classic weapon presentation handles must be finite and known.");
            }

            ArgumentNullException.ThrowIfNull(action.Timing);
            action.Timing.Validate();
        }

        Dictionary<ClassicEffect, ClassicEffectPresentation> effectMap = IndexExact(
            effects,
            effect => effect.Effect,
            Enum.GetValues<ClassicEffect>(),
            "classic effect");
        foreach (ClassicEffectPresentation effect in effectMap.Values)
        {
            NormalizedImportDocument.RequireLogicalId(effect.MediaId, nameof(effect.MediaId));
            ArgumentNullException.ThrowIfNull(effect.Timing);
            effect.Timing.Validate();
        }

        Dictionary<ClassicUiImage, ClassicUiImagePresentation> uiMap = IndexExact(
            uiImages,
            image => image.Image,
            Enum.GetValues<ClassicUiImage>(),
            "classic UI image");
        foreach (ClassicUiImagePresentation image in uiMap.Values)
        {
            NormalizedImportDocument.RequireLogicalId(image.MediaId, nameof(image.MediaId));
            if (!UiImageSources.Any(source => StringComparer.Ordinal.Equals(source.FileName, image.SourceFile)))
            {
                throw new ArgumentException($"Classic UI image source '{image.SourceFile}' is not admitted.", nameof(profile));
            }
        }

        Dictionary<string, ClassicInventoryIconPresentation> inventoryMap = IndexExact(
            inventoryIcons,
            icon => icon.ItemId,
            InventoryIconSources.Select(source => source.ItemId),
            "classic inventory icon");
        foreach (ClassicInventoryIconPresentation icon in inventoryMap.Values)
        {
            NormalizedImportDocument.RequireLogicalId(icon.MediaId, nameof(icon.MediaId));
            if (icon.SourceRecordOrdinal < 0 || icon.TextureArchive is not 207 and not 216 and not 234 and not 245)
            {
                throw new ArgumentOutOfRangeException(nameof(profile), "Classic inventory mappings must stay within the admitted texture archive closure.");
            }
        }

        string weaponMediaId = profile.WeaponMediaId ?? "weapon.dagger.steel";
        NormalizedImportDocument.RequireLogicalId(weaponMediaId, nameof(profile.WeaponMediaId));
        string fontMediaId = profile.FontMediaId ?? "font.classic.0003";
        NormalizedImportDocument.RequireLogicalId(fontMediaId, nameof(profile.FontMediaId));
        if (!FontSources.Any(source => StringComparer.Ordinal.Equals(source.MediaId, fontMediaId)))
        {
            throw new ArgumentException($"The default font '{fontMediaId}' is not an admitted classic font.", nameof(profile));
        }
        Dictionary<string, ClassicMediaPresentation> presentationMap = IndexUnique(
            presentation,
            item => item.MediaId,
            "classic media presentation");
        foreach (ClassicMediaPresentation item in presentationMap.Values)
        {
            NormalizedImportDocument.RequireLogicalId(item.MediaId, nameof(item.MediaId));
            if (item.DisplayName is not null && (string.IsNullOrWhiteSpace(item.DisplayName) || item.DisplayName.Any(char.IsControl)))
            {
                throw new ArgumentException("Classic media display names must be plain non-empty text.", nameof(profile));
            }

            item.Pivot?.Validate(nameof(item.Pivot));
            item.DisplaySize?.Validate(nameof(item.DisplaySize));
            if (item.DisplaySize is { X: <= 0F } or { Y: <= 0F })
            {
                throw new ArgumentOutOfRangeException(nameof(profile), "Classic media display sizes must be positive.");
            }
        }

        ValidateAuthoredUiAssets(authoredUiAssets);
        if (profile.AuthoredUiManifest is not null)
        {
            NormalizedImportDocument.RequireLogicalPath(profile.AuthoredUiManifest.SourceLabel, nameof(profile.AuthoredUiManifest));
            if (authoredUiAssets.Any(asset => StringComparer.Ordinal.Equals(asset.SourceLabel, profile.AuthoredUiManifest.SourceLabel)))
            {
                throw new ArgumentException("The authored UI manifest source label must not collide with an authored PNG source label.", nameof(profile));
            }
        }

        return new(weaponMediaId, weapons, effectMap, uiMap, inventoryMap, fontMediaId, presentationMap, profile.AuthoredUiManifest, authoredUiAssets.OrderBy(asset => asset.Id, StringComparer.Ordinal).ToArray());
    }

    private static void ValidateAuthoredUiAssets(IReadOnlyList<ClassicAuthoredUiAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> paths = new(StringComparer.Ordinal);
        HashSet<string> sourceLabels = new(StringComparer.Ordinal);
        foreach (ClassicAuthoredUiAsset asset in assets)
        {
            ArgumentNullException.ThrowIfNull(asset);
            NormalizedImportDocument.RequireLogicalId(asset.Id, nameof(asset.Id));
            NormalizedImportDocument.RequireLogicalPath(asset.RelativePath, nameof(asset.RelativePath));
            NormalizedImportDocument.RequireLogicalPath(asset.SourceLabel, nameof(asset.SourceLabel));
            if (asset.PngBytes is null || asset.PngBytes.Length == 0
                || string.IsNullOrWhiteSpace(asset.Generator) || asset.Generator.Any(char.IsControl)
                || string.IsNullOrWhiteSpace(asset.Prompt) || asset.Prompt.Any(char.IsControl)
                || !ids.Add(asset.Id) || !paths.Add(asset.RelativePath) || !sourceLabels.Add(asset.SourceLabel))
            {
                throw new ArgumentException("Authored UI inputs need unique IDs, output paths, source labels, non-empty PNG bytes, and plain provenance metadata.", nameof(assets));
            }
        }
    }

    private static Dictionary<TKey, TValue> IndexExact<TKey, TValue>(
        IReadOnlyList<TValue> values,
        Func<TValue, TKey> key,
        IEnumerable<TKey> required,
        string kind)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(values);
        Dictionary<TKey, TValue> result = new();
        foreach (TValue value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!result.TryAdd(key(value), value))
            {
                throw new ArgumentException($"The profile repeats {kind} '{key(value)}'.", nameof(values));
            }
        }

        HashSet<TKey> expected = required.ToHashSet();
        if (!result.Keys.ToHashSet().SetEquals(expected))
        {
            throw new ArgumentException($"The profile must map the exact admitted {kind} set.", nameof(values));
        }

        return result;
    }

    private static Dictionary<string, TValue> IndexUnique<TValue>(IReadOnlyList<TValue> values, Func<TValue, string> key, string kind)
    {
        ArgumentNullException.ThrowIfNull(values);
        Dictionary<string, TValue> result = new(StringComparer.Ordinal);
        foreach (TValue value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            string id = key(value);
            if (!result.TryAdd(id, value))
            {
                throw new ArgumentException($"The profile repeats {kind} '{id}'.", nameof(values));
            }
        }

        return result;
    }

    private static (int Width, int Height) ReadPngDimensions(ReadOnlySpan<byte> bytes, string id)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature) || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8) || ReadUInt32BigEndian(bytes, 8) != 13)
        {
            throw new ArgumentException($"Authored UI asset '{id}' must begin with a PNG IHDR chunk.", nameof(bytes));
        }

        uint width = ReadUInt32BigEndian(bytes, 16);
        uint height = ReadUInt32BigEndian(bytes, 20);
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
        {
            throw new ArgumentException($"Authored UI asset '{id}' has invalid PNG dimensions.", nameof(bytes));
        }

        int offset = 8;
        bool hasImageData = false;
        bool hasEnd = false;
        while (offset < bytes.Length)
        {
            if (offset > bytes.Length - 12)
            {
                throw new ArgumentException($"Authored UI asset '{id}' has a truncated PNG chunk.", nameof(bytes));
            }

            uint length = ReadUInt32BigEndian(bytes, offset);
            int dataOffset = offset + 8;
            if (length > bytes.Length - dataOffset - 4)
            {
                throw new ArgumentException($"Authored UI asset '{id}' has a PNG chunk outside its byte closure.", nameof(bytes));
            }

            ReadOnlySpan<byte> kind = bytes.Slice(offset + 4, 4);
            hasImageData |= kind.SequenceEqual("IDAT"u8) && length > 0;
            if (kind.SequenceEqual("IEND"u8))
            {
                if (length != 0 || dataOffset + 4 != bytes.Length)
                {
                    throw new ArgumentException($"Authored UI asset '{id}' has an invalid terminal PNG chunk.", nameof(bytes));
                }

                hasEnd = true;
                break;
            }

            offset = checked(dataOffset + (int)length + 4);
        }

        if (!hasImageData || !hasEnd)
        {
            throw new ArgumentException($"Authored UI asset '{id}' needs a complete PNG image-data and terminal closure.", nameof(bytes));
        }

        return ((int)width, (int)height);
    }

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes, int offset) =>
        (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);

    private static void RequirePortableSourceBytes(string sourceLabel, byte[] bytes, long maximumSourceBytes, string parameterName)
    {
        NormalizedImportDocument.RequireLogicalPath(sourceLabel, parameterName);
        if (bytes is null || bytes.Length == 0 || bytes.LongLength > maximumSourceBytes)
        {
            throw new ArgumentException("A tracked authored manifest must be present and within the source-byte quota.", parameterName);
        }
    }

    private sealed record ResolvedProfile(
        string WeaponMediaId,
        IReadOnlyDictionary<ClassicDaggerWeaponAction, ClassicWeaponActionPresentation> WeaponActions,
        IReadOnlyDictionary<ClassicEffect, ClassicEffectPresentation> Effects,
        IReadOnlyDictionary<ClassicUiImage, ClassicUiImagePresentation> UiImages,
        IReadOnlyDictionary<string, ClassicInventoryIconPresentation> InventoryIcons,
        string FontMediaId,
        IReadOnlyDictionary<string, ClassicMediaPresentation> Presentation,
        ClassicAuthoredUiManifestInput? AuthoredUiManifest,
        IReadOnlyList<ClassicAuthoredUiAsset> AuthoredUiAssets);

    private static byte[] PlaceWeaponFrame(IndexedWeaponCifFrame source, Arena2Palette palette, ClassicWeaponScreenAlignment alignment, float screenOffset)
    {
        if (source.Info.Width > WeaponReferenceWidth || source.Info.Height > WeaponReferenceHeight)
        {
            throw new Arena2FormatException(source.Source, 0, $"weapon frame {source.Info.Width}x{source.Info.Height} exceeds {WeaponReferenceWidth}x{WeaponReferenceHeight} classic reference canvas");
        }

        Rgba32[] pixels = palette.ToRgba(source.Pixels.Span, PaletteAlphaMode.IndexZeroTransparent);
        byte[] canvas = new byte[WeaponReferenceWidth * WeaponReferenceHeight * 4];
        int available = WeaponReferenceWidth - source.Info.Width;
        int requestedOffset = checked((int)MathF.Round(screenOffset * WeaponReferenceWidth, MidpointRounding.AwayFromZero));
        int offset = Math.Clamp(requestedOffset, 0, available);
        int x = alignment switch
        {
            ClassicWeaponScreenAlignment.Left => offset,
            ClassicWeaponScreenAlignment.Center => available / 2,
            ClassicWeaponScreenAlignment.Right => available - offset,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
        };
        int y = WeaponReferenceHeight - source.Info.Height;
        for (int row = 0; row < source.Info.Height; row++)
        {
            for (int column = 0; column < source.Info.Width; column++)
            {
                Rgba32 pixel = pixels[(row * source.Info.Width) + column];
                int target = (((y + row) * WeaponReferenceWidth) + x + column) * 4;
                canvas[target] = pixel.Red;
                canvas[target + 1] = pixel.Green;
                canvas[target + 2] = pixel.Blue;
                canvas[target + 3] = pixel.Alpha;
            }
        }

        return canvas;
    }

    private static byte[] EncodePalettePng(int width, int height, ReadOnlySpan<byte> indexed, Arena2Palette palette)
    {
        Rgba32[] colors = palette.ToRgba(indexed, PaletteAlphaMode.IndexZeroTransparent);
        byte[] rgba = new byte[checked(colors.Length * 4)];
        for (int index = 0; index < colors.Length; index++)
        {
            int target = index * 4;
            rgba[target] = colors[index].Red;
            rgba[target + 1] = colors[index].Green;
            rgba[target + 2] = colors[index].Blue;
            rgba[target + 3] = colors[index].Alpha;
        }

        return DeterministicPngEncoder.EncodeRgba8(width, height, rgba);
    }

    private static void RequireArtifactQuota(byte[] bytes, Arena2ClassicMediaPublicationOptions options, string id)
    {
        if (bytes.LongLength > options.MaximumArtifactBytes)
        {
            throw new InvalidOperationException($"Classic media artifact '{id}' exceeds the encoded-byte quota.");
        }
    }

    private static string Slug(string value) => value.Replace('.', '-');

    private sealed record WeaponActionSource(
        ClassicDaggerWeaponAction Action,
        int SourceRecordOrdinal,
        ClassicWeaponScreenAlignment Alignment,
        float ScreenOffset,
        bool Loop,
        int FrameCount,
        IReadOnlyList<int>? Sequence = null,
        IReadOnlyList<int>? FrameSourceRecords = null);
    private sealed record WeaponMediaSource(string FileName, string ResourceId, IReadOnlyList<WeaponActionSource> Actions);
    private sealed record FontSource(string FileName, string MediaId, string Use);

    /// <summary>
    /// The map art files the donor draws with index-zero cutout rather than opaque: every other
    /// map canvas covers with index zero.
    /// </summary>
    private static readonly HashSet<string> CutoutMapFiles = new(
        ["AMAP00I0.IMG", "AMAP01I0.IMG", "TMAP00I0.IMG", "TOWN00I0.IMG", "TRAV0I04.IMG"],
        StringComparer.Ordinal);
    private sealed record EffectSource(ClassicEffect Effect, string MediaId, int SourceRecordOrdinal);
    private sealed record AudioSource(ClassicDaggerAudioClip Clip, string MediaId, int SourceRecordOrdinal);
    private sealed record UiImageSource(ClassicUiImage Image, string MediaId, string FileName, bool IsHeaderless);
    private sealed record InventoryIconSource(string ItemId, int TextureArchive, int SourceRecordOrdinal);

    private sealed class SourceBytes
    {
        private SourceBytes(Arena2ClassicMediaInputs inputs, IReadOnlyList<LogicalSourceRecord> logicalSources)
        {
            Weapon01Cif = inputs.Weapon01Cif;
            Weapon02Cif = inputs.Weapon02Cif;
            Weapon04Cif = inputs.Weapon04Cif;
            Weapon05Cif = inputs.Weapon05Cif;
            Weapon06Cif = inputs.Weapon06Cif;
            Weapon07Cif = inputs.Weapon07Cif;
            Weapon08Cif = inputs.Weapon08Cif;
            Weapon09Cif = inputs.Weapon09Cif;
            Weapon10Cif = inputs.Weapon10Cif;
            ArtPalette = inputs.ArtPalette;
            Texture380 = inputs.Texture380;
            Palette = inputs.Palette;
            DaggerSound = inputs.DaggerSound;
            Main00I0Img = inputs.Main00I0Img;
            Main03I0Img = inputs.Main03I0Img;
            Main04I0Img = inputs.Main04I0Img;
            Main05I0Img = inputs.Main05I0Img;
            Inve00I0Img = inputs.Inve00I0Img;
            Book00I0Img = inputs.Book00I0Img;
            Rest01I0Img = inputs.Rest01I0Img;
            Rest02I0Img = inputs.Rest02I0Img;
            Inve08I0Img = inputs.Inve08I0Img;
            Inve10I0Img = inputs.Inve10I0Img;
            Inve11I0Img = inputs.Inve11I0Img;
            Inve12I0Img = inputs.Inve12I0Img;
            Inve14I0Img = inputs.Inve14I0Img;
            Gild01I0Img = inputs.Gild01I0Img;
            Rest00I0Img = inputs.Rest00I0Img;
            Shop00I0Img = inputs.Shop00I0Img;
            Gild00I0Img = inputs.Gild00I0Img;
            Bank00I0Img = inputs.Bank00I0Img;
            Die00I0Img = inputs.Die00I0Img;
            Chgn00I0Img = inputs.Chgn00I0Img;
            Pick02I0Img = inputs.Pick02I0Img;
            Pick03I0Img = inputs.Pick03I0Img;
            Pris00I0Img = inputs.Pris00I0Img;
            Titl00I0Img = inputs.Titl00I0Img;
            Info00I0Img = inputs.Info00I0Img;
            Texture207 = inputs.Texture207;
            Texture216 = inputs.Texture216;
            Texture234 = inputs.Texture234;
            Texture245 = inputs.Texture245;
            Font0003Fnt = inputs.Font0003Fnt;
            Font0000Fnt = inputs.Font0000Fnt;
            Font0001Fnt = inputs.Font0001Fnt;
            Font0002Fnt = inputs.Font0002Fnt;
            Font0004Fnt = inputs.Font0004Fnt;
            MapMedia = inputs.MapMedia;
            FmapPalCol = inputs.FmapPalCol;
            MapPalCol = inputs.MapPalCol;
            Weapon00Cif = inputs.Weapon00Cif;
            Weapon03Cif = inputs.Weapon03Cif;
            Weapon11Cif = inputs.Weapon11Cif;
            LogicalSources = logicalSources;
        }

        public byte[] Weapon01Cif { get; }
        public byte[] Weapon02Cif { get; }
        public byte[] Weapon04Cif { get; }
        public byte[] Weapon05Cif { get; }
        public byte[] Weapon06Cif { get; }
        public byte[] Weapon07Cif { get; }
        public byte[] Weapon08Cif { get; }
        public byte[] Weapon09Cif { get; }
        public byte[] Weapon10Cif { get; }
        public byte[] Weapon00Cif { get; }
        public byte[] Weapon03Cif { get; }
        public byte[] Weapon11Cif { get; }
        public byte[] ArtPalette { get; }
        public byte[] Texture380 { get; }
        public byte[] Palette { get; }
        public byte[] DaggerSound { get; }
        public byte[] Main00I0Img { get; }
        public byte[] Main03I0Img { get; }
        public byte[] Main04I0Img { get; }
        public byte[] Main05I0Img { get; }
        public byte[] Inve00I0Img { get; }
        public byte[] Book00I0Img { get; }
        public byte[] Rest01I0Img { get; }
        public byte[] Rest02I0Img { get; }
        public byte[] Inve08I0Img { get; }
        public byte[] Inve10I0Img { get; }
        public byte[] Inve11I0Img { get; }
        public byte[] Inve12I0Img { get; }
        public byte[] Inve14I0Img { get; }
        public byte[] Gild01I0Img { get; }
        public byte[] Rest00I0Img { get; }
        public byte[] Shop00I0Img { get; }
        public byte[] Gild00I0Img { get; }
        public byte[] Bank00I0Img { get; }

        public byte[] Die00I0Img { get; }
        public byte[] Chgn00I0Img { get; }
        public byte[] Pick02I0Img { get; }
        public byte[] Pick03I0Img { get; }
        public byte[] Pris00I0Img { get; }
        public byte[] Titl00I0Img { get; }
        public byte[] Info00I0Img { get; }
        public byte[] Texture207 { get; }
        public byte[] Texture216 { get; }
        public byte[] Texture234 { get; }
        public byte[] Texture245 { get; }
        public byte[] Font0003Fnt { get; }
        public byte[] Font0000Fnt { get; }
        public byte[] Font0001Fnt { get; }
        public byte[] Font0002Fnt { get; }
        public byte[] Font0004Fnt { get; }
        public IReadOnlyList<MapMediaInput> MapMedia { get; }
        public byte[] FmapPalCol { get; }
        public byte[] MapPalCol { get; }
        public IReadOnlyList<LogicalSourceRecord> LogicalSources { get; }

        public static SourceBytes From(Arena2ClassicMediaInputs inputs, long maximumSourceBytes)
        {
            (string FileName, byte[] Bytes)[] sources =
            [
                ("WEAPON00.CIF", inputs.Weapon00Cif), ("WEAPON01.CIF", inputs.Weapon01Cif), ("WEAPON02.CIF", inputs.Weapon02Cif), ("WEAPON03.CIF", inputs.Weapon03Cif), ("WEAPON04.CIF", inputs.Weapon04Cif),
                ("WEAPON05.CIF", inputs.Weapon05Cif), ("WEAPON06.CIF", inputs.Weapon06Cif), ("WEAPON07.CIF", inputs.Weapon07Cif),
                ("WEAPON08.CIF", inputs.Weapon08Cif), ("WEAPON09.CIF", inputs.Weapon09Cif), ("WEAPON10.CIF", inputs.Weapon10Cif), ("WEAPON11.CIF", inputs.Weapon11Cif),
                ("ART_PAL.COL", inputs.ArtPalette), ("TEXTURE.380", inputs.Texture380), ("PAL.PAL", inputs.Palette),
                ("DAGGER.SND", inputs.DaggerSound), ("MAIN00I0.IMG", inputs.Main00I0Img), ("MAIN03I0.IMG", inputs.Main03I0Img),
                ("MAIN04I0.IMG", inputs.Main04I0Img), ("MAIN05I0.IMG", inputs.Main05I0Img), ("INVE00I0.IMG", inputs.Inve00I0Img), ("DIE_00I0.IMG", inputs.Die00I0Img), ("CHGN00I0.IMG", inputs.Chgn00I0Img), ("PICK02I0.IMG", inputs.Pick02I0Img),
                ("PICK03I0.IMG", inputs.Pick03I0Img), ("PRIS00I0.IMG", inputs.Pris00I0Img), ("TITL00I0.IMG", inputs.Titl00I0Img),
                ("BOOK00I0.IMG", inputs.Book00I0Img), ("REST00I0.IMG", inputs.Rest00I0Img), ("SHOP00I0.IMG", inputs.Shop00I0Img),
                ("REST01I0.IMG", inputs.Rest01I0Img), ("REST02I0.IMG", inputs.Rest02I0Img), ("GILD01I0.IMG", inputs.Gild01I0Img),
                ("INVE08I0.IMG", inputs.Inve08I0Img), ("INVE10I0.IMG", inputs.Inve10I0Img), ("INVE11I0.IMG", inputs.Inve11I0Img),
                ("INVE12I0.IMG", inputs.Inve12I0Img), ("INVE14I0.IMG", inputs.Inve14I0Img),
                ("GILD00I0.IMG", inputs.Gild00I0Img), ("BANK00I0.IMG", inputs.Bank00I0Img),
                ("INFO00I0.IMG", inputs.Info00I0Img), ("TEXTURE.207", inputs.Texture207), ("TEXTURE.216", inputs.Texture216),
                ("TEXTURE.234", inputs.Texture234), ("TEXTURE.245", inputs.Texture245), ("FONT0003.FNT", inputs.Font0003Fnt),
                ("FONT0000.FNT", inputs.Font0000Fnt), ("FONT0001.FNT", inputs.Font0001Fnt), ("FONT0002.FNT", inputs.Font0002Fnt), ("FONT0004.FNT", inputs.Font0004Fnt),
                ("FMAP_PAL.COL", inputs.FmapPalCol), ("MAP.PAL", inputs.MapPalCol),
                ..inputs.MapMedia.Select(file => (file.FileName, file.Bytes)),
            ];
            List<LogicalSourceRecord> logicalSources = new(sources.Length);
            foreach ((string fileName, byte[] bytes) in sources)
            {
                if (bytes is null || bytes.Length == 0 || bytes.LongLength > maximumSourceBytes)
                {
                    throw new ArgumentException($"Classic media source '{fileName}' must be present and within the source-byte quota.", nameof(inputs));
                }

                logicalSources.Add(new(LogicalSourceRecord.CurrentSchemaVersion, $"arena2/{fileName}", ContentDigest.Compute(bytes), bytes.LongLength, 1));
            }

            return new(inputs, logicalSources.OrderBy(source => source.SourcePath, StringComparer.Ordinal).ToArray());
        }

        public byte[] RequireWeapon(string fileName) => fileName switch
        {
            "WEAPON00.CIF" => Weapon00Cif,
            "WEAPON01.CIF" => Weapon01Cif,
            "WEAPON02.CIF" => Weapon02Cif,
            "WEAPON03.CIF" => Weapon03Cif,
            "WEAPON04.CIF" => Weapon04Cif,
            "WEAPON05.CIF" => Weapon05Cif,
            "WEAPON06.CIF" => Weapon06Cif,
            "WEAPON07.CIF" => Weapon07Cif,
            "WEAPON08.CIF" => Weapon08Cif,
            "WEAPON09.CIF" => Weapon09Cif,
            "WEAPON10.CIF" => Weapon10Cif,
            "WEAPON11.CIF" => Weapon11Cif,
            _ => throw new ArgumentOutOfRangeException(nameof(fileName), "The requested source is not an admitted classic weapon CIF."),
        };

        /// <summary>
        /// Decodes a UI image and, when the source carries one, the palette it is meant to be drawn
        /// with.
        /// </summary>
        /// <remarks>
        /// A screen is headerless like the window chrome and, unlike it, is followed by its own
        /// palette. The length decides that - the corpus's six screens are the only files of that
        /// shape - so the source answers rather than the caller deciding per image.
        /// </remarks>
        public (IndexedImg Image, Arena2Palette? Palette) DecodeUi(string fileName) => fileName switch
        {
            "MAIN00I0.IMG" => (ImgDecoder.Decode(Main00I0Img, "arena2/MAIN00I0.IMG"), null),
            "MAIN03I0.IMG" => (ImgDecoder.Decode(Main03I0Img, "arena2/MAIN03I0.IMG"), null),
            "MAIN04I0.IMG" => (ImgDecoder.Decode(Main04I0Img, "arena2/MAIN04I0.IMG"), null),
            "MAIN05I0.IMG" => (ImgDecoder.Decode(Main05I0Img, "arena2/MAIN05I0.IMG"), null),
            "INVE00I0.IMG" => (ImgDecoder.DecodeHeaderless(Inve00I0Img, "arena2/INVE00I0.IMG"), null),
            "INFO00I0.IMG" => (ImgDecoder.DecodeHeaderless(Info00I0Img, "arena2/INFO00I0.IMG"), null),
            "BOOK00I0.IMG" => (ImgDecoder.DecodeHeaderless(Book00I0Img, "arena2/BOOK00I0.IMG"), null),
            "REST00I0.IMG" => (ImgDecoder.Decode(Rest00I0Img, "arena2/REST00I0.IMG"), null),
            "SHOP00I0.IMG" => (ImgDecoder.Decode(Shop00I0Img, "arena2/SHOP00I0.IMG"), null),
            "GILD00I0.IMG" => (ImgDecoder.Decode(Gild00I0Img, "arena2/GILD00I0.IMG"), null),
            "BANK00I0.IMG" => (ImgDecoder.Decode(Bank00I0Img, "arena2/BANK00I0.IMG"), null),
            "REST01I0.IMG" => (ImgDecoder.Decode(Rest01I0Img, "arena2/REST01I0.IMG"), null),
            "REST02I0.IMG" => (ImgDecoder.Decode(Rest02I0Img, "arena2/REST02I0.IMG"), null),
            "INVE08I0.IMG" => (ImgDecoder.Decode(Inve08I0Img, "arena2/INVE08I0.IMG"), null),
            "INVE10I0.IMG" => (ImgDecoder.Decode(Inve10I0Img, "arena2/INVE10I0.IMG"), null),
            "INVE11I0.IMG" => (ImgDecoder.Decode(Inve11I0Img, "arena2/INVE11I0.IMG"), null),
            "INVE12I0.IMG" => (ImgDecoder.Decode(Inve12I0Img, "arena2/INVE12I0.IMG"), null),
            "INVE14I0.IMG" => (ImgDecoder.Decode(Inve14I0Img, "arena2/INVE14I0.IMG"), null),
            "GILD01I0.IMG" => (ImgDecoder.Decode(Gild01I0Img, "arena2/GILD01I0.IMG"), null),
            "DIE_00I0.IMG" => Screen(Die00I0Img, "arena2/DIE_00I0.IMG"),
            "CHGN00I0.IMG" => Screen(Chgn00I0Img, "arena2/CHGN00I0.IMG"),
            "PICK02I0.IMG" => Screen(Pick02I0Img, "arena2/PICK02I0.IMG"),
            "PICK03I0.IMG" => Screen(Pick03I0Img, "arena2/PICK03I0.IMG"),
            "PRIS00I0.IMG" => Screen(Pris00I0Img, "arena2/PRIS00I0.IMG"),
            "TITL00I0.IMG" => Screen(Titl00I0Img, "arena2/TITL00I0.IMG"),
            _ => throw new ArgumentOutOfRangeException(nameof(fileName)),
        };

        /// <summary>
        /// Decodes one supplied screen in the palette its own file carries.
        /// </summary>
        /// <remarks>
        /// The palette is required rather than optional: these six screens are the only files in the
        /// corpus of this shape, the classic reader paints them from those trailing bytes, and falling
        /// back to the shared art palette would publish every colour wrong while looking successful. A
        /// file that is not the documented shape - truncated, or padded - is refused with the missing
        /// reference named instead.
        /// </remarks>
        private static (IndexedImg Image, Arena2Palette? Palette) Screen(byte[] bytes, string source)
        {
            int canvasBytes = ImgDecoder.EmbeddedPaletteScreenBytes - ImgDecoder.EmbeddedPaletteBytes;
            if (!ImgDecoder.TryReadEmbeddedPalette(bytes, source, out Arena2Palette? palette, out string reason))
            {
                throw new InvalidOperationException(
                    $"{source} is published as a screen in its own embedded palette, but that palette could not be read: {reason}");
            }

            IndexedImg image = ImgDecoder.DecodeHeaderless([.. bytes.AsSpan(0, canvasBytes)], source);
            return (image, palette);
        }
    }
}
