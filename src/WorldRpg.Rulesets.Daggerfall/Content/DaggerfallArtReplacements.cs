using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Where a replacement image is drawn in the paper doll, in doll units (the classic 110x184 grid). It is the
/// generated image's own rect, independent of its pixel size, and may extend past the doll's frame.
/// </summary>
internal sealed record DaggerfallDollRect(double X, double Y, double Width, double Height)
{
    /// <summary>Metres per doll unit: the doll's 184 units hold a body about 1.8 m tall.</summary>
    internal const double Metres = .01;
}

/// <summary>One refreshed image that stands in for a classic media identity.</summary>
/// <param name="Replaces">The classic media identity it stands in for.</param>
/// <param name="MediaId">Its own media identity.</param>
/// <param name="Path">Its content-root path.</param>
/// <param name="ByteLength">Its length, as promotion recorded it.</param>
/// <param name="Doll">Where the paper doll draws it, for a paper-doll layer.</param>
/// <param name="DyeMask">For clothing, the image of the neutral cloth a dye recolours, and for armour or a weapon, of the
/// bare steel a metal recolours; the same size as this one.</param>
/// <param name="World">For an item's image, the PNG a dropped item stands as on the ground.</param>
/// <param name="Icon">For a held weapon, whose doll layer leaves a gap where the hand holds it, the whole weapon: what the
/// inventory, a shop and the ground draw. It shares the layer's tint, and is drawn in place of it by its own identity.</param>
internal sealed record DaggerfallArtReplacement(string Replaces, string MediaId, string Path, long ByteLength, DaggerfallDollRect? Doll,
    DaggerfallArtImage? DyeMask = null, DaggerfallArtWorldImage? World = null, DaggerfallArtReplacement? Icon = null)
{
    /// <summary>For an item icon, its height standing on the ground, in metres; a worn image stands at its doll height.</summary>
    internal double? Ground { get; init; }

    /// <summary>For the clothed body, the row it is split at for clothing worn above and below, in doll units from its top.</summary>
    internal double? Waist { get; init; }

    /// <summary>The height an item's world image stands on the ground at, in metres: an icon's own, a worn image's doll height.</summary>
    internal double GroundHeight => Ground ?? Doll!.Height * DaggerfallDollRect.Metres;
}

/// <summary>One published image of the refreshed art set beside its replacement: its identity, path and length.</summary>
/// <param name="World">For a worn item's dye mask, its PNG the same size as the item's <see cref="DaggerfallArtReplacement.World"/>.</param>
internal sealed record DaggerfallArtImage(string MediaId, string Path, long ByteLength, DaggerfallArtWorldImage? World = null);

/// <summary>A PNG of an item's image for the world, which reads PNG: its file and pixel size.</summary>
internal sealed record DaggerfallArtWorldImage(DaggerfallArtFile File, int Width, int Height);

/// <summary>A rect of an image in pixels, or, for <see cref="DaggerfallEffectArt.Slot"/>, in fractions of a frame.</summary>
internal sealed record DaggerfallArtRect(double X, double Y, double Width, double Height);

/// <summary>
/// One refreshed world effect: a PNG atlas of its frames, each the same box, and its own placement: a frame's world size
/// in metres, the pivot (fractions of a frame, from its left and bottom), the frames in playing order, their rate, and
/// whether it loops; for an inventory glint, also the part of a frame an item's slot holds (fractions, top-down).
/// </summary>
internal sealed record DaggerfallEffectArt(string Replaces, string Path, ContentSha256 Sha256, long ByteLength, int Width, int Height,
    IReadOnlyList<DaggerfallArtRect> Frames, Vector2 Size, Vector2 Pivot, IReadOnlyList<int> Sequence, float FramesPerSecond, bool Loops,
    DaggerfallArtRect? Slot);

/// <summary>
/// The walking test figure the developer command draws every walking person as: one compiled parts rig per view
/// (<see cref="DaggerfallPeopleRigView"/>, by view name), its standing height in rig units, the clips every view
/// holds, its walk and idle among them, its layer stack, and its view sets by name (<c>4</c>, <c>8</c>): which view
/// a camera sees from each direction around it.
/// </summary>
internal sealed record DaggerfallPeopleRig(string Name, float Height, string Walk, string Idle, float CrossfadeSeconds, IReadOnlySet<string> Clips,
    PartsRigLayerStack Layers, IReadOnlyDictionary<string, DaggerfallPeopleRigView> Views,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleRigDirection>> Directions)
{
    /// <summary>The figure seen from view set <paramref name="set"/>, its pages sampled by <paramref name="filter"/> and lit as <paramref name="material"/>.</summary>
    internal PartsRigFigure Figure(string set, TextureFilter filter, SpriteMaterialDescriptor material) => new($"{Name} {set}",
        [.. Directions[set].Select(direction => PartsRigFigureView.AtCamera(Views[direction.View].Model.Path, direction.Angle, direction.Mirror))],
        Height, Walk, Idle, Clips, Layers, CrossfadeSeconds, filter, material);
}

/// <summary>One view's compiled rig: its model document and the pages beside it.</summary>
internal sealed record DaggerfallPeopleRigView(DaggerfallArtFile Model, IReadOnlyList<DaggerfallArtFile> Pages);

/// <summary>A direction of a view set: the view a camera <paramref name="Angle"/> degrees around the figure sees (0 in front, 90 at its left), mirrored or not.</summary>
internal sealed record DaggerfallPeopleRigDirection(string Label, float Angle, string View, bool Mirror);

/// <summary>A published file of the refreshed set: its path and digest.</summary>
internal sealed record DaggerfallArtFile(string Path, ContentSha256 Sha256);

/// <summary>
/// A refreshed mesh standing in for the classic sprite cut from one texture record: a GLB resting on the floor, drawn at
/// the width of the sprite it replaces from its own width across (x), as the Engine admits it.
/// </summary>
/// <param name="Glow">The GLB material slots whose emissive maps shine at the lamps' level, as a refreshed flat's
/// emission map does: a lamp's glass and flame.</param>
internal sealed record DaggerfallArtMesh(DaggerfallArtFile File, IReadOnlyList<uint> Glow)
{
    /// <summary>
    /// How it hangs from its sprite's top and swings when an actor walks through it, for a mesh that does: its rig names the
    /// point it hangs from and the bodies that swing below it.
    /// </summary>
    internal DaggerfallHangingKind? Hangs { get; init; }
}

