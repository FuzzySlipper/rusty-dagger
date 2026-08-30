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
    byte[] Weapon02Cif,
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
    byte[] Texture207,
    byte[] Texture216,
    byte[] Texture234,
    byte[] Texture245,
    byte[] Font0003Fnt);

/// <summary>Quotas for bounded, deterministic classic-media regeneration.</summary>
public sealed record Arena2ClassicMediaPublicationOptions(
    int MaximumAtlasDimension = 4096,
    long MaximumSourceBytes = 16L * 1024 * 1024,
    long MaximumArtifactBytes = 16L * 1024 * 1024,
    long MaximumTotalArtifactBytes = 64L * 1024 * 1024)
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
    IReadOnlyList<ClassicAuthoredUiAsset>? AuthoredUiAssets = null);

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
    short SourceYOffset)
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
    }
}

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
    bool IsHeaderless)
{
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

/// <summary>Typed, renderer-free FONT0003 glyph metrics and generated atlas identity.</summary>
public sealed record ClassicFontManifest(
    string MediaId,
    ushort FixedWidth,
    ushort FixedHeight,
    IReadOnlyList<ClassicFontGlyphMetric> Glyphs)
{
    internal void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(MediaId, nameof(MediaId));
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
    IReadOnlyList<ClassicWeaponActionManifest> WeaponActions,
    IReadOnlyList<ClassicEffectManifest> Effects,
    IReadOnlyList<ClassicAudioManifest> Audio,
    IReadOnlyList<ClassicUiImageManifest> UiImages,
    IReadOnlyList<ClassicInventoryIconManifest> InventoryIcons,
    ClassicFontManifest Font,
    IReadOnlyList<ClassicAuthoredUiAssetManifest> AuthoredUiAssets)
{
    private const int WeaponReferenceWidth = 320;
    private const int WeaponReferenceHeight = 200;
    private const int FontCellSize = 16;

    private static readonly WeaponActionSource[] WeaponActionSources =
    [
        new(ClassicDaggerWeaponAction.Idle, ClassicWeaponScreenAlignment.Right, 0.04F, true),
        new(ClassicDaggerWeaponAction.StrikeDown, ClassicWeaponScreenAlignment.Right, 0F, false),
        new(ClassicDaggerWeaponAction.StrikeDownLeft, ClassicWeaponScreenAlignment.Right, 0F, false),
        new(ClassicDaggerWeaponAction.StrikeLeft, ClassicWeaponScreenAlignment.Right, 0F, false),
        new(ClassicDaggerWeaponAction.StrikeRight, ClassicWeaponScreenAlignment.Left, 0F, false),
        new(ClassicDaggerWeaponAction.StrikeDownRight, ClassicWeaponScreenAlignment.Left, 0F, false),
        new(ClassicDaggerWeaponAction.StrikeUp, ClassicWeaponScreenAlignment.Right, 0F, false),
    ];

    private static readonly EffectSource[] EffectSources =
    [
        new(ClassicEffect.Blood0, "effect.blood.0", 0),
        new(ClassicEffect.Blood1, "effect.blood.1", 1),
        new(ClassicEffect.Blood2, "effect.blood.2", 2),
        new(ClassicEffect.MagicSparkle, "effect.sparkle.magic", 3),
    ];

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

        ResolvedProfile resolved = ResolveProfile(profile);
        SourceBytes source = SourceBytes.From(inputs, effectiveOptions.MaximumSourceBytes);
        Arena2Palette artPalette = PaletteDecoder.Decode(source.ArtPalette, "arena2/ART_PAL.COL");
        Arena2Palette palette = PaletteDecoder.Decode(source.Palette, "arena2/PAL.PAL");
        List<GeneratedMediaArtifact> generated = [];

        (GeneratedMediaArtifact weapon, ClassicWeaponActionManifest[] actions) = BuildWeapon(source.Weapon02Cif, artPalette, resolved, effectiveOptions);
        generated.Add(weapon);
        generated.AddRange(BuildEffects(source.Texture380, palette, resolved, effectiveOptions, out ClassicEffectManifest[] effects));
        generated.AddRange(BuildAudio(source.DaggerSound, effectiveOptions, out ClassicAudioManifest[] audio));
        generated.AddRange(BuildUi(source, artPalette, resolved, effectiveOptions, out ClassicUiImageManifest[] uiImages));
        generated.AddRange(BuildInventoryIcons(source, artPalette, resolved, effectiveOptions, out ClassicInventoryIconManifest[] inventoryIcons));
        (GeneratedMediaArtifact fontArtifact, ClassicFontManifest font) = BuildFont(source.Font0003Fnt, resolved.FontMediaId, effectiveOptions);
        generated.Add(fontArtifact);
        generated.AddRange(BuildAuthoredUi(resolved, effectiveOptions, out ClassicAuthoredUiAssetManifest[] authoredUiAssets));

        if (generated.Sum(artifact => artifact.Bytes.LongLength) > effectiveOptions.MaximumTotalArtifactBytes)
        {
            throw new InvalidOperationException("Classic media closure exceeds the total encoded-byte quota.");
        }

        NormalizedMediaManifest mediaManifest = MediaManifestNormalizer.Normalize(
            generated,
            BuildOverlays(resolved),
            effectiveOptions.MaximumArtifactBytes);
        ImportPublicationArtifact[] artifacts = generated
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact => new ImportPublicationArtifact(artifact.RelativePath, artifact.Bytes))
            .ToArray();
        LogicalSourceRecord? authoredManifestSource = CreateAuthoredManifestSource(resolved.AuthoredUiManifest, effectiveOptions.MaximumSourceBytes);
        return new(
            artifacts,
            mediaManifest,
            MergeSources(source.LogicalSources, resolved.AuthoredUiAssets, authoredManifestSource),
            authoredManifestSource,
            actions,
            effects,
            audio,
            uiImages,
            inventoryIcons,
            font,
            authoredUiAssets);
    }

    private static (GeneratedMediaArtifact Artifact, ClassicWeaponActionManifest[] Actions) BuildWeapon(
        ReadOnlySpan<byte> weaponBytes,
        Arena2Palette palette,
        ResolvedProfile profile,
        Arena2ClassicMediaPublicationOptions options)
    {
        WeaponCifArchive weapon = WeaponCifArchive.Parse(weaponBytes, "arena2/WEAPON02.CIF");
        if (weapon.RecordCount != WeaponActionSources.Length)
        {
            throw new Arena2FormatException("arena2/WEAPON02.CIF", 0, $"classic dagger requires exactly {WeaponActionSources.Length} action records, got {weapon.RecordCount}");
        }

        List<DecodedSpriteFrame> frames = [];
        List<ClassicWeaponActionManifest> actions = [];
        for (int record = 0; record < WeaponActionSources.Length; record++)
        {
            WeaponActionSource sourceMapping = WeaponActionSources[record];
            ClassicWeaponActionPresentation mapping = profile.WeaponActions[sourceMapping.Action];
            WeaponCifRecordInfo info = weapon.GetRecordInfo(record);
            int start = frames.Count;
            for (int frame = 0; frame < info.FrameCount; frame++)
            {
                IndexedWeaponCifFrame decoded = weapon.DecodeFrame(record, frame);
                frames.Add(new($"{profile.WeaponMediaId}/{frames.Count:D2}", WeaponReferenceWidth, WeaponReferenceHeight,
                    PlaceWeaponFrame(decoded, palette, mapping.Alignment, mapping.ScreenOffset)));
            }

            ClassicWeaponActionManifest action = new(
                sourceMapping.Action,
                record,
                start,
                info.FrameCount,
                mapping.Alignment,
                mapping.ScreenOffset,
                mapping.Timing,
                info.XOffset,
                info.YOffset);
            action.Validate(frames.Count);
            actions.Add(action);
        }

        if (frames.Count != 31)
        {
            throw new Arena2FormatException("arena2/WEAPON02.CIF", 0, $"classic dagger action table requires exactly 31 frames, got {frames.Count}");
        }

        NormalizedSpriteAtlas atlas = SpriteAtlasNormalizer.Normalize(
            frames,
            SpriteAtlasOptions.FixedCellGrid(options.MaximumAtlasDimension, WeaponReferenceWidth, WeaponReferenceHeight, bottomAlign: true));
        return (GeneratedMediaArtifact.FromAtlas(profile.WeaponMediaId, NormalizedMediaKind.WeaponSprite, $"media/combat/{Slug(profile.WeaponMediaId)}-atlas.png", atlas), actions.ToArray());
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
        SoundArchive sounds = SoundArchive.Parse(soundBytes, "arena2/DAGGER.SND");
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
            IndexedImg image = sources.DecodeUi(mapping.SourceFile);
            byte[] png = EncodePalettePng(image.Width, image.Height, image.Pixels.Span, palette);
            RequireArtifactQuota(png, options, mapping.MediaId);
            result.Add(new(mapping.MediaId, NormalizedMediaKind.UserInterface, $"media/ui/{Slug(mapping.MediaId)}.png", png, image.Width, image.Height, null, "image/png"));
            ClassicUiImageManifest manifest = new(mapping.Image, mapping.MediaId, mapping.SourceFile, image.XOffset, image.YOffset, image.IsHeaderless);
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
        string mediaId,
        Arena2ClassicMediaPublicationOptions options)
    {
        FntFont font = FntDecoder.Decode(fontBytes, "arena2/FONT0003.FNT");
        if (font.Glyphs.Count != Arena2FormatConstants.FntGlyphCount)
        {
            throw new Arena2FormatException("arena2/FONT0003.FNT", 0, $"classic font requires {Arena2FormatConstants.FntGlyphCount} glyphs");
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
        ClassicFontManifest manifest = new(mediaId, font.FixedWidth, font.FixedHeight, glyphs);
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

            RequireArtifactQuota(asset.PngBytes, options, asset.Id);
            artifacts.Add(new(asset.Id, NormalizedMediaKind.UserInterface, asset.RelativePath, asset.PngBytes, width, height, null, "image/png"));
            metadata.Add(new(asset.Id, asset.RelativePath, asset.SourceLabel, asset.Generator, asset.Prompt));
        }

        manifests = metadata.ToArray();
        return artifacts;
    }

    private static IReadOnlyList<AuthoredMediaOverlay> BuildOverlays(ResolvedProfile profile)
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

        return overlays;
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
        IReadOnlyList<ClassicWeaponActionPresentation> weaponActions = profile.WeaponActions ?? WeaponActionSources
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
        int x = alignment == ClassicWeaponScreenAlignment.Left ? offset : available - offset;
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

    private sealed record WeaponActionSource(ClassicDaggerWeaponAction Action, ClassicWeaponScreenAlignment Alignment, float ScreenOffset, bool Loop);
    private sealed record EffectSource(ClassicEffect Effect, string MediaId, int SourceRecordOrdinal);
    private sealed record AudioSource(ClassicDaggerAudioClip Clip, string MediaId, int SourceRecordOrdinal);
    private sealed record UiImageSource(ClassicUiImage Image, string MediaId, string FileName, bool IsHeaderless);
    private sealed record InventoryIconSource(string ItemId, int TextureArchive, int SourceRecordOrdinal);

    private sealed class SourceBytes
    {
        private SourceBytes(Arena2ClassicMediaInputs inputs, IReadOnlyList<LogicalSourceRecord> logicalSources)
        {
            Weapon02Cif = inputs.Weapon02Cif;
            ArtPalette = inputs.ArtPalette;
            Texture380 = inputs.Texture380;
            Palette = inputs.Palette;
            DaggerSound = inputs.DaggerSound;
            Main00I0Img = inputs.Main00I0Img;
            Main03I0Img = inputs.Main03I0Img;
            Main04I0Img = inputs.Main04I0Img;
            Main05I0Img = inputs.Main05I0Img;
            Inve00I0Img = inputs.Inve00I0Img;
            Info00I0Img = inputs.Info00I0Img;
            Texture207 = inputs.Texture207;
            Texture216 = inputs.Texture216;
            Texture234 = inputs.Texture234;
            Texture245 = inputs.Texture245;
            Font0003Fnt = inputs.Font0003Fnt;
            LogicalSources = logicalSources;
        }

        public byte[] Weapon02Cif { get; }
        public byte[] ArtPalette { get; }
        public byte[] Texture380 { get; }
        public byte[] Palette { get; }
        public byte[] DaggerSound { get; }
        public byte[] Main00I0Img { get; }
        public byte[] Main03I0Img { get; }
        public byte[] Main04I0Img { get; }
        public byte[] Main05I0Img { get; }
        public byte[] Inve00I0Img { get; }
        public byte[] Info00I0Img { get; }
        public byte[] Texture207 { get; }
        public byte[] Texture216 { get; }
        public byte[] Texture234 { get; }
        public byte[] Texture245 { get; }
        public byte[] Font0003Fnt { get; }
        public IReadOnlyList<LogicalSourceRecord> LogicalSources { get; }

        public static SourceBytes From(Arena2ClassicMediaInputs inputs, long maximumSourceBytes)
        {
            (string FileName, byte[] Bytes)[] sources =
            [
                ("WEAPON02.CIF", inputs.Weapon02Cif), ("ART_PAL.COL", inputs.ArtPalette), ("TEXTURE.380", inputs.Texture380), ("PAL.PAL", inputs.Palette),
                ("DAGGER.SND", inputs.DaggerSound), ("MAIN00I0.IMG", inputs.Main00I0Img), ("MAIN03I0.IMG", inputs.Main03I0Img),
                ("MAIN04I0.IMG", inputs.Main04I0Img), ("MAIN05I0.IMG", inputs.Main05I0Img), ("INVE00I0.IMG", inputs.Inve00I0Img),
                ("INFO00I0.IMG", inputs.Info00I0Img), ("TEXTURE.207", inputs.Texture207), ("TEXTURE.216", inputs.Texture216),
                ("TEXTURE.234", inputs.Texture234), ("TEXTURE.245", inputs.Texture245), ("FONT0003.FNT", inputs.Font0003Fnt),
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

        public IndexedImg DecodeUi(string fileName) => fileName switch
        {
            "MAIN00I0.IMG" => ImgDecoder.Decode(Main00I0Img, "arena2/MAIN00I0.IMG"),
            "MAIN03I0.IMG" => ImgDecoder.Decode(Main03I0Img, "arena2/MAIN03I0.IMG"),
            "MAIN04I0.IMG" => ImgDecoder.Decode(Main04I0Img, "arena2/MAIN04I0.IMG"),
            "MAIN05I0.IMG" => ImgDecoder.Decode(Main05I0Img, "arena2/MAIN05I0.IMG"),
            "INVE00I0.IMG" => ImgDecoder.DecodeHeaderlessUiCanvas(Inve00I0Img, "arena2/INVE00I0.IMG"),
            "INFO00I0.IMG" => ImgDecoder.DecodeHeaderlessUiCanvas(Info00I0Img, "arena2/INFO00I0.IMG"),
            _ => throw new ArgumentOutOfRangeException(nameof(fileName)),
        };
    }
}
