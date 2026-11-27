using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Which sheet of parchment a posting is pinned up as (#11138): a wanted poster, a decree, or a plain notice.</summary>
internal enum DaggerfallNoticeSheet { Bounty = 0, Decree = 1, Notice = 2 }

/// <summary>
/// Built 3D geometry for the Town/Tavern Notice Board (#11082, #11138): oak posts and frame, a gabled rain canopy, a
/// timber backboard, and one sheet of parchment for each live posting, in the slots
/// <see cref="DaggerfallSiteAppearance.NoticeSheetPlaces"/> gives, front first.
/// </summary>
internal readonly record struct DaggerfallNoticeBoardMeshData(
    Vector3[] Positions,
    Vector3[] Normals,
    Vector2[] Uvs,
    uint[] Indices,
    MeshGroup[] Groups,
    Vector3 BoundsMin,
    Vector3 BoundsMax)
{
    internal const uint TimberFrameSlot = 0;
    internal const uint RoofCanopySlot = 1;
    internal const uint BackboardSlot = 2;

    /// <summary>The slot of a sheet of <paramref name="sheet"/>: one per kind, after the wood.</summary>
    internal static uint SheetSlot(DaggerfallNoticeSheet sheet) => 3 + (uint)sheet;

    internal const float BackboardMinY = 0.95f;
    internal const float BackboardMaxY = 1.74f;
}

internal sealed partial class DaggerfallSiteAppearance
{
    /// <summary>The most sheets a board shows: four on its face and two on its back.</summary>
    internal const int MaxNoticeSheets = 6;

    /// <summary>Where each sheet is pinned, in the order they fill: centre, size and tilt, and whether on the front face.</summary>
    internal static readonly (float X, float Y, float Width, float Height, float Tilt, bool Front)[] NoticeSheetPlaces =
    [
        (-0.24f, 1.48f, 0.27f, 0.36f, 2.4f, true),
        (0.23f, 1.47f, 0.26f, 0.34f, -2.8f, true),
        (-0.22f, 1.14f, 0.24f, 0.32f, -1.9f, true),
        (0.21f, 1.13f, 0.25f, 0.33f, 2.1f, true),
        (-0.18f, 1.36f, 0.27f, 0.36f, -2.0f, false),
        (0.20f, 1.30f, 0.25f, 0.33f, 2.5f, false),
    ];

    private readonly List<(DaggerfallSiteNoticeBoard Definition, ulong Ordinal)> noticeBoards = [];
    private readonly List<IDisposable> noticeBoardResources = [];
    private readonly Dictionary<string, Appearance> noticeBoardMeshes = new(StringComparer.Ordinal);
    private MeshMaterialBinding[]? noticeBoardBindings;
    private DaggerfallNoticeBoardArt? noticeBoardArt;
    private IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtSurface> noticeBoardSurfaces = new Dictionary<(int, int), DaggerfallArtSurface>();
    private Func<DaggerfallSiteId, IReadOnlyList<DaggerfallNoticeSheet>> noticeSheets = _ => [];

    /// <summary>
    /// Draws the notice boards in the art pack's wood and parchment (<paramref name="art"/> and the
    /// <paramref name="surfaces"/> its wood names; flat colours without it), each with the sheets
    /// <paramref name="sheets"/> says its site posts now, read as the location's facts are drawn.
    /// </summary>
    internal void UseNoticeBoards(DaggerfallNoticeBoardArt? art, IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtSurface> surfaces,
        Func<DaggerfallSiteId, IReadOnlyList<DaggerfallNoticeSheet>> sheets)
    {
        noticeBoardArt = art;
        noticeBoardSurfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        noticeSheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
    }

    /// <summary>Admits the site's notice boards; their meshes are made when first drawn, one for each sheet layout.</summary>
    private void AdmitNoticeBoards(DaggerfallSiteProfile inputs)
    {
        foreach (DaggerfallSiteNoticeBoard board in inputs.NoticeBoards)
            noticeBoards.Add((board, NextLocationVisualOrdinal()));
    }

    /// <summary>The sheets this site's boards show now: its live postings', at most <see cref="MaxNoticeSheets"/>.</summary>
    internal IReadOnlyList<DaggerfallNoticeSheet> NoticeSheetsShown =>
        inputs.Site is { } site ? [.. noticeSheets(site).Take(MaxNoticeSheets)] : [];

    private void AppendNoticeBoardFacts(List<AppearanceFact> facts, int slot)
    {
        if (noticeBoards.Count == 0)
            return;
        IReadOnlyList<DaggerfallNoticeSheet> sheets = NoticeSheetsShown;
        string layout = string.Join(',', sheets.Select(sheet => (int)sheet));
        if (!noticeBoardMeshes.TryGetValue(layout, out Appearance? mesh))
            noticeBoardMeshes.Add(layout, mesh = CreateNoticeBoardMesh(sheets));
        foreach ((DaggerfallSiteNoticeBoard board, ulong ordinal) in noticeBoards)
        {
            Transform localPose = new(board.Position.ToVector(), NoticeBoardRotation(board.SurfaceNormal), Vector3.One);
            facts.Add(new AppearanceFact(
                DaggerfallPresentationObjectIds.LocationVisual(slot, ordinal),
                false,
                0,
                Compose(worldAppearance.Transform, localPose),
                mesh,
                worldAppearance.Visible,
                worldAppearance.Layer,
                ShadowCasting.Cast));
        }
    }

    private Appearance CreateNoticeBoardMesh(IReadOnlyList<DaggerfallNoticeSheet> sheets)
    {
        noticeBoardBindings ??= NoticeBoardBindings();
        DaggerfallNoticeBoardMeshData data = BuildNoticeBoardMeshData(sheets);
        MeshResource resource = appearance.CreateMeshResource(new MeshResourceCreateRequest(
            data.Positions, data.Normals, data.Uvs, data.Indices, data.Groups, noticeBoardBindings));
        noticeBoardResources.Add(resource);
        Appearance shown = appearance.CreateMeshAppearance(resource);
        noticeBoardResources.Add(shown);
        return shown;
    }

    /// <summary>The board's materials: the art pack's planks and sheets where the set has them, else flat colours.</summary>
    private MeshMaterialBinding[] NoticeBoardBindings()
    {
        Material Wood((int Archive, int Record) record, float r, float g, float b) =>
            noticeBoardArt is not null && noticeBoardSurfaces.TryGetValue(record, out DaggerfallArtSurface? surface)
                ? NoticeBoardTexture(surface.File, TextureWrap.Repeat, .9f)
                : NoticeBoardColour(r, g, b, .9f);
        Material Sheet(DaggerfallArtFile? file) => file is not null
            ? NoticeBoardTexture(file, TextureWrap.Clamp, .85f)
            : NoticeBoardColour(0.86f, 0.79f, 0.62f, .82f);
        DaggerfallNoticeBoardArt? art = noticeBoardArt;
        return
        [
            new(DaggerfallNoticeBoardMeshData.TimberFrameSlot, Wood(art?.Frame ?? default, 0.24f, 0.16f, 0.10f)),
            new(DaggerfallNoticeBoardMeshData.RoofCanopySlot, Wood(art?.Roof ?? default, 0.16f, 0.11f, 0.08f)),
            new(DaggerfallNoticeBoardMeshData.BackboardSlot, Wood(art?.Board ?? default, 0.41f, 0.28f, 0.17f)),
            new(DaggerfallNoticeBoardMeshData.SheetSlot(DaggerfallNoticeSheet.Bounty), Sheet(art?.Bounty)),
            new(DaggerfallNoticeBoardMeshData.SheetSlot(DaggerfallNoticeSheet.Decree), Sheet(art?.Decree)),
            new(DaggerfallNoticeBoardMeshData.SheetSlot(DaggerfallNoticeSheet.Notice), Sheet(art?.Notice)),
        ];
    }

    private Material NoticeBoardTexture(DaggerfallArtFile file, TextureWrap wrap, float roughness)
    {
        RenderResourceInfo texture = appearance.OpenTexture(content, file.Path, file.Sha256, TextureFilter.Linear, wrap);
        Material created = appearance.CreateMaterial(new MaterialRequest(new Color(1f, 1f, 1f, 1f), new RenderResourceReference(texture.Handle),
            roughness, new Color(1f, 1f, 1f, 1f), Vector3.Zero, 0f, false));
        noticeBoardResources.Add(created);
        return created;
    }

    private Material NoticeBoardColour(float r, float g, float b, float roughness)
    {
        Material created = appearance.CreateMaterial(
            new MaterialRequest(Color.FromSrgb(r, g, b), default(RenderResourceReference), roughness,
                new Color(1f, 1f, 1f, 1f), Vector3.Zero, 0f, false) with { FlatShading = true });
        noticeBoardResources.Add(created);
        return created;
    }

    private IEnumerable<IDisposable> TakeNoticeBoardResources()
    {
        List<IDisposable> retired = [.. noticeBoardResources.AsEnumerable().Reverse()];
        noticeBoardResources.Clear();
        noticeBoards.Clear();
        noticeBoardMeshes.Clear();
        noticeBoardBindings = null;
        return retired;
    }

    /// <summary>
    /// Orientates the board's local <c>+Z</c> posted-parchments front face along <paramref name="surfaceNormal"/>
    /// projected onto the horizontal plane.
    /// </summary>
    internal static Quaternion NoticeBoardRotation(Vector3 surfaceNormal)
    {
        Vector3 horizontal = surfaceNormal with { Y = 0f };
        horizontal = horizontal.LengthSquared() > 1e-6f ? Vector3.Normalize(horizontal) : Vector3.UnitZ;
        return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(horizontal.X, horizontal.Z));
    }

    /// <summary>
    /// Builds the Town/Tavern Notice Board mesh in local space: grounded on <c>Y = 0</c>, ~1.48 m wide across <c>X</c>,
    /// ~2.08 m tall along <c>Y</c>, its front face along <c>+Z</c>, with one sheet for each of <paramref name="sheets"/>
    /// (at most <see cref="MaxNoticeSheets"/>) in its slot's group. Every triangle is wound counter-clockwise around its
    /// outward normal, and a sheet's image stands upright on its face.
    /// </summary>
    internal static DaggerfallNoticeBoardMeshData BuildNoticeBoardMeshData(IReadOnlyList<DaggerfallNoticeSheet> sheets)
    {
        var timber = new NoticeBoardPartBuilder();
        var roof = new NoticeBoardPartBuilder();
        var backboard = new NoticeBoardPartBuilder();
        NoticeBoardPartBuilder[] pinned = [new(), new(), new()];

        // 1. Weathered oak posts, grounded base plinths, frame rails, and lower cross-brace.
        timber.AddBox(new(-0.64f, 0.00f, -0.06f), new(-0.53f, 1.92f, 0.06f)); // Left upright post
        timber.AddBox(new(0.53f, 0.00f, -0.06f), new(0.64f, 1.92f, 0.06f));   // Right upright post
        timber.AddBox(new(-0.68f, 0.00f, -0.11f), new(-0.49f, 0.14f, 0.11f)); // Left foot plinth
        timber.AddBox(new(0.49f, 0.00f, -0.11f), new(0.68f, 0.14f, 0.11f));   // Right foot plinth
        timber.AddBox(new(-0.55f, 0.85f, -0.055f), new(0.55f, 0.95f, 0.055f)); // Lower frame rail
        timber.AddBox(new(-0.55f, 1.74f, -0.055f), new(0.55f, 1.84f, 0.055f)); // Upper frame rail
        timber.AddBox(new(-0.55f, 0.30f, -0.04f), new(0.55f, 0.37f, 0.04f));   // Lower stability crossbar

        // 2. Pitched gabled wooden rain canopy overhanging the board.
        AddGabledRoof(roof, halfWidth: 0.74f, ridgeY: 2.08f, eaveY: 1.87f, eaveZ: 0.24f, thickness: 0.055f);

        // 3. Timber backboard panel between the posts and rails.
        backboard.AddBox(
            new(-0.53f, DaggerfallNoticeBoardMeshData.BackboardMinY, -0.032f),
            new(0.53f, DaggerfallNoticeBoardMeshData.BackboardMaxY, 0.032f));

        // 4. One sheet of parchment for each live posting, front face first.
        for (int index = 0; index < Math.Min(sheets.Count, MaxNoticeSheets); index++)
        {
            (float x, float y, float width, float height, float tilt, bool front) = NoticeSheetPlaces[index];
            AddSheet(pinned[(int)sheets[index]], x, y, width, height, tilt, front);
        }

        List<(uint Slot, NoticeBoardPartBuilder Part)> parts =
        [
            (DaggerfallNoticeBoardMeshData.TimberFrameSlot, timber),
            (DaggerfallNoticeBoardMeshData.RoofCanopySlot, roof),
            (DaggerfallNoticeBoardMeshData.BackboardSlot, backboard),
            .. Enum.GetValues<DaggerfallNoticeSheet>().Select(sheet => (DaggerfallNoticeBoardMeshData.SheetSlot(sheet), pinned[(int)sheet])),
        ];

        List<Vector3> positions = [];
        List<Vector3> normals = [];
        List<Vector2> uvs = [];
        List<uint> indices = [];
        List<MeshGroup> groups = [];

        foreach ((uint slot, NoticeBoardPartBuilder part) in parts)
        {
            if (part.Indices.Count == 0) continue;
            uint firstIndex = (uint)indices.Count;
            uint vertexBase = (uint)positions.Count;
            positions.AddRange(part.Positions);
            normals.AddRange(part.Normals);
            uvs.AddRange(part.Uvs);
            foreach (uint idx in part.Indices)
                indices.Add(vertexBase + idx);
            groups.Add(new MeshGroup(slot, firstIndex, (uint)part.Indices.Count));
        }

        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);
        foreach (Vector3 p in positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return new DaggerfallNoticeBoardMeshData([.. positions], [.. normals], [.. uvs], [.. indices], [.. groups], min, max);
    }

    /// <summary>
    /// One sheet pinned flat to the board, a little proud of it, tilted by <paramref name="tiltDegrees"/>: a single face
    /// out from the board, its image's top (<c>v = 0</c>) up and its left on the viewer's left.
    /// </summary>
    private static void AddSheet(NoticeBoardPartBuilder sheet, float centerX, float centerY, float width, float height, float tiltDegrees, bool front)
    {
        float sign = front ? 1f : -1f, z = 0.036f * sign;
        float radians = tiltDegrees * MathF.PI / 180f, cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        Vector3 At(float lx, float ly) => new(centerX + lx * cos - ly * sin, centerY + lx * sin + ly * cos, z);
        // Seen from its own side, the viewer's left is -X on the front face and +X on the back.
        float left = -width * 0.5f * sign, right = width * 0.5f * sign;
        sheet.AddFace([At(left, -height * 0.5f), At(right, -height * 0.5f), At(right, height * 0.5f), At(left, height * 0.5f)],
            Vector3.UnitZ * sign, [new(0f, 1f), new(1f, 1f), new(1f, 0f), new(0f, 0f)]);
    }

    private static void AddGabledRoof(
        NoticeBoardPartBuilder roof,
        float halfWidth,
        float ridgeY,
        float eaveY,
        float eaveZ,
        float thickness)
    {
        // Front slope: ridge (z=0, y=ridgeY) to front eave (z=+eaveZ, y=eaveY), extruded downward by thickness.
        AddSlopedSlab(roof, -halfWidth, halfWidth, 0f, ridgeY, eaveZ, eaveY, thickness);
        // Back slope: ridge (z=0, y=ridgeY) to back eave (z=-eaveZ, y=eaveY), extruded downward by thickness.
        AddSlopedSlab(roof, -halfWidth, halfWidth, -eaveZ, eaveY, 0f, ridgeY, thickness);
        // Ridge beam cap sealing the peak cleanly.
        roof.AddBox(new(-halfWidth, ridgeY - thickness, -0.03f), new(halfWidth, ridgeY + 0.01f, 0.03f));
    }

    private static void AddSlopedSlab(
        NoticeBoardPartBuilder builder,
        float minX,
        float maxX,
        float z0,
        float y0,
        float z1,
        float y1,
        float thickness)
    {
        Vector3 t00 = new(minX, y0, z0);
        Vector3 t10 = new(maxX, y0, z0);
        Vector3 t11 = new(maxX, y1, z1);
        Vector3 t01 = new(minX, y1, z1);

        Vector3 b00 = new(minX, y0 - thickness, z0);
        Vector3 b10 = new(maxX, y0 - thickness, z0);
        Vector3 b11 = new(maxX, y1 - thickness, z1);
        Vector3 b01 = new(minX, y1 - thickness, z1);

        Vector3 slopeDir = new(0f, y1 - y0, z1 - z0);
        Vector3 topNormal = Vector3.Normalize(new Vector3(0f, slopeDir.Z, -slopeDir.Y));
        if (topNormal.Y < 0f) topNormal = -topNormal;

        builder.AddQuad(t00, t10, t11, t01, topNormal);     // Top slope
        builder.AddQuad(b00, b01, b11, b10, -topNormal);    // Underside
        builder.AddQuad(t00, b00, b10, t10, -Vector3.UnitZ); // z0 edge
        builder.AddQuad(t01, t11, b11, b01, Vector3.UnitZ);  // z1 edge
        builder.AddQuad(t00, t01, b01, b00, -Vector3.UnitX); // Left gable end
        builder.AddQuad(t10, b10, b11, t11, Vector3.UnitX);  // Right gable end
    }


    private sealed class NoticeBoardPartBuilder
    {
        internal List<Vector3> Positions { get; } = [];
        internal List<Vector3> Normals { get; } = [];
        internal List<Vector2> Uvs { get; } = [];
        internal List<uint> Indices { get; } = [];

        internal void AddBox(Vector3 min, Vector3 max)
        {
            Vector3 p000 = new(min.X, min.Y, min.Z);
            Vector3 p100 = new(max.X, min.Y, min.Z);
            Vector3 p110 = new(max.X, max.Y, min.Z);
            Vector3 p010 = new(min.X, max.Y, min.Z);
            Vector3 p001 = new(min.X, min.Y, max.Z);
            Vector3 p101 = new(max.X, min.Y, max.Z);
            Vector3 p111 = new(max.X, max.Y, max.Z);
            Vector3 p011 = new(min.X, max.Y, max.Z);

            AddQuad(p001, p101, p111, p011, Vector3.UnitZ);   // +Z
            AddQuad(p100, p000, p010, p110, -Vector3.UnitZ);  // -Z
            AddQuad(p101, p100, p110, p111, Vector3.UnitX);   // +X
            AddQuad(p000, p001, p011, p010, -Vector3.UnitX);  // -X
            AddQuad(p011, p111, p110, p010, Vector3.UnitY);   // +Y
            AddQuad(p000, p100, p101, p001, -Vector3.UnitY);  // -Y
        }

        /// <summary>One quad with its own texture coordinates, given counter-clockwise about <paramref name="normal"/>.</summary>
        internal void AddFace(Vector3[] corners, Vector3 normal, Vector2[] uv)
        {
            uint baseIndex = (uint)Positions.Count;
            Positions.AddRange(corners);
            Normals.AddRange([normal, normal, normal, normal]);
            Uvs.AddRange(uv);
            Indices.AddRange([baseIndex, baseIndex + 1, baseIndex + 2, baseIndex, baseIndex + 2, baseIndex + 3]);
        }

        internal void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outwardHint)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() <= 1e-10f)
                return;

            // Enforce counter-clockwise winding around the outward normal so back-face culling never flips a face.
            if (Vector3.Dot(cross, outwardHint) < 0f)
            {
                (b, d) = (d, b);
                cross = Vector3.Cross(b - a, c - a);
            }

            Vector3 normal = Vector3.Normalize(cross);
            uint baseIndex = (uint)Positions.Count;
            Positions.Add(a);
            Positions.Add(b);
            Positions.Add(c);
            Positions.Add(d);
            Normals.Add(normal);
            Normals.Add(normal);
            Normals.Add(normal);
            Normals.Add(normal);
            Uvs.Add(new(0f, 0f));
            Uvs.Add(new(1f, 0f));
            Uvs.Add(new(1f, 1f));
            Uvs.Add(new(0f, 1f));
            Indices.Add(baseIndex);
            Indices.Add(baseIndex + 1);
            Indices.Add(baseIndex + 2);
            Indices.Add(baseIndex);
            Indices.Add(baseIndex + 2);
            Indices.Add(baseIndex + 3);
        }
    }
}