/// <summary>What a hanging mesh is, which sets how its bodies weigh and turn: a root, a chain, a cage or a lantern on a chain.</summary>
internal enum DaggerfallHangingKind { Root, Chain, Cage, Lantern }

/// <summary>
/// A remodel standing in for one classic ARCH3D model wherever an assembled location places it: an Engine static mesh in the
/// classic model's own frame, drawn at the classic's pose while refreshed art is drawn. It is render-only: promotion holds it
/// inside the classic's volume, whose mesh still serves the automap, collision and navigation.
/// </summary>
/// <param name="Slots">What each of its material slots draws, every slot once.</param>
internal sealed record DaggerfallArtModel(DaggerfallArtFile File, IReadOnlyList<DaggerfallArtModelSlot> Slots);

/// <summary>
/// One material slot of a remodel: a classic texture identity (<c>material/texture-A-R</c>), drawn as the location's own
/// surface for it with its refreshed art, climate, season and dungeon remaps, or else a PNG the art pack ships, opaque or,
/// with <paramref name="Mask"/>, cut out where its alpha is under half, as a cutout layer of goods before a shelf's back.
/// A classic texture may instead be drawn as one layer of a shell stack (<paramref name="Shell"/>). A slot with both an
/// identity and a PNG is painted for that identity: it draws its own PNG, or, where the location draws the identity as
/// another (a classic stone swapped by climate), the PNG <paramref name="Drawn"/> holds for that one, so a generated rock
/// wears the town's stone as the classic it stands in for does. A PNG of the pack's own may be a <paramref name="Flipbook"/>
/// the game steps through, and may be drawn <paramref name="Unlit"/>, at its own colours whatever the light, as a hearth's
/// flames are.
/// </summary>
internal sealed record DaggerfallArtModelSlot(uint Slot, string? Material, DaggerfallArtFile? Texture, bool Mask = false, DaggerfallArtShell? Shell = null,
    IReadOnlyDictionary<string, DaggerfallArtFile>? Drawn = null, DaggerfallArtFlipbook? Flipbook = null, bool Unlit = false)
{
    /// <summary>What a painted slot draws where the location draws its identity as <paramref name="drawn"/>.</summary>
    internal DaggerfallArtFile Painted(string drawn) => Drawn?.GetValueOrDefault(drawn) ?? Texture!;
}

/// <summary>
/// A texture that is a strip of <paramref name="Frames"/> equal frames side by side, left to right, shown one at a time at
/// <paramref name="FramesPerSecond"/> on faces whose uvs cover one frame's width of [0, 1].
/// </summary>
internal sealed record DaggerfallArtFlipbook(int Frames, float FramesPerSecond);

/// <summary>
/// One layer of a remodel's shell stack, the ground cover's effect on a model's faces: the mesh draws its classic texture's
/// faces again in <paramref name="Layers"/> layers, each a step deeper inside the classic than the one over it, and each layer
/// draws the relief of the texture the location draws there (<see cref="DaggerfallGroundCoverArt.Relief"/>), whose alpha is
/// each texel's height. Layer 0 is the stack's core and draws every texel; layer K the texels at least K/N tall, so a
/// texel's column rises from the core toward the classic's own faces.
/// </summary>
internal sealed record DaggerfallArtShell(int Layer, int Layers)
{
    /// <summary>
    /// The height a texel must reach to show in this layer, as a share of the stack: the ground's cutoffs, for the ground's
    /// own core is the ground under its shells, so a stack of N layers cuts where N - 1 shells over the ground do.
    /// </summary>
    internal float Cutoff => (float)Layer / Layers;

    /// <summary>How high the layer stands in its stack, from 0 at the core to 1 at the classic's faces.</summary>
    internal float Height => (float)Layer / (Layers - 1);
}

/// <summary>
/// A refreshed surface texture standing in for one classic texture record that walls, floors and ceilings draw: an RGBA
/// PNG the renderer repeats over the same faces the classic record covers.
/// </summary>
internal sealed record DaggerfallArtSurface(DaggerfallArtFile File, int Width, int Height);

/// <summary>
/// The refreshed ground cover: a tileable shell texture whose alpha is each texel's blade height, this many texels square and
/// drawn this many to the metre, the shader that sways the shells, and a grass tuft, both images greyscale; and each ground archive's grass, the
/// linear tints that colour the shells and the tufts there. An archive the set names no grass for grows no grass. The relief
/// of each classic texture record shells lift (every open ground layer, and each surface a remodel stacks in shells), by its
/// archive and record, is that record's own refreshed texture with its texels' heights: the set's one home for them.
/// </summary>
internal sealed record DaggerfallGroundCoverArt(DaggerfallArtFile Shell, int ShellTexels, float ShellTexelsPerMetre, DaggerfallArtFile Shader, DaggerfallArtFile Tuft,
    IReadOnlyDictionary<int, (Vector3 Shells, Vector3 Tufts)> Grass, IReadOnlyDictionary<(int Archive, int Record), DaggerfallGroundRelief> Relief);

/// <summary>
/// A refreshed nature mesh standing in for a flora sprite: a prop mesh (bark, leaf and stone parts, its vertex alpha the
/// share each vertex flutters) and its own height in metres; the sprite's drawn height and where its foot stands, as shares
/// of its frame height, which the mesh is drawn to; its bark and leaves, which a bare tree has no leaves of; its stone,
/// which a rock wears and the wind does not move; and the snow a winter tree's leaves or a winter rock's stone carry: the
/// share of their upward faces it covers, its linear colour and that texture's texels a side.
/// </summary>
internal sealed record DaggerfallTreeArt(DaggerfallArtFile Mesh, float MeshHeight, float Height, float Foot, DaggerfallArtFile? Bark, DaggerfallArtFile? Leaves,
    (float Share, Vector3 Colour, int Texels)? Snow = null, DaggerfallArtFile? Stone = null);

