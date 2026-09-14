using System.Text;
using System.IO.Compression;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2ClassicMediaPublicationTests
{
    [Fact]
    public void RegeneratesClosedClassicMediaBytesAndTypedSemanticMappings()
    {
        Arena2ClassicMediaInputs inputs = CreateInputs();

        Arena2ClassicMediaPublication first = Arena2ClassicMediaPublication.Create(inputs);
        Arena2ClassicMediaPublication second = Arena2ClassicMediaPublication.Create(inputs);

        // Five service screens and their donor companions are thirteen more artifacts and resources:
        // naming an image admits it.
        Assert.Equal(76, first.Artifacts.Count);
        Assert.Equal(76, first.MediaManifest.Resources.Count);
        // Thirteen more admitted source files, because those images are read as well as named.
        Assert.Equal(43, first.Sources.Count);
        Assert.Equal(first.Artifacts.Select(artifact => artifact.RelativePath).OrderBy(path => path, StringComparer.Ordinal), first.Artifacts.Select(artifact => artifact.RelativePath));
        Assert.Equal(first.Artifacts.Select(artifact => artifact.RelativePath), second.Artifacts.Select(artifact => artifact.RelativePath));
        Assert.All(first.Artifacts.Zip(second.Artifacts), pair => Assert.Equal(pair.First.Bytes.ToArray(), pair.Second.Bytes.ToArray()));

        IReadOnlyList<ClassicWeaponActionManifest> daggerActions = WeaponActions(first, "weapon.dagger.steel");
        Assert.Equal(7, daggerActions.Count);
        Assert.Equal(31, daggerActions.Sum(action => action.FrameCount));
        Assert.Equal(ClassicDaggerWeaponAction.Idle, daggerActions[0].Action);
        Assert.True(daggerActions[0].Timing.Loop);
        Assert.All(daggerActions.Skip(1), action => Assert.False(action.Timing.Loop));
        Assert.All(daggerActions, action => Assert.Equal(10F, action.Timing.FramesPerSecond));
        Assert.Equal(9, first.WeaponMedia.Count);
        Assert.Equal(["weapon.axe", "weapon.bow", "weapon.dagger.steel", "weapon.flail", "weapon.longblade", "weapon.mace", "weapon.staff", "weapon.unarmed", "weapon.warhammer"], first.WeaponMedia.Select(weapon => weapon.ResourceId).OrderBy(id => id, StringComparer.Ordinal));
        ClassicWeaponMediaManifest bow = first.WeaponMedia.Single(weapon => weapon.ResourceId == "weapon.bow");
        Assert.All(bow.Actions, action => Assert.Equal(0, action.SourceRecordOrdinal));
        Assert.Equal(7, bow.Actions.Single(action => action.Action == ClassicDaggerWeaponAction.StrikeDown).FrameCount);
        Assert.Equal(4, bow.Actions.Single(action => action.Action == ClassicDaggerWeaponAction.StrikeUp).FrameCount);
        ClassicWeaponActionManifest unarmedLeft = first.WeaponMedia.Single(weapon => weapon.ResourceId == "weapon.unarmed").Actions.Single(action => action.Action == ClassicDaggerWeaponAction.StrikeLeft);
        Assert.Equal([unarmedLeft.FrameStart, unarmedLeft.FrameStart + 1, unarmedLeft.FrameStart + 2, unarmedLeft.FrameStart + 3, unarmedLeft.FrameStart + 4, unarmedLeft.FrameStart + 2, unarmedLeft.FrameStart + 1, unarmedLeft.FrameStart], unarmedLeft.Sequence);
        Assert.Equal(4, first.Effects.Count);
        Assert.All(first.Effects, effect => Assert.False(effect.Timing.Loop));
        Assert.Equal(6, first.Audio.Count);
// Six windows, one mode screen, and the service panels the donor windows read, each part of a
        // screen the donor composes from several images published under its own media identity.
        Assert.Equal(25, first.UiImages.Count);
        Assert.Equal(31, first.InventoryIcons.Count);
        Assert.Equal(240, first.Font.Glyphs.Count);

        byte[] weapon = Artifact(first, "media/combat/weapon-dagger-steel-atlas.png");
        AssertPng(weapon, 3840, 600);
        byte[] font = Artifact(first, "media/fonts/font-classic-0003-atlas.png");
        AssertPng(font, 256, 240);
        Assert.NotNull(first.MediaManifest.Resources.Single(resource => resource.Id == "weapon.dagger.steel").Frames);
        Assert.Equal(31, first.MediaManifest.Resources.Single(resource => resource.Id == "weapon.dagger.steel").Frames.Count);
        Assert.Contains(first.Artifacts, artifact => artifact.RelativePath == "media/ui/inventory-icons/inventory-icon-iron-dagger.png");
        Assert.Contains(first.Artifacts, artifact => artifact.RelativePath == "media/ui/inventory-icons/inventory-icon-arrow.png");

        byte[] wave = Artifact(first, "media/audio/audio-melee-dagger-swing.wav");
        AssertWave(wave);
        Assert.Equal("arena2/DAGGER.SND", first.Sources.Single(source => source.SourcePath == "arena2/DAGGER.SND").SourcePath);
    }

    [Fact]
    public void PublishesAuthoredUiArtAsABoundedDerivativeOfItsSource()
    {
        // A 1254x1254 opaque plate, the shape the authored inventory skins actually have.
        byte[] source = SolidPng(1254, 1254, [204, 187, 136, 255]);
        Arena2ClassicMediaPublication publication = Arena2ClassicMediaPublication.Create(CreateInputs(), AuthoredProfile(
            new ClassicAuthoredUiAsset("inventory.skin.panel-slate.v1", "media/ui/authored/inventory-skin-panel-slate-v1.png", "ui-original/inventory-panel-slate-v1.png", source, "generator", "prompt")));

        NormalizedMediaDescriptor published = publication.MediaManifest.Resources.Single(resource => resource.Id == "inventory.skin.panel-slate.v1");
        Assert.Equal((256, 256), (published.SourceWidth, published.SourceHeight));
        byte[] bytes = Artifact(publication, "media/ui/authored/inventory-skin-panel-slate-v1.png");
        Assert.True(bytes.Length < 512 * 1024, $"the published skin is {bytes.Length} bytes");
        DeterministicPngImage decoded = DeterministicPngReader.ReadRgba8(bytes, "published skin");
        Assert.Equal((256, 256), (decoded.Width, decoded.Height));
        Assert.Equal([204, 187, 136, 255], decoded.Rgba[..4]);
        // The source is untouched: the publication derives an artifact, it does not rewrite the operator's file.
        Assert.Equal(1254, DeterministicPngReader.ReadRgba8(source, "source").Width);
    }

    [Fact]
    public void RefusesAnAuthoredUiArtifactPastTheDomDecorationBound()
    {
        // A noise plate does not compress, so a bound that only counted pixels would let it through.
        byte[] noisy = NoisePng(1024, 1024);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Arena2ClassicMediaPublication.Create(
            CreateInputs(),
            AuthoredProfile(new ClassicAuthoredUiAsset("inventory.skin.noisy", "media/ui/authored/noisy.png", "ui-original/noisy.png", noisy, "generator", "prompt")),
            new Arena2ClassicMediaPublicationOptions(AuthoredUiMaximumArtifactBytes: 4096)));
        Assert.Contains("inventory.skin.noisy", failure.Message, StringComparison.Ordinal);
        Assert.Contains("4096-byte bound", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAScreenWhoseOwnPaletteCannotBeRead()
    {
        // A screen of the right canvas shape but not the documented file length has no palette to read,
        // and the publication must say so - naming the screen - rather than fall back to the shared art
        // palette and publish every colour wrong while looking successful.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            Daggerfall.Import.Normalization.Arena2ClassicMediaPublication.Create(CreateInputs(new byte[320 * 200])));

        Assert.True(failure.Message.Contains("DIE_00I0", StringComparison.Ordinal) && failure.Message.Contains("palette", StringComparison.Ordinal),
            $"the refusal must name the screen and the palette it could not read: {failure.Message}");
    }

    [Fact]
    public void PublishesOneSemanticSlotPerAdmittedUiImage()
    {
        Arena2ClassicMediaPublication first = Arena2ClassicMediaPublication.Create(CreateInputs());
        Arena2ClassicMediaPublication second = Arena2ClassicMediaPublication.Create(CreateInputs());

        // Every role in the admitted closure is published as one image, so "every image fills a slot"
        // is a statement about the closure rather than about the images someone remembered to add.
        Assert.Equal(
            Enum.GetValues<ClassicUiImage>().OrderBy(image => image),
            first.UiImages.Select(image => image.Image).OrderBy(image => image));

        // The slots a consumer binds: the windows the product already drew, the service screens the
        // donor windows read, and the mode screens. A slot names a screen, and a screen the donor
        // composes from several images fills that slot with each part.
        Assert.Equal(
            [
                ClassicUiSlot.HudChrome, ClassicUiSlot.HudVitalHealth, ClassicUiSlot.HudVitalFatigue,
                ClassicUiSlot.HudVitalMagicka, ClassicUiSlot.Inventory, ClassicUiSlot.CharacterSheet,
                ClassicUiSlot.Book, ClassicUiSlot.Rest, ClassicUiSlot.Merchant, ClassicUiSlot.Guild,
                ClassicUiSlot.Bank, ClassicUiSlot.Death,
                ClassicUiSlot.CharacterGeneration, ClassicUiSlot.Pick, ClassicUiSlot.StartMenu, ClassicUiSlot.Prison, ClassicUiSlot.Title,
            ],
            first.UiImages.Select(image => image.Slot).Distinct().OrderBy(slot => slot));
        // The five supplied screens carry their own palettes, so each fills a slot of its own; the pick
        // screen keeps the pick slot, and the start-menu and prison screens have theirs.
        Assert.Equal(
            ["screen.character-generation", "screen.pick.02", "screen.prison", "screen.start-menu", "screen.title"],
            first.UiImages.Where(image => image.Slot is ClassicUiSlot.CharacterGeneration or ClassicUiSlot.Pick or ClassicUiSlot.StartMenu or ClassicUiSlot.Prison or ClassicUiSlot.Title)
                .Select(image => image.MediaId)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["window.rest.hours-past", "window.rest.hours-remaining", "window.rest.panel"],
            first.UiImages.Where(image => image.Slot == ClassicUiSlot.Rest).Select(image => image.MediaId).Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "window.merchant.buttons.buy", "window.merchant.buttons.identify", "window.merchant.buttons.repair",
                "window.merchant.buttons.sell", "window.merchant.buttons.sell-gold", "window.merchant.cost",
            ],
            first.UiImages.Where(image => image.Slot == ClassicUiSlot.Merchant).Select(image => image.MediaId).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["window.guild.member", "window.guild.service"],
            first.UiImages.Where(image => image.Slot == ClassicUiSlot.Guild).Select(image => image.MediaId).Order(StringComparer.Ordinal));
        // Book and bank are one donor image each, so their slots stay single; the companions above are
        // the difference between a screen and a fragment.
        Assert.Single(first.UiImages, image => image.Slot == ClassicUiSlot.Book);
        Assert.Single(first.UiImages, image => image.Slot == ClassicUiSlot.Bank);

        // A slot travels with the media identity and the exact source record it was decoded from, and
        // the publication emits the artifact under that identity.
        ClassicUiImageManifest book = first.UiImages.Single(image => image.Slot == ClassicUiSlot.Book);
        Assert.Equal("window.book.reader", book.MediaId);
        Assert.Equal("BOOK00I0.IMG", book.SourceFile);
        Assert.Contains(first.Artifacts, artifact => artifact.MediaId == book.MediaId);
        Assert.Equal(
            first.UiImages.Select(image => (image.Image, image.Slot, image.MediaId)),
            second.UiImages.Select(image => (image.Image, image.Slot, image.MediaId)));
    }

    [Fact]
    public void CentersUnarmedFramesOnTheClassicCanvas()
    {
        Arena2ClassicMediaPublication publication = Arena2ClassicMediaPublication.Create(CreateInputs());
        NormalizedMediaDescriptor unarmed = publication.MediaManifest.Resources.Single(resource => resource.Id == "weapon.unarmed");
        ClassicWeaponActionManifest left = WeaponActions(publication, "weapon.unarmed").Single(action => action.Action == ClassicDaggerWeaponAction.StrikeLeft);
        NormalizedAtlasFrame frame = unarmed.Frames.Single(value => value.FrameIndex == left.FrameStart);
        byte[] atlas = Artifact(publication, "media/combat/weapon-unarmed-atlas.png");

        Assert.Equal(255, ReadPngAlpha(atlas, unarmed.AtlasWidth, frame.X + ((320 - 1) / 2), frame.Y + 199));
        Assert.Equal(0, ReadPngAlpha(atlas, unarmed.AtlasWidth, frame.X + 319, frame.Y + 199));
    }

    [Fact]
    public void RejectsMalformedClassicSourcesAndFixedSourceTableGaps()
    {
        Arena2ClassicMediaInputs malformed = CreateInputs() with { Weapon02Cif = [1] };
        Assert.Throws<Arena2FormatException>(() => Arena2ClassicMediaPublication.Create(malformed));

        Arena2ClassicMediaInputs missingInventoryRecord = CreateInputs() with { Texture245 = CreateTextureArchive(1) };
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena2ClassicMediaPublication.Create(missingInventoryRecord));
    }

    [Fact]
    public void EnforcesAtlasAndArtifactByteQuotas()
    {
        Arena2ClassicMediaInputs inputs = CreateInputs();
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena2ClassicMediaPublication.Create(inputs, new Arena2ClassicMediaPublicationOptions(MaximumAtlasDimension: 319)));
        Assert.Throws<InvalidOperationException>(() => Arena2ClassicMediaPublication.Create(inputs, new Arena2ClassicMediaPublicationOptions(MaximumArtifactBytes: 1)));
        Assert.Throws<InvalidOperationException>(() => Arena2ClassicMediaPublication.Create(inputs, new Arena2ClassicMediaPublicationOptions(MaximumTotalArtifactBytes: 1)));
    }

    [Fact]
    public void ReappliesTypedSpriteOverlayDuringNormalClassicRegeneration()
    {
        Arena2ClassicMediaPublication baseline = Arena2ClassicMediaPublication.Create(CreateInputs());
        string effectId = baseline.Effects.Single(effect => effect.Effect == ClassicEffect.Blood0).MediaId;
        Arena2ClassicMediaProfile profile = new(AuthoredOverlays:
        [
            new AuthoredMediaOverlay(effectId, true, DisplayName: "Authored blood", Pivot: new(0.25F, 0.75F), Sequence: [0]),
        ]);

        Arena2ClassicMediaPublication regenerated = Arena2ClassicMediaPublication.Create(CreateInputs(), profile);
        NormalizedMediaDescriptor effect = regenerated.MediaManifest.Resources.Single(resource => resource.Id == effectId);
        Assert.Equal("Authored blood", effect.DisplayName);
        Assert.Equal(new NormalizedVector2(0.25F, 0.75F), effect.Pivot);
        Assert.Equal(10F, effect.FramesPerSecond);
        Assert.False(effect.Loop);
        Assert.Equal([0], effect.Sequence);
    }

    [Fact]
    public void ReappliesEffectTimingOverlayToBothDescriptorAndCanonicalEffectManifest()
    {
        Arena2ClassicMediaPublication baseline = Arena2ClassicMediaPublication.Create(CreateInputs());
        string effectId = baseline.Effects.Single(effect => effect.Effect == ClassicEffect.Blood0).MediaId;

        Arena2ClassicMediaPublication regenerated = Arena2ClassicMediaPublication.Create(CreateInputs(), new Arena2ClassicMediaProfile(AuthoredOverlays:
        [
            new AuthoredMediaOverlay(effectId, true, FramesPerSecond: 7F, Loop: true),
        ]));

        NormalizedMediaDescriptor descriptor = regenerated.MediaManifest.Resources.Single(resource => resource.Id == effectId);
        ClassicEffectManifest effect = regenerated.Effects.Single(value => value.MediaId == effectId);
        Assert.Equal(7F, descriptor.FramesPerSecond);
        Assert.True(descriptor.Loop);
        Assert.Equal(7F, effect.Timing.FramesPerSecond);
        Assert.True(effect.Timing.Loop);
    }

    [Fact]
    public void ReappliesPerActionTimingOverlayWithoutChangingOtherClassicActions()
    {
        Arena2ClassicMediaPublication baseline = Arena2ClassicMediaPublication.Create(CreateInputs());
        string weaponId = "weapon.dagger.steel";
        ClassicWeaponActionManifest baselineStrike = WeaponActions(baseline, "weapon.dagger.steel").Single(action => action.Action == ClassicDaggerWeaponAction.StrikeDown);

        Arena2ClassicMediaPublication regenerated = Arena2ClassicMediaPublication.Create(CreateInputs(), new Arena2ClassicMediaProfile(AuthoredOverlays:
        [
            new AuthoredMediaOverlay(weaponId, true, ActionTimings: [new(ClassicDaggerWeaponAction.Idle.ToString(), 7F, false)]),
        ]));

        ClassicWeaponActionManifest idle = WeaponActions(regenerated, "weapon.dagger.steel").Single(action => action.Action == ClassicDaggerWeaponAction.Idle);
        ClassicWeaponActionManifest strike = WeaponActions(regenerated, "weapon.dagger.steel").Single(action => action.Action == ClassicDaggerWeaponAction.StrikeDown);
        Assert.Equal(7F, idle.Timing.FramesPerSecond);
        Assert.False(idle.Timing.Loop);
        Assert.Equal(baselineStrike.Timing, strike.Timing);
    }

    [Fact]
    public void RejectsUnownedExternalClassicOverlayAndResourceWideWeaponTiming()
    {
        Arena2ClassicMediaPublication baseline = Arena2ClassicMediaPublication.Create(CreateInputs());
        string effectId = baseline.Effects.Single(effect => effect.Effect == ClassicEffect.Blood0).MediaId;
        string weaponId = "weapon.dagger.steel";

        Assert.Throws<ArgumentException>(() => Arena2ClassicMediaPublication.Create(CreateInputs(), new Arena2ClassicMediaProfile(AuthoredOverlays:
        [
            new AuthoredMediaOverlay(effectId, false, DisplayName: "Unowned"),
        ])));
        Assert.Throws<ArgumentException>(() => Arena2ClassicMediaPublication.Create(CreateInputs(), new Arena2ClassicMediaProfile(AuthoredOverlays:
        [
            new AuthoredMediaOverlay(weaponId, true, FramesPerSecond: 7F),
        ])));
    }

    [Fact]
    public void ProfileOwnsAdjustablePresentationAndAdmittedUiInventoryMappings()
    {
        Arena2ClassicMediaInputs inputs = CreateInputs();
        Arena2ClassicMediaPublication baseline = Arena2ClassicMediaPublication.Create(inputs);
        Arena2ClassicMediaProfile profile = new(
            WeaponMediaId: "weapon.dagger.profiled",
            WeaponActions: WeaponActions(baseline, "weapon.dagger.steel")
                .Select(action => new ClassicWeaponActionPresentation(
                    action.Action,
                    action.Action == ClassicDaggerWeaponAction.Idle ? ClassicWeaponScreenAlignment.Left : action.Alignment,
                    action.Action == ClassicDaggerWeaponAction.Idle ? 0F : action.ScreenOffset,
                    action.Action == ClassicDaggerWeaponAction.Idle ? new ClassicSpriteTiming(12F, true) : action.Timing))
                .ToArray(),
            Effects: baseline.Effects
                .Select(effect => new ClassicEffectPresentation(
                    effect.Effect,
                    effect.Effect == ClassicEffect.Blood0 ? "effect.blood.profiled" : effect.MediaId,
                    effect.Effect == ClassicEffect.Blood0 ? new ClassicSpriteTiming(6F, false) : effect.Timing))
                .ToArray(),
            UiImages: baseline.UiImages
                .Select(image => new ClassicUiImagePresentation(
                    image.Image,
                    image.MediaId,
                    image.Image == ClassicUiImage.HudChromeMain ? "MAIN03I0.IMG" : image.SourceFile))
                .ToArray(),
            InventoryIcons: baseline.InventoryIcons
                .Select(icon => new ClassicInventoryIconPresentation(
                    icon.ItemId,
                    icon.ItemId == "iron-dagger" ? "inventory.icon.profiled-dagger" : icon.MediaId,
                    icon.TextureArchive,
                    icon.SourceRecordOrdinal))
                .ToArray(),
            Presentation:
            [
                new ClassicMediaPresentation("weapon.dagger.profiled", "Profiled dagger", new(0.5F, 0F), new(1F, 2F)),
                new ClassicMediaPresentation("effect.blood.profiled", "Profiled blood", new(0.5F, 0.5F)),
            ]);

        Arena2ClassicMediaPublication publication = Arena2ClassicMediaPublication.Create(inputs, profile);

        Assert.Equal("weapon.dagger.profiled", publication.MediaManifest.Resources.Single(resource => resource.Id == "weapon.dagger.profiled").Id);
        ClassicWeaponActionManifest idle = WeaponActions(publication, "weapon.dagger.profiled").Single(action => action.Action == ClassicDaggerWeaponAction.Idle);
        Assert.Equal(ClassicWeaponScreenAlignment.Left, idle.Alignment);
        Assert.Equal(12F, idle.Timing.FramesPerSecond);
        Assert.Equal("MAIN03I0.IMG", publication.UiImages.Single(image => image.Image == ClassicUiImage.HudChromeMain).SourceFile);
        Assert.Equal("inventory.icon.profiled-dagger", publication.InventoryIcons.Single(icon => icon.ItemId == "iron-dagger").MediaId);

        // The death screen is named and published, and it is the one UI image drawn with its own
        // palette rather than the publication's.
        ClassicUiImageManifest screen = publication.UiImages.Single(image => image.Image == ClassicUiImage.ScreenDeath);
        Assert.Equal("screen.death", screen.MediaId);
        Assert.Equal("DIE_00I0.IMG", screen.SourceFile);
        Assert.Contains(publication.Artifacts, artifact => artifact.RelativePath == "media/ui/screen-death.png");

        NormalizedMediaDescriptor effect = publication.MediaManifest.Resources.Single(resource => resource.Id == "effect.blood.profiled");
        Assert.Equal(6F, effect.FramesPerSecond);
        Assert.False(effect.Loop!.Value);
        Assert.Equal("Profiled blood", effect.DisplayName);
        Assert.Contains(publication.Artifacts, artifact => artifact.RelativePath == "media/combat/weapon-dagger-profiled-atlas.png");
        Assert.Contains(publication.Artifacts, artifact => artifact.RelativePath == "media/ui/inventory-icons/inventory-icon-iron-dagger.png");
    }

    [Fact]
    public void PreservesTypedAuthoredUiBytesAndPortableManifestProvenance()
    {
        byte[] png = DeterministicPngEncoder.EncodeRgba8(1, 1, [10, 20, 30, 255]);
        Arena2ClassicMediaProfile profile = new(
            Presentation: [new ClassicMediaPresentation("inventory.skin.panel-slate.v1", "Slate panel", new(0.5F, 0.5F), new(4F, 2F))],
            AuthoredUiManifest: new("ui-authored-assets.json", "{\"schemaVersion\":1}"u8.ToArray()),
            AuthoredUiAssets:
            [
                new ClassicAuthoredUiAsset(
                    "inventory.skin.panel-slate.v1",
                    "media/ui/authored/inventory-skin-panel-slate-v1.png",
                    "ui-original/inventory-panel-slate-v1.png",
                    png,
                    "test-generator",
                    "original test panel"),
            ]);

        Arena2ClassicMediaPublication publication = Arena2ClassicMediaPublication.Create(CreateInputs(), profile);

        Assert.Equal(png, Artifact(publication, "media/ui/authored/inventory-skin-panel-slate-v1.png"));
        Assert.Equal("authored-ui/ui-authored-assets.json", publication.AuthoredUiManifestSource!.SourcePath);
        Assert.Contains(publication.Sources, source => source.SourcePath == "authored-ui/ui-original/inventory-panel-slate-v1.png");
        ClassicAuthoredUiAssetManifest asset = Assert.Single(publication.AuthoredUiAssets);
        Assert.Equal("ui-original/inventory-panel-slate-v1.png", asset.SourceLabel);
        NormalizedMediaDescriptor descriptor = publication.MediaManifest.Resources.Single(resource => resource.Id == asset.Id);
        Assert.Equal(asset.RelativePath, descriptor.RelativePath);
        Assert.Equal(ContentDigest.Compute(png), descriptor.ContentDigest);
        Assert.Equal("Slate panel", descriptor.DisplayName);
    }

    [Fact]
    public void RejectsInvalidProfileAndAuthoredUiInputs()
    {
        Arena2ClassicMediaProfile incompleteActions = new(WeaponActions: [new(ClassicDaggerWeaponAction.Idle, ClassicWeaponScreenAlignment.Left, 0F, new ClassicSpriteTiming(10F, true))]);
        Assert.Throws<ArgumentException>(() => Arena2ClassicMediaPublication.Create(CreateInputs(), incompleteActions));

        Arena2ClassicMediaProfile invalidPng = new(AuthoredUiAssets:
        [
            new ClassicAuthoredUiAsset("inventory.skin.invalid", "media/ui/authored/invalid.png", "ui-original/invalid.png", [1], "generator", "prompt"),
        ]);
        Assert.Throws<ArgumentException>(() => Arena2ClassicMediaPublication.Create(CreateInputs(), invalidPng));

        Arena2ClassicMediaProfile collidingProvenance = new(
            AuthoredUiManifest: new("ui-original/same.png", [1]),
            AuthoredUiAssets:
            [
                new ClassicAuthoredUiAsset("inventory.skin.same", "media/ui/authored/same.png", "ui-original/same.png", DeterministicPngEncoder.EncodeRgba8(1, 1, [0, 0, 0, 0]), "generator", "prompt"),
            ]);
        Assert.Throws<ArgumentException>(() => Arena2ClassicMediaPublication.Create(CreateInputs(), collidingProvenance));
    }

    [Fact]
    public void RegeneratesTheSelectedClosureFromOperatorSuppliedArena2WhenAvailable()
    {
        string arena2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../local/arena2"));
        if (!new[] { "WEAPON01.CIF", "WEAPON02.CIF", "WEAPON04.CIF", "WEAPON05.CIF", "WEAPON06.CIF", "WEAPON07.CIF", "WEAPON08.CIF", "WEAPON09.CIF", "WEAPON10.CIF" }.All(file => File.Exists(Path.Combine(arena2, file)))) return;

        Arena2ClassicMediaPublication publication = Arena2ClassicMediaPublication.Create(new(
            Read(arena2, "WEAPON01.CIF"), Read(arena2, "WEAPON02.CIF"), Read(arena2, "WEAPON04.CIF"), Read(arena2, "WEAPON05.CIF"), Read(arena2, "WEAPON06.CIF"), Read(arena2, "WEAPON07.CIF"), Read(arena2, "WEAPON08.CIF"), Read(arena2, "WEAPON09.CIF"), Read(arena2, "WEAPON10.CIF"),
            Read(arena2, "ART_PAL.COL"), Read(arena2, "TEXTURE.380"), Read(arena2, "PAL.PAL"), Read(arena2, "DAGGER.SND"),
            Read(arena2, "MAIN00I0.IMG"), Read(arena2, "MAIN03I0.IMG"), Read(arena2, "MAIN04I0.IMG"), Read(arena2, "MAIN05I0.IMG"), Read(arena2, "INVE00I0.IMG"), Read(arena2, "INFO00I0.IMG"), Read(arena2, "DIE_00I0.IMG"),
            Read(arena2, "CHGN00I0.IMG"), Read(arena2, "PICK02I0.IMG"), Read(arena2, "PICK03I0.IMG"), Read(arena2, "PRIS00I0.IMG"), Read(arena2, "TITL00I0.IMG"),
            Read(arena2, "BOOK00I0.IMG"), Read(arena2, "REST00I0.IMG"), Read(arena2, "SHOP00I0.IMG"), Read(arena2, "GILD00I0.IMG"), Read(arena2, "BANK00I0.IMG"),
            Read(arena2, "REST01I0.IMG"), Read(arena2, "REST02I0.IMG"), Read(arena2, "INVE08I0.IMG"), Read(arena2, "INVE10I0.IMG"), Read(arena2, "INVE11I0.IMG"),
            Read(arena2, "INVE12I0.IMG"), Read(arena2, "INVE14I0.IMG"), Read(arena2, "GILD01I0.IMG"),
            Read(arena2, "TEXTURE.207"), Read(arena2, "TEXTURE.216"), Read(arena2, "TEXTURE.234"), Read(arena2, "TEXTURE.245"), Read(arena2, "FONT0003.FNT")));

        Assert.Equal(31, WeaponActions(publication, "weapon.dagger.steel").Sum(action => action.FrameCount));
        Assert.Equal(76, publication.Artifacts.Count);
        AssertPng(Artifact(publication, "media/combat/weapon-dagger-steel-atlas.png"), 3840, 600);
        Assert.All(publication.Audio, clip => Assert.Equal(11_025U, clip.SampleRate));
    }

    private static byte[] Artifact(Arena2ClassicMediaPublication publication, string path) => publication.Artifacts.Single(artifact => artifact.RelativePath == path).Bytes.ToArray();

    private static IReadOnlyList<ClassicWeaponActionManifest> WeaponActions(Arena2ClassicMediaPublication publication, string resourceId) =>
        publication.WeaponMedia.Single(weapon => weapon.ResourceId == resourceId).Actions;

    private static void AssertPng(byte[] png, int width, int height)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(width, ReadBigEndian(png, 16));
        Assert.Equal(height, ReadBigEndian(png, 20));
    }

    private static byte ReadPngAlpha(byte[] png, int width, int x, int y)
    {
        int position = 8;
        byte[]? compressed = null;
        while (position < png.Length)
        {
            int length = ReadBigEndian(png, position);
            string kind = Encoding.ASCII.GetString(png, position + 4, 4);
            if (kind == "IDAT")
            {
                compressed = png.AsSpan(position + 8, length).ToArray();
                break;
            }

            position += checked(length + 12);
        }

        Assert.NotNull(compressed);
        using MemoryStream input = new(compressed!);
        using ZLibStream zlib = new(input, CompressionMode.Decompress);
        using MemoryStream scanlines = new();
        zlib.CopyTo(scanlines);
        int pixel = checked((y * (width * 4 + 1)) + 1 + (x * 4));
        return scanlines.GetBuffer()[pixel + 3];
    }

    private static void AssertWave(byte[] wave)
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(1, ReadLittleEndian16(wave, 20));
        Assert.Equal(1, ReadLittleEndian16(wave, 22));
        Assert.Equal(11_025, ReadLittleEndian32(wave, 24));
        Assert.Equal(8, ReadLittleEndian16(wave, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wave, 36, 4));
    }

    private static Arena2ClassicMediaInputs CreateInputs(byte[]? brokenScreen = null) => new(
        CreateWeaponCif(),
        CreateWeaponCif(),
        CreateWeaponCif(),
        CreateWeaponCif(),
        CreateWeaponCif(),
        CreateWeaponCif(),
        CreateWeaponCif(),
        CreateWeaponCif(7, includeWieldImage: false, animationRecordCount: 1),
        CreateWeaponCif(),
        CreatePalette(),
        CreateTextureArchive(4),
        CreatePalette(),
        CreateNumericBsa(113),
        CreateHeaderedImage(1),
        CreateHeaderedImage(2),
        CreateHeaderedImage(3),
        CreateHeaderedImage(4),
        new byte[320 * 200],
        new byte[320 * 200],
        // The death screen's own bytes, or a caller-supplied file that is not the documented shape, which
        // proves the publication refuses rather than painting a screen with the shared palette.
        brokenScreen ?? new byte[Daggerfall.Import.Arena2.ImgDecoder.EmbeddedPaletteScreenBytes],
        // The four remaining screens of the same shape: a 320x200 canvas with its own trailing palette.
        new byte[Daggerfall.Import.Arena2.ImgDecoder.EmbeddedPaletteScreenBytes],
        new byte[Daggerfall.Import.Arena2.ImgDecoder.EmbeddedPaletteScreenBytes],
        new byte[Daggerfall.Import.Arena2.ImgDecoder.EmbeddedPaletteScreenBytes],
        new byte[Daggerfall.Import.Arena2.ImgDecoder.EmbeddedPaletteScreenBytes],
        new byte[Daggerfall.Import.Arena2.ImgDecoder.EmbeddedPaletteScreenBytes],
        new byte[320 * 200],
        CreateHeaderedImage(5),
        CreateHeaderedImage(6),
        CreateHeaderedImage(7),
        CreateHeaderedImage(8),
        CreateHeaderedImage(9),
        CreateHeaderedImage(10),
        CreateHeaderedImage(11),
        CreateHeaderedImage(12),
        CreateHeaderedImage(13),
        CreateHeaderedImage(14),
        CreateHeaderedImage(15),
        CreateHeaderedImage(16),
        CreateTextureArchive(17),
        CreateTextureArchive(2),
        CreateTextureArchive(27),
        CreateTextureArchive(37),
        CreateFont());

    private static byte[] Read(string directory, string fileName) => File.ReadAllBytes(Path.Combine(directory, fileName));

    private static Arena2ClassicMediaProfile AuthoredProfile(params ClassicAuthoredUiAsset[] assets) =>
        new(AuthoredUiManifest: new ClassicAuthoredUiManifestInput("ui-authored-assets.json", [1, 2, 3]), AuthoredUiAssets: assets);

    private static byte[] SolidPng(int width, int height, byte[] rgba)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            rgba.CopyTo(pixels, index);
        }

        return DeterministicPngEncoder.EncodeRgba8(width, height, pixels);
    }

    private static byte[] NoisePng(int width, int height)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        uint state = 0x9E3779B9;
        for (int index = 0; index < pixels.Length; index++)
        {
            state = unchecked((state * 1664525) + 1013904223);
            pixels[index] = (byte)(state >> 24);
        }

        return DeterministicPngEncoder.EncodeRgba8(width, height, pixels);
    }

    private static byte[] CreatePalette()
    {
        byte[] palette = new byte[768];
        for (int index = 0; index < 256; index++)
        {
            palette[index * 3] = (byte)index;
            palette[(index * 3) + 1] = (byte)(255 - index);
            palette[(index * 3) + 2] = (byte)(index / 2);
        }

        return palette;
    }

    private static byte[] CreateHeaderedImage(byte pixel)
    {
        byte[] image = new byte[13];
        WriteInt16(image, 0, 0);
        WriteInt16(image, 2, 0);
        WriteUInt16(image, 4, 1);
        WriteUInt16(image, 6, 1);
        WriteUInt16(image, 8, 0);
        WriteUInt16(image, 10, 1);
        image[12] = pixel;
        return image;
    }

    private static byte[] CreateWeaponCif(int animationFrameCount = 5, bool includeWieldImage = true, int animationRecordCount = 6)
    {
        List<byte> bytes = [];
        if (includeWieldImage)
        {
            AppendInt16(bytes, 0);
            AppendInt16(bytes, 0);
            AppendInt16(bytes, 1);
            AppendInt16(bytes, 1);
            AppendUInt16(bytes, 0);
            AppendUInt16(bytes, 1);
            bytes.Add(1);
        }
        for (int action = 0; action < animationRecordCount; action++)
        {
            AppendUInt16(bytes, 1);
            AppendUInt16(bytes, 1);
            AppendUInt16(bytes, 2);
            AppendInt16(bytes, 0);
            AppendInt16(bytes, 0);
            AppendInt16(bytes, 0);
            for (int frame = 0; frame < 31; frame++)
            {
                AppendUInt16(bytes, frame < animationFrameCount ? checked((ushort)(76 + (frame * 2))) : (ushort)0);
            }

            AppendUInt16(bytes, checked((ushort)(76 + (animationFrameCount * 2))));
            for (int frame = 0; frame < animationFrameCount; frame++)
            {
                bytes.Add(0);
                bytes.Add((byte)(action + frame + 2));
            }
        }

        return bytes.ToArray();
    }

    private static byte[] CreateTextureArchive(int recordCount)
    {
        int recordsOffset = 26 + (recordCount * 20);
        const int recordHeaderBytes = 28;
        const int rowStride = 256;
        byte[] output = new byte[recordsOffset + (recordCount * (recordHeaderBytes + rowStride))];
        WriteInt16(output, 0, checked((short)recordCount));
        for (int index = 0; index < recordCount; index++)
        {
            int recordOffset = recordsOffset + (index * (recordHeaderBytes + rowStride));
            WriteInt32(output, 28 + (index * 20), recordOffset);
            WriteInt16(output, recordOffset + 4, 1);
            WriteInt16(output, recordOffset + 6, 1);
            WriteUInt16(output, recordOffset + 8, 0);
            WriteInt32(output, recordOffset + 14, 28);
            WriteUInt16(output, recordOffset + 20, 1);
            output[recordOffset + recordHeaderBytes] = checked((byte)(index + 1));
        }

        return output;
    }

    private static byte[] CreateNumericBsa(int recordCount)
    {
        List<byte> result = [];
        AppendInt16(result, checked((short)recordCount));
        AppendUInt16(result, 0x0200);
        for (int index = 0; index < recordCount; index++) result.Add((byte)index);
        for (int index = 0; index < recordCount; index++)
        {
            AppendUInt32(result, checked((uint)(10_000 + index)));
            AppendInt32(result, 1);
        }

        return result.ToArray();
    }

    private static byte[] CreateFont()
    {
        const int glyphCount = 240;
        const int headerBytes = 4;
        const int tableBytes = glyphCount * 4;
        const int glyphBytes = 32;
        byte[] font = new byte[headerBytes + tableBytes + (glyphCount * glyphBytes)];
        WriteUInt16(font, 0, 8);
        WriteUInt16(font, 2, 16);
        for (int index = 0; index < glyphCount; index++)
        {
            int offset = headerBytes + tableBytes + (index * glyphBytes);
            WriteUInt16(font, headerBytes + (index * 4), checked((ushort)offset));
            WriteUInt16(font, headerBytes + (index * 4) + 2, checked((ushort)(index % 16)));
            font[offset + 1] = 1;
        }

        return font;
    }

    private static int ReadBigEndian(byte[] bytes, int offset) => bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
    private static int ReadLittleEndian16(byte[] bytes, int offset) => bytes[offset] | (bytes[offset + 1] << 8);
    private static int ReadLittleEndian32(byte[] bytes, int offset) => bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);
    private static void WriteInt16(byte[] bytes, int offset, short value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void WriteUInt16(byte[] bytes, int offset, ushort value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void WriteInt32(byte[] bytes, int offset, int value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void AppendInt16(List<byte> bytes, short value) => bytes.AddRange(BitConverter.GetBytes(value));
    private static void AppendUInt16(List<byte> bytes, ushort value) => bytes.AddRange(BitConverter.GetBytes(value));
    private static void AppendUInt32(List<byte> bytes, uint value) => bytes.AddRange(BitConverter.GetBytes(value));
    private static void AppendInt32(List<byte> bytes, int value) => bytes.AddRange(BitConverter.GetBytes(value));
}