/// <summary>
/// A classic texture record's relief, the art set's one home for its texels' heights, which a ground layer's shells and a
/// remodel's shell stack draw: its refreshed texture, whose alpha is each texel's height as a share of the shell stack; the
/// tallest texel's share, so the stack draws only the layers it reaches; its size in texels, as the record's (a ground
/// tile's is square, a wall's need not be); how far its top sways in the wind, as a share of the shells' sway (a remodel's
/// shells stand still); the linear colour its lowest layer is shaded to, rising to its own at the top; what share of its
/// top texels glint as the viewer moves; and whether lying snow buries it, as it buries all but the snow's own relief.
/// </summary>
internal sealed record DaggerfallGroundRelief(DaggerfallArtFile File, float Tallest, int Width, int Height, float Sway, Vector3 Shade, float Glints, bool SinksUnderSnow);

/// <summary>
/// A refreshed world billboard standing in for one classic texture record: a PNG atlas of its frames in classic order, each
/// the same box, and where the classic frame sits in that box (fractions of the frame, top-down). A flat that gives light
/// names its emission map, an atlas of the same layout holding only what glows.
/// </summary>
internal sealed record DaggerfallArtFlat(DaggerfallArtFile File, int Width, int Height, IReadOnlyList<DaggerfallArtRect> Frames, DaggerfallArtRect Classic,
    DaggerfallArtFile? Emission = null)
{
    /// <summary>
    /// The world size and pivot that draw this art where the classic sprite stands: the box is larger than the classic frame
    /// it holds, so the classic size grows by the share that frame takes, and the classic pivot (from the frame's bottom) moves into the box.
    /// </summary>
    internal (Vector2 Size, Vector2 Pivot) Placed(Vector2 size, Vector2 pivot) =>
        (size / new Vector2((float)Classic.Width, (float)Classic.Height),
            new((float)(Classic.X + pivot.X * Classic.Width), (float)(1 - Classic.Y - Classic.Height + pivot.Y * Classic.Height)));
}

/// <summary>
/// How refreshed clothing, painted neutral, is drawn in one classic dye, or refreshed armour or a weapon, in bare
/// steel, in one classic metal: its dye mask multiplied by <paramref name="Multiply"/> (a CSS colour), the image first
/// brightened by <paramref name="Brightness"/> where the dye or metal is lighter than a multiply can reach.
/// </summary>
internal sealed record DaggerfallDyeTint(string Multiply, double Brightness);

/// <summary>
/// The refreshed art set: generated images promoted from <c>authoring/</c> (<c>promote.py</c>), each standing in for
/// one classic media identity. A presentation that draws a classic identity draws its replacement instead unless a
/// developer switched the run to classic art (<c>world.classic-art</c>), so every category substitutes through this
/// one map and classic art stays the fallback.
/// </summary>
internal sealed class DaggerfallArtReplacements
{
    /// <summary>
    /// The art pack's folder, a content-root path: its descriptor, its payload and the media the payload names, and
    /// beside them the committed art read without the pack, the authored UI kit (<see cref="DaggerfallUiArt.AuthoredRoot"/>)
    /// and the held definition (<c>DaggerfallHeldArt.DocumentPath</c>), so all of the game's own art is one unit.
    /// </summary>
    internal const string Folder = "worldrpg/packs/daggerfall.art-replacements";

    private readonly Dictionary<string, DaggerfallArtReplacement> _byClassic;
    private readonly Dictionary<string, DaggerfallArtReplacement> _byMediaId;

    private DaggerfallArtReplacements(Dictionary<string, DaggerfallArtReplacement> byClassic, IReadOnlyDictionary<string, DaggerfallDyeTint> tints,
        IReadOnlyDictionary<string, DaggerfallEffectArt> effects, DaggerfallArtFile? effectGlow, IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtMesh> meshes,
        IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtSurface> surfaces, IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtFlat> flats,
        IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtFlat> people)
    {
        Meshes = meshes;
        Surfaces = surfaces;
        Flats = flats;
        People = people;
        _byClassic = byClassic;
        _byMediaId = Every(byClassic.Values).ToDictionary(replacement => replacement.MediaId, StringComparer.Ordinal);
        Tints = tints;
        Effects = effects;
        EffectGlow = effectGlow;
    }

    /// <summary>The refreshed world effects, by the classic effect media identity each stands in for.</summary>
    internal IReadOnlyDictionary<string, DaggerfallEffectArt> Effects { get; }

    /// <summary>The soft white glow the refreshed spell trails and impact bursts tint, when the set has effects.</summary>
    internal DaggerfallArtFile? EffectGlow { get; }

    /// <summary>
    /// The meshes drawn in place of classic world sprites, by the texture archive and record each replaces: the bone pile
    /// for the corpse every human enemy class shares (so it is no one in particular) and for the bone piles placed in dungeons,
    /// the placed rocks, logs and barrel, and the roots, chains, cage and lanterns that hang and swing.
    /// </summary>
    internal IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtMesh> Meshes { get; }

    /// <summary>The remodels drawn in place of classic ARCH3D models, by the ARCH3D record id each replaces.</summary>
    internal IReadOnlyDictionary<string, DaggerfallArtModel> Models { get; private init; } = new Dictionary<string, DaggerfallArtModel>();

    /// <summary>The refreshed grass that covers open ground near the player, when the set has it.</summary>
    internal DaggerfallGroundCoverArt? GroundCover { get; private init; }

    /// <summary>The shader that lays a winter tree's snow on its leaves, where the set has snowy trees.</summary>
    internal DaggerfallArtFile? TreeSnow { get; private init; }

    /// <summary>The tree meshes standing in for flora sprites, by nature archive and record.</summary>
    internal IReadOnlyDictionary<(int Archive, int Record), DaggerfallTreeArt> Trees { get; private init; } = new Dictionary<(int, int), DaggerfallTreeArt>();

    /// <summary>The refreshed surface textures, by the classic texture archive and record each replaces.</summary>
    internal IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtSurface> Surfaces { get; }

    /// <summary>The refreshed world billboards, by the classic texture archive and record each replaces.</summary>
    internal IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtFlat> Flats { get; }

    /// <summary>The refreshed standing people, by the classic billboard record each stands in for.</summary>
    internal IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtFlat> People { get; }

    /// <summary>The walking test figure, when the set has one.</summary>
    internal DaggerfallPeopleRig? PeopleRig { get; private init; }

    /// <summary>
    /// The refreshed surface for a classic texture record the importer published at <paramref name="texturePath"/>
    /// (<c>…/texture-ARCHIVE-RECORD.png</c>), if the set has one.
    /// </summary>
    internal DaggerfallArtSurface? Surface(string texturePath) =>
        Record(texturePath) is { } record && Surfaces.TryGetValue(record, out DaggerfallArtSurface? surface) ? surface : null;

    /// <summary>
    /// The relief of the classic texture record the importer published at <paramref name="texturePath"/>, which a remodel's
    /// shell stack draws, if the set has one.
    /// </summary>
    internal DaggerfallGroundRelief? Relief(string texturePath) =>
        Record(texturePath) is { } record && GroundCover?.Relief.TryGetValue(record, out DaggerfallGroundRelief? relief) == true ? relief : null;

    private static (int Archive, int Record)? Record(string texturePath) =>
        System.Text.RegularExpressions.Regex.Match(texturePath, @"/texture-(\d+)-(\d+)\.png$") is { Success: true } match
            ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) : null;

    /// <summary>Every replacement.</summary>
    internal IEnumerable<DaggerfallArtReplacement> All => _byClassic.Values;

    /// <summary>Every image the set publishes: each replacement, its icon, and their dye masks.</summary>
    internal IEnumerable<DaggerfallArtImage> Images => Every(_byClassic.Values).SelectMany(replacement =>
        replacement.DyeMask is { } mask ? [new(replacement.MediaId, replacement.Path, replacement.ByteLength), mask]
            : new[] { new DaggerfallArtImage(replacement.MediaId, replacement.Path, replacement.ByteLength) });

    /// <summary>Each classic clothing dye's and metal's tint, by dye or metal name.</summary>
    internal IReadOnlyDictionary<string, DaggerfallDyeTint> Tints { get; }

    private static IEnumerable<DaggerfallArtReplacement> Every(IEnumerable<DaggerfallArtReplacement> replacements) =>
        replacements.SelectMany(replacement => replacement.Icon is { } icon ? [replacement, icon] : new[] { replacement });

    /// <summary>The replacement for a classic media identity, if the set has one.</summary>
    internal DaggerfallArtReplacement? For(string classicMediaId) =>
        _byClassic.TryGetValue(classicMediaId, out DaggerfallArtReplacement? replacement) ? replacement : null;

    /// <summary>The replacement whose own media identity this is, if the set has one.</summary>
    internal DaggerfallArtReplacement? ByMediaId(string mediaId) =>
        _byMediaId.TryGetValue(mediaId, out DaggerfallArtReplacement? replacement) ? replacement : null;

    /// <summary>The media identity the page knows a refreshed effect atlas by, as the UI draws the inventory glints.</summary>
    internal static string EffectMediaId(string classicMediaId) => $"refreshed.{classicMediaId}";

    /// <summary>The media identity drawn in place of a classic one: its replacement's, or the classic one when the set has none.</summary>
    internal string Drawn(string classicMediaId) => For(classicMediaId)?.MediaId ?? classicMediaId;

    /// <summary>Reads the <c>artReplacements</c> section of the pack payload.</summary>
    /// <exception cref="InvalidOperationException">The section is unreadable, or names one identity twice.</exception>
    internal static DaggerfallArtReplacements Read(ReadOnlyMemory<byte> payload, string packId)
    {
        Dictionary<string, DaggerfallArtReplacement> byClassic = new(StringComparer.Ordinal);
        HashSet<string> ids = new(StringComparer.Ordinal);
        Dictionary<string, DaggerfallDyeTint> tints = new(StringComparer.Ordinal);
        Dictionary<string, DaggerfallEffectArt> effects = new(StringComparer.Ordinal);
        DaggerfallArtFile? glow = null;
        DaggerfallGroundCoverArt? groundCover = null;
        Dictionary<(int, int), DaggerfallTreeArt> trees = [];
        DaggerfallArtFile? treeSnow = null;
        DaggerfallPeopleRig? peopleRig = null;
        Dictionary<(int Archive, int Record), DaggerfallArtMesh> meshes = [];
        Dictionary<string, DaggerfallArtModel> models = new(StringComparer.Ordinal);
        Dictionary<(int Archive, int Record), DaggerfallArtSurface> surfaces = [];
        Dictionary<(int Archive, int Record), DaggerfallArtFlat> flats = [], people = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            DaggerfallArtReplacement Image(JsonElement image, string replaces)
            {
                string mediaId = image.GetProperty("mediaId").GetString()!;
                string path = image.GetProperty("path").GetString()!;
                long byteLength = image.GetProperty("byteLength").GetInt64();
                if (string.IsNullOrWhiteSpace(replaces) || string.IsNullOrWhiteSpace(mediaId) || string.IsNullOrWhiteSpace(path) || byteLength <= 0)
                    throw new InvalidOperationException($"Content pack '{packId}' states a replacement without a usable identity, path or length.");
                DaggerfallDollRect? doll = image.TryGetProperty("doll", out JsonElement rect) && rect.ValueKind == JsonValueKind.Object
                    ? new(rect.GetProperty("x").GetDouble(), rect.GetProperty("y").GetDouble(), rect.GetProperty("width").GetDouble(), rect.GetProperty("height").GetDouble())
                    : null;
                if (doll is { Width: <= 0 } or { Height: <= 0 })
                    throw new InvalidOperationException($"Content pack '{packId}' places replacement '{mediaId}' in an empty doll rect.");
                DaggerfallArtImage? dyeMask = image.TryGetProperty("dyeMask", out JsonElement mask)
                    ? new(mask.GetProperty("mediaId").GetString()!, mask.GetProperty("path").GetString()!, mask.GetProperty("byteLength").GetInt64(), World(mask, mediaId))
                    : null;
                DaggerfallArtWorldImage? world = World(image, mediaId);
                JsonElement? placement = image.TryGetProperty("placement", out JsonElement placed) ? placed : null;
                // An icon stands on the ground at its own height, a worn image at its doll height.
                double? ground = world is not null && doll is null ? Positive(Placement(placement, $"the world image of '{mediaId}'", "ground"), "ground", $"the world image of '{mediaId}'") : null;
                double? waist = replaces.StartsWith("character.body-clothed.", StringComparison.Ordinal)
                    ? Placement(placement, $"the clothed body '{mediaId}'", "waist").GetProperty("waist").GetDouble() : null;
                if (waist is { } row && (!double.IsFinite(row) || row < 0 || row > doll?.Height))
                    throw new InvalidOperationException($"Content pack '{packId}' splits the clothed body '{mediaId}' at row {row}, outside its doll rect.");
                if (dyeMask?.World is { } maskWorld && (world is null || maskWorld.Width != world.Width || maskWorld.Height != world.Height))
                    throw new InvalidOperationException($"Content pack '{packId}' states a world dye mask for '{mediaId}' that is not the size of its world image.");
                if (!ids.Add(mediaId) || dyeMask is not null && !ids.Add(dyeMask.MediaId))
                    throw new InvalidOperationException($"Content pack '{packId}' names replacement '{mediaId}' for '{replaces}' twice.");
                DaggerfallArtReplacement? icon = image.TryGetProperty("icon", out JsonElement whole) ? Image(whole, replaces) : null;
                // The icon is drawn in the layer's tint, so it needs a dye mask exactly when the layer has one.
                if (icon is not null && (icon.DyeMask is null) != (dyeMask is null))
                    throw new InvalidOperationException($"Content pack '{packId}' states an icon for '{mediaId}' that is not dyed as its layer is.");
                return new(replaces, mediaId, path, byteLength, doll, dyeMask, world, icon) { Ground = ground, Waist = waist };
            }

            foreach (JsonElement image in document.RootElement.GetProperty("artReplacements").GetProperty("images").EnumerateArray())
            {
                string replaces = image.GetProperty("replaces").GetString()!;
                DaggerfallArtReplacement replacement = Image(image, replaces);
                if (!byClassic.TryAdd(replaces, replacement))
                    throw new InvalidOperationException($"Content pack '{packId}' names replacement '{replacement.MediaId}' for '{replaces}' twice.");
            }

            if (document.RootElement.GetProperty("artReplacements").TryGetProperty("dyeTints", out JsonElement dyes))
                foreach (JsonProperty dye in dyes.EnumerateObject())
                {
                    string multiply = dye.Value.GetProperty("multiply").GetString()!;
                    double brightness = dye.Value.GetProperty("brightness").GetDouble();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(multiply, "^#[0-9a-f]{6}$") || !double.IsFinite(brightness) || brightness < 1)
                        throw new InvalidOperationException($"Content pack '{packId}' states an unusable tint for dye '{dye.Name}'.");
                    tints[dye.Name] = new(multiply, brightness);
                }

            JsonElement section = document.RootElement.GetProperty("artReplacements");
            if (section.TryGetProperty("effects", out JsonElement effectArt))
                foreach (JsonElement effect in effectArt.EnumerateArray())
                {
                    string replaces = effect.GetProperty("replaces").GetString()!;
                    int width = effect.GetProperty("width").GetInt32(), height = effect.GetProperty("height").GetInt32();
                    DaggerfallArtRect[] frames = [.. effect.GetProperty("frames").EnumerateArray().Select(Rect)];
                    if (frames.Length == 0 || frames.Any(frame => frame.X < 0 || frame.Y < 0 || frame.Width <= 0 || frame.Height <= 0 || frame.X + frame.Width > width || frame.Y + frame.Height > height))
                        throw new InvalidOperationException($"Content pack '{packId}' places refreshed effect '{replaces}' outside its atlas.");
                    string subject = $"refreshed effect '{replaces}'";
                    JsonElement placement = Placement(effect.TryGetProperty("placement", out JsonElement placed) ? placed : null, subject,
                        ["size", "pivot", "sequence", "framesPerSecond", "loops", .. replaces.StartsWith("effect.glint.", StringComparison.Ordinal) ? ["slot"] : Array.Empty<string>()]);
                    JsonElement size = placement.GetProperty("size"), pivot = placement.GetProperty("pivot");
                    Vector2 metres = new(size.GetProperty("width").GetSingle(), size.GetProperty("height").GetSingle());
                    int[] sequence = [.. placement.GetProperty("sequence").EnumerateArray().Select(index => index.GetInt32())];
                    float rate = placement.GetProperty("framesPerSecond").GetSingle();
                    DaggerfallArtRect? slot = placement.TryGetProperty("slot", out JsonElement held) ? Rect(held) : null;
                    if (!float.IsFinite(metres.X) || !float.IsFinite(metres.Y) || metres.X <= 0 || metres.Y <= 0 || !float.IsFinite(rate) || rate <= 0
                        || slot is { Width: <= 0 } or { Height: <= 0 })
                        throw new InvalidOperationException($"Content pack '{packId}' states an empty size, rate or slot for {subject}.");
                    if (sequence.Length == 0 || sequence.Any(index => index < 0 || index >= frames.Length))
                        throw new InvalidOperationException($"Content pack '{packId}' plays {subject} through a frame of its {frames.Length} it lacks.");
                    if (!effects.TryAdd(replaces, new(replaces, effect.GetProperty("path").GetString()!,
                            DaggerfallContentHash.Parse(effect.GetProperty("sha256").GetString()!, $"Refreshed effect '{replaces}'"),
                            effect.GetProperty("byteLength").GetInt64(), width, height, frames, metres,
                            new(pivot.GetProperty("x").GetSingle(), pivot.GetProperty("y").GetSingle()), sequence, rate,
                            placement.GetProperty("loops").GetBoolean(), slot)))
                        throw new InvalidOperationException($"Content pack '{packId}' names refreshed effect '{replaces}' twice.");
                }
            if (section.TryGetProperty("effectGlow", out JsonElement glowValue))
                glow = new(glowValue.GetProperty("path").GetString()!, DaggerfallContentHash.Parse(glowValue.GetProperty("sha256").GetString()!, "The refreshed effect glow"));
            if (section.TryGetProperty("meshes", out JsonElement meshArt))
                foreach (JsonElement mesh in meshArt.EnumerateArray())
                {
                    (int, int) replaces = (mesh.GetProperty("replaces").GetProperty("archive").GetInt32(), mesh.GetProperty("replaces").GetProperty("record").GetInt32());
                    uint[] glowing = mesh.TryGetProperty("glow", out JsonElement slots) ? [.. slots.EnumerateArray().Select(slot => slot.GetUInt32())] : [];
                    DaggerfallHangingKind? hangs = mesh.TryGetProperty("hangs", out JsonElement kind) ? kind.GetString() switch
                    {
                        "root" => DaggerfallHangingKind.Root,
                        "chain" => DaggerfallHangingKind.Chain,
                        "cage" => DaggerfallHangingKind.Cage,
                        "lantern" => DaggerfallHangingKind.Lantern,
                        var other => throw new InvalidOperationException(
                            $"Content pack '{packId}' hangs the mesh replacing {replaces.Item1}/{replaces.Item2} as '{other}', which is no root, chain, cage or lantern."),
                    } : null;
                    if (!meshes.TryAdd(replaces, new(new(mesh.GetProperty("path").GetString()!,
                            DaggerfallContentHash.Parse(mesh.GetProperty("sha256").GetString()!, $"The refreshed mesh for {replaces.Item1}/{replaces.Item2}")), glowing) { Hangs = hangs }))
                        throw new InvalidOperationException($"Content pack '{packId}' names two meshes for {replaces.Item1}/{replaces.Item2}.");
                }
            if (section.TryGetProperty("models", out JsonElement modelArt))
                foreach (JsonProperty model in modelArt.EnumerateObject())
                {
                    string subject = $"the remodel of ARCH3D model {model.Name}";
                    // A key that is no ARCH3D record id would never meet a placed model, so the remodel would never draw.
                    if (!uint.TryParse(model.Name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
                        throw new InvalidOperationException($"Content pack '{packId}' states {subject} without an ARCH3D record id.");
                    DaggerfallArtModelSlot[] slots = [.. model.Value.GetProperty("slots").EnumerateArray().Select(slot =>
                    {
                        string? material = slot.TryGetProperty("material", out JsonElement named) ? named.GetString() : null;
                        DaggerfallArtFile? texture = slot.TryGetProperty("texture", out JsonElement shipped) ? File(shipped, $"A texture of {subject}") : null;
                        // A slot is classic (an identity), the pack's own (a texture), or painted for an identity (both).
                        if (material is null && texture is null || material is not null && !material.StartsWith("material/texture-", StringComparison.Ordinal))
                            throw new InvalidOperationException($"Content pack '{packId}' states a slot of {subject} that names neither one classic texture identity nor one texture of its own.");
                        string? alpha = slot.TryGetProperty("alpha", out JsonElement cut) ? cut.GetString() : null;
                        if (alpha is not (null or "mask") || alpha is not null && texture is null)
                            throw new InvalidOperationException($"Content pack '{packId}' cuts out a slot of {subject} by '{alpha}': only a texture of its own is cut out, by \"mask\".");
                        DaggerfallArtShell? shell = slot.TryGetProperty("shell", out JsonElement stack)
                            ? new(stack.GetProperty("layer").GetInt32(), stack.GetProperty("layers").GetInt32()) : null;
                        if (shell is not null && (material is null || texture is not null || shell.Layers is < 2 or > 16 || shell.Layer < 0 || shell.Layer >= shell.Layers))
                            throw new InvalidOperationException($"Content pack '{packId}' states a slot of {subject} as no layer of a shell stack of 2 to 16 drawing a classic texture.");
                        Dictionary<string, DaggerfallArtFile>? drawn = null;
                        if (slot.TryGetProperty("drawn", out JsonElement others))
                        {
                            // A painted slot's other textures stand for identities the location draws its own as.
                            if (material is null || texture is null)
                                throw new InvalidOperationException($"Content pack '{packId}' states textures by drawn identity for a slot of {subject} that is not painted for a classic identity.");
                            drawn = new(StringComparer.Ordinal);
                            foreach (JsonProperty other in others.EnumerateObject())
                                drawn.Add(other.Name.StartsWith("material/texture-", StringComparison.Ordinal) ? other.Name
                                    : throw new InvalidOperationException($"Content pack '{packId}' paints a slot of {subject} for '{other.Name}', which is no classic texture identity."),
                                    File(other.Value, $"A texture of {subject}"));
                        }
                        bool own = texture is not null && material is null;
                        DaggerfallArtFlipbook? flipbook = slot.TryGetProperty("frames", out JsonElement frames) | slot.TryGetProperty("framesPerSecond", out JsonElement rate)
                            ? new(frames.ValueKind == JsonValueKind.Number ? frames.GetInt32() : 0, rate.ValueKind == JsonValueKind.Number ? rate.GetSingle() : 0F) : null;
                        // A strip the game cannot step through would draw all its frames squeezed onto one face, or none.
                        if (flipbook is not null && (!own || flipbook.Frames < 2 || !float.IsFinite(flipbook.FramesPerSecond) || flipbook.FramesPerSecond <= 0F))
                            throw new InvalidOperationException($"Content pack '{packId}' states a slot of {subject} as {flipbook.Frames} frames at {flipbook.FramesPerSecond} a second: only a texture of its own is a strip, of two frames or more at a rate above zero.");
                        bool unlit = slot.TryGetProperty("unlit", out JsonElement light) && light.GetBoolean();
                        if (unlit && !own)
                            throw new InvalidOperationException($"Content pack '{packId}' draws a slot of {subject} unlit: only a texture of its own is, as a classic identity draws as the location's lit surface.");
                        return new DaggerfallArtModelSlot(slot.GetProperty("slot").GetUInt32(), material, texture, alpha == "mask", shell, drawn, flipbook, unlit);
                    })];
                    if (slots.Length == 0 || slots.DistinctBy(slot => slot.Slot).Count() != slots.Length)
                        throw new InvalidOperationException($"Content pack '{packId}' states {subject} without its slots, each once.");
                    models.Add(model.Name, new(File(model.Value, $"The mesh of {subject}"), slots));
                }
            if (section.TryGetProperty("surfaces", out JsonElement surfaceArt))
                foreach (JsonElement surface in surfaceArt.EnumerateArray())
                {
                    (int, int) replaces = (surface.GetProperty("replaces").GetProperty("archive").GetInt32(), surface.GetProperty("replaces").GetProperty("record").GetInt32());
                    int width = surface.GetProperty("width").GetInt32(), height = surface.GetProperty("height").GetInt32();
                    if (width <= 0 || height <= 0)
                        throw new InvalidOperationException($"Content pack '{packId}' states an empty surface for {replaces.Item1}/{replaces.Item2}.");
                    if (!surfaces.TryAdd(replaces, new(new(surface.GetProperty("path").GetString()!,
                            DaggerfallContentHash.Parse(surface.GetProperty("sha256").GetString()!, $"The refreshed surface for {replaces.Item1}/{replaces.Item2}")), width, height)))
                        throw new InvalidOperationException($"Content pack '{packId}' names two surfaces for {replaces.Item1}/{replaces.Item2}.");
                }
            foreach ((string name, Dictionary<(int Archive, int Record), DaggerfallArtFlat> sprites) in new[] { ("flats", flats), ("people", people) })
                if (section.TryGetProperty(name, out JsonElement spriteArt))
                foreach (JsonElement flat in spriteArt.EnumerateArray())
                {
                    (int, int) replaces = (flat.GetProperty("replaces").GetProperty("archive").GetInt32(), flat.GetProperty("replaces").GetProperty("record").GetInt32());
                    int width = flat.GetProperty("width").GetInt32(), height = flat.GetProperty("height").GetInt32();
                    DaggerfallArtRect[] frames = [.. flat.GetProperty("frames").EnumerateArray().Select(Rect)];
                    DaggerfallArtRect classic = Rect(flat.GetProperty("classic"));
                    if (frames.Length == 0 || frames.Any(frame => frame.X < 0 || frame.Y < 0 || frame.Width <= 0 || frame.Height <= 0 || frame.X + frame.Width > width || frame.Y + frame.Height > height)
                        || classic.Width <= 0 || classic.Height <= 0)
                        throw new InvalidOperationException($"Content pack '{packId}' places the refreshed flat for {replaces.Item1}/{replaces.Item2} outside its atlas.");
                    DaggerfallArtFile? emission = flat.TryGetProperty("emission", out JsonElement map)
                        ? new(map.GetProperty("path").GetString()!, DaggerfallContentHash.Parse(map.GetProperty("sha256").GetString()!, $"The emission map for {replaces.Item1}/{replaces.Item2}"))
                        : null;
                    if (!sprites.TryAdd(replaces, new(new(flat.GetProperty("path").GetString()!,
                            DaggerfallContentHash.Parse(flat.GetProperty("sha256").GetString()!, $"The refreshed flat for {replaces.Item1}/{replaces.Item2}")), width, height, frames, classic, emission)))
                        throw new InvalidOperationException($"Content pack '{packId}' names two flats for {replaces.Item1}/{replaces.Item2}.");
                }
            if (section.TryGetProperty("peopleRig", out JsonElement rig)) peopleRig = PeopleRig(rig);
            if (section.TryGetProperty("treeSnow", out JsonElement snowShader)) treeSnow = File(snowShader, "The trees' snow shader");
            if (section.TryGetProperty("trees", out JsonElement treeArt))
                foreach (JsonProperty tree in treeArt.EnumerateObject())
                {
                    string[] key = tree.Name.Split('-');
                    JsonElement t = tree.Value, mesh = t.GetProperty("mesh");
                    if (key.Length != 2)
                        throw new InvalidOperationException($"Content pack '{packId}' states tree '{tree.Name}' without an archive and record.");
                    DaggerfallArtFile? bark = t.TryGetProperty("bark", out JsonElement treeBark) ? File(treeBark, $"Tree {tree.Name}'s bark") : null;
                    DaggerfallArtFile? stone = t.TryGetProperty("stone", out JsonElement treeStone) ? File(treeStone, $"Tree {tree.Name}'s stone") : null;
                    trees.Add((int.Parse(key[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(key[1], System.Globalization.CultureInfo.InvariantCulture)),
                        new(File(mesh, $"Tree {tree.Name}'s mesh"), mesh.GetProperty("height").GetSingle(), t.GetProperty("height").GetSingle(),
                            t.GetProperty("foot").GetSingle(), bark,
                            t.TryGetProperty("leaves", out JsonElement treeLeaves) ? File(treeLeaves, $"Tree {tree.Name}'s leaves") : null,
                            t.TryGetProperty("snow", out JsonElement snow) ? (snow.GetProperty("share").GetSingle(), Tint(snow.GetProperty("colour")), snow.GetProperty("texels").GetInt32()) : null,
                            stone));
                }
            if (section.TryGetProperty("groundCover", out JsonElement cover))
            {
                JsonElement shell = cover.GetProperty("shell"), tuft = cover.GetProperty("tuft");
                float texels = shell.GetProperty("texelsPerMetre").GetSingle();
                int side = shell.GetProperty("width").GetInt32();
                if (side <= 0 || side != shell.GetProperty("height").GetInt32())
                    throw new InvalidOperationException($"Content pack '{packId}' states a ground cover shell texture that is not square.");
                if (!float.IsFinite(texels) || texels <= 0)
                    throw new InvalidOperationException($"Content pack '{packId}' draws its ground cover's shells at {texels} texels to the metre.");
                Dictionary<int, (Vector3, Vector3)> grass = [];
                foreach (JsonProperty archive in cover.GetProperty("grass").EnumerateObject())
                    grass.Add(int.Parse(archive.Name, System.Globalization.CultureInfo.InvariantCulture),
                        (Tint(archive.Value.GetProperty("shells")), Tint(archive.Value.GetProperty("tufts"))));
                Dictionary<(int, int), DaggerfallGroundRelief> relief = [];
                foreach (JsonProperty layer in cover.GetProperty("relief").EnumerateObject())
                {
                    string[] key = layer.Name.Split('-');
                    float tallest = layer.Value.GetProperty("tallest").GetSingle();
                    int width = layer.Value.GetProperty("width").GetInt32(), height = layer.Value.GetProperty("height").GetInt32();
                    if (key.Length != 2 || !float.IsFinite(tallest) || tallest is <= 0 or > 1 || width <= 0)
                        throw new InvalidOperationException($"Content pack '{packId}' states relief '{layer.Name}' without an archive, record, width and tallest texel.");
                    relief.Add((int.Parse(key[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(key[1], System.Globalization.CultureInfo.InvariantCulture)),
                        new(File(layer.Value, $"The relief {layer.Name}"), tallest, width, height, layer.Value.GetProperty("sway").GetSingle(),
                            Tint(layer.Value.GetProperty("shade")), layer.Value.GetProperty("glints").GetSingle(),
                            layer.Value.GetProperty("sinksUnderSnow").GetBoolean()));
                }
                groundCover = new(File(shell, "The ground cover's shell texture"), side, texels, File(cover.GetProperty("shader"), "The ground cover's shader"),
                    File(tuft, "The ground cover's tuft"), grass, relief);
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException($"Content pack '{packId}' carries an unreadable artReplacements section: {exception.Message}", exception);
        }

        return new DaggerfallArtReplacements(byClassic, tints, effects, glow, meshes, surfaces, flats, people)
        { GroundCover = groundCover, Trees = trees, TreeSnow = treeSnow, PeopleRig = peopleRig, Models = models };

        DaggerfallPeopleRig PeopleRig(JsonElement rig)
        {
            string name = rig.GetProperty("figure").GetString()!;
            float height = rig.GetProperty("height").GetSingle(), crossfade = rig.GetProperty("crossfade").GetSingle();
            string walk = rig.GetProperty("walk").GetString()!, idle = rig.GetProperty("idle").GetString()!;
            HashSet<string> clips = [.. rig.GetProperty("clips").EnumerateArray().Select(clip => clip.GetString()!)];
            Dictionary<string, DaggerfallPeopleRigView> views = rig.GetProperty("views").EnumerateObject().ToDictionary(view => view.Name,
                view => new DaggerfallPeopleRigView(File(view.Value.GetProperty("model"), $"People rig {name}'s {view.Name} model"),
                    [.. view.Value.GetProperty("pages").EnumerateArray().Select(page => File(page, $"People rig {name}'s {view.Name} pages"))]), StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<DaggerfallPeopleRigDirection>> directions = rig.GetProperty("directions").EnumerateObject().ToDictionary(set => set.Name,
                set => (IReadOnlyList<DaggerfallPeopleRigDirection>)[.. set.Value.EnumerateArray().Select(direction => new DaggerfallPeopleRigDirection(
                    direction.GetProperty("label").GetString()!, direction.GetProperty("angle").GetSingle(), direction.GetProperty("view").GetString()!,
                    direction.TryGetProperty("mirror", out JsonElement mirror) && mirror.GetBoolean()))], StringComparer.Ordinal);
            if (!float.IsFinite(height) || height <= 0 || !float.IsFinite(crossfade) || crossfade < 0 || !clips.Contains(walk) || !clips.Contains(idle))
                throw new InvalidOperationException($"Content pack '{packId}' states people rig '{name}' without a height, a crossfade, or its walk and idle among its clips.");
            if (directions.Count == 0 || directions.Values.Any(set => set.Count == 0 || set.Any(direction => !views.ContainsKey(direction.View) || !float.IsFinite(direction.Angle))))
                throw new InvalidOperationException($"Content pack '{packId}' states a view set of people rig '{name}' that is empty or names a view it lacks.");
            return new(name, height, walk, idle, crossfade, clips, PartsRigLayerStack.Read(rig.GetProperty("layers")), views, directions);
        }

        DaggerfallArtWorldImage? World(JsonElement owner, string mediaId)
        {
            if (!owner.TryGetProperty("world", out JsonElement world)) return null;
            int width = world.GetProperty("width").GetInt32(), height = world.GetProperty("height").GetInt32();
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Content pack '{packId}' states an empty world image for '{mediaId}'.");
            return new(new(world.GetProperty("path").GetString()!, DaggerfallContentHash.Parse(world.GetProperty("sha256").GetString()!, $"The world image of '{mediaId}'")),
                width, height);
        }

        // An entry's own placement, refused by name when it lacks a value its kind needs: nothing falls back to the classic.
        JsonElement Placement(JsonElement? placement, string subject, params string[] names)
        {
            if (placement is not { ValueKind: JsonValueKind.Object } values)
                throw new InvalidOperationException($"Content pack '{packId}' states no placement for {subject}.");
            foreach (string name in names)
                if (!values.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                    throw new InvalidOperationException($"Content pack '{packId}' states no {name} in the placement of {subject}.");
            return values;
        }

        double Positive(JsonElement placement, string name, string subject)
        {
            double value = placement.GetProperty(name).GetDouble();
            if (!double.IsFinite(value) || value <= 0)
                throw new InvalidOperationException($"Content pack '{packId}' states {name} {value} for {subject}.");
            return value;
        }

        static DaggerfallArtFile File(JsonElement file, string publisher) =>
            new(file.GetProperty("path").GetString()!, DaggerfallContentHash.Parse(file.GetProperty("sha256").GetString()!, publisher));

        Vector3 Tint(JsonElement tint)
        {
            Vector3 value = new(tint[0].GetSingle(), tint[1].GetSingle(), tint[2].GetSingle());
            if (!float.IsFinite(value.X + value.Y + value.Z) || value.X < 0 || value.Y < 0 || value.Z < 0)
                throw new InvalidOperationException($"Content pack '{packId}' tints its ground cover by {value}.");
            return value;
        }

        static DaggerfallArtRect Rect(JsonElement rect) => new(rect.GetProperty("x").GetDouble(), rect.GetProperty("y").GetDouble(),
            rect.GetProperty("width").GetDouble(), rect.GetProperty("height").GetDouble());
    }
}
