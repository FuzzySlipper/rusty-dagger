using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Built 3D geometry for the dedicated physical Town/Tavern Notice Board mesh (#11082):
/// weathered oak posts &amp; frame, gabled rain canopy, warm timber backboard, pinned parchment notices
/// (front and back faces), sepia ink lines, and crimson wax seals.
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
    internal const uint ParchmentSlot = 3;
    internal const uint InkLinesSlot = 4;
    internal const uint WaxSealSlot = 5;

    internal const float BackboardMinY = 0.95f;
    internal const float BackboardMaxY = 1.74f;
}

internal sealed partial class DaggerfallSiteAppearance
{
    private readonly List<(DaggerfallSiteNoticeBoard Definition, ulong Ordinal)> noticeBoards = [];
    private readonly List<IDisposable> noticeBoardResources = [];
    private Appearance? noticeBoardAppearance;

    /// <summary>
    /// Admits one shared 3D Notice Board mesh appearance for every physical <see cref="DaggerfallSiteNoticeBoard"/>
    /// assembled into <paramref name="inputs"/>. Sites without notice boards allocate zero Engine resources.
    /// </summary>
    private void AdmitNoticeBoards(DaggerfallSiteProfile inputs)
    {
        if (inputs.NoticeBoards.Count == 0)
            return;

        DaggerfallNoticeBoardMeshData data = BuildNoticeBoardMeshData();
        Material timber = CreateNoticeBoardMaterial(0.24f, 0.16f, 0.10f, 0.90f);
        Material roof = CreateNoticeBoardMaterial(0.16f, 0.11f, 0.08f, 0.92f);
        Material backboard = CreateNoticeBoardMaterial(0.41f, 0.28f, 0.17f, 0.88f);
        Material parchment = CreateNoticeBoardMaterial(0.86f, 0.79f, 0.62f, 0.82f);
        Material ink = CreateNoticeBoardMaterial(0.21f, 0.15f, 0.10f, 0.90f);
        Material wax = CreateNoticeBoardMaterial(0.64f, 0.13f, 0.11f, 0.42f);

        MeshMaterialBinding[] bindings =
        [
            new(DaggerfallNoticeBoardMeshData.TimberFrameSlot, timber),
            new(DaggerfallNoticeBoardMeshData.RoofCanopySlot, roof),
            new(DaggerfallNoticeBoardMeshData.BackboardSlot, backboard),
            new(DaggerfallNoticeBoardMeshData.ParchmentSlot, parchment),
            new(DaggerfallNoticeBoardMeshData.InkLinesSlot, ink),
            new(DaggerfallNoticeBoardMeshData.WaxSealSlot, wax),
        ];

        MeshResource mesh = appearance.CreateMeshResource(new MeshResourceCreateRequest(
            data.Positions, data.Normals, data.Uvs, data.Indices, data.Groups, bindings));
        noticeBoardResources.Add(mesh);
        noticeBoardAppearance = appearance.CreateMeshAppearance(mesh);
        noticeBoardResources.Add(noticeBoardAppearance);

        foreach (DaggerfallSiteNoticeBoard board in inputs.NoticeBoards)
            noticeBoards.Add((board, NextLocationVisualOrdinal()));
    }

    private Material CreateNoticeBoardMaterial(float r, float g, float b, float roughness)
    {
        Material created = appearance.CreateMaterial(
            new MaterialRequest(Color.FromSrgb(r, g, b), default(RenderResourceReference), roughness,
                new Color(1f, 1f, 1f, 1f), Vector3.Zero, 0f, false) with { FlatShading = true });
        noticeBoardResources.Add(created);
        return created;
    }

    private void AppendNoticeBoardFacts(List<AppearanceFact> facts, int slot)
    {
        if (noticeBoardAppearance is not { } mesh || noticeBoards.Count == 0)
            return;

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

    private IEnumerable<IDisposable> TakeNoticeBoardResources()
    {
        List<IDisposable> retired = [.. noticeBoardResources.AsEnumerable().Reverse()];
        noticeBoardResources.Clear();
        noticeBoards.Clear();
        noticeBoardAppearance = null;
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
    /// Builds the dedicated 3D Town/Tavern Notice Board mesh in local space: grounded on <c>Y = 0</c>,
    /// ~1.48 m wide across <c>X</c>, ~2.08 m tall along <c>Y</c>, front posted face oriented along <c>+Z</c>.
    /// Every triangle is wound counter-clockwise around its outward normal.
    /// </summary>
    internal static DaggerfallNoticeBoardMeshData BuildNoticeBoardMeshData()
    {
        var timber = new NoticeBoardPartBuilder();
        var roof = new NoticeBoardPartBuilder();
        var backboard = new NoticeBoardPartBuilder();
        var parchment = new NoticeBoardPartBuilder();
        var ink = new NoticeBoardPartBuilder();
        var wax = new NoticeBoardPartBuilder();

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

        // 3. Warm wooden backboard panel between the posts and rails.
        backboard.AddBox(
            new(-0.53f, DaggerfallNoticeBoardMeshData.BackboardMinY, -0.032f),
            new(0.53f, DaggerfallNoticeBoardMeshData.BackboardMaxY, 0.032f));

        // 4. Pinned parchment notices with sepia ink text lines and crimson wax seals (front +Z face).
        AddPostedSheet(parchment, ink, wax, centerX: -0.24f, centerY: 1.51f, width: 0.34f, height: 0.27f, tiltDegrees: 2.4f, frontFace: true, inkRows: 3);
        AddPostedSheet(parchment, ink, wax, centerX: 0.23f, centerY: 1.48f, width: 0.28f, height: 0.33f, tiltDegrees: -2.8f, frontFace: true, inkRows: 4);
        AddPostedSheet(parchment, ink, wax, centerX: -0.22f, centerY: 1.15f, width: 0.29f, height: 0.24f, tiltDegrees: -1.9f, frontFace: true, inkRows: 3);
        AddPostedSheet(parchment, ink, wax, centerX: 0.21f, centerY: 1.14f, width: 0.32f, height: 0.22f, tiltDegrees: 2.1f, frontFace: true, inkRows: 2);

        // 5. Two pinned notices on the back (-Z) face so the prop reads as a notice board from every angle.
        AddPostedSheet(parchment, ink, wax, centerX: -0.18f, centerY: 1.40f, width: 0.30f, height: 0.28f, tiltDegrees: -2.0f, frontFace: false, inkRows: 3);
        AddPostedSheet(parchment, ink, wax, centerX: 0.20f, centerY: 1.28f, width: 0.28f, height: 0.25f, tiltDegrees: 2.5f, frontFace: false, inkRows: 2);

        (uint Slot, NoticeBoardPartBuilder Part)[] parts =
        [
            (DaggerfallNoticeBoardMeshData.TimberFrameSlot, timber),
            (DaggerfallNoticeBoardMeshData.RoofCanopySlot, roof),
            (DaggerfallNoticeBoardMeshData.BackboardSlot, backboard),
            (DaggerfallNoticeBoardMeshData.ParchmentSlot, parchment),
            (DaggerfallNoticeBoardMeshData.InkLinesSlot, ink),
            (DaggerfallNoticeBoardMeshData.WaxSealSlot, wax),
        ];

        List<Vector3> positions = [];
        List<Vector3> normals = [];
        List<Vector2> uvs = [];
        List<uint> indices = [];
        List<MeshGroup> groups = [];

        foreach ((uint slot, NoticeBoardPartBuilder part) in parts)
        {
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

        return new DaggerfallNoticeBoardMeshData(
            [.. positions],
            [.. normals],
            [.. uvs],
            [.. indices],
            [.. groups],
            min,
            max);
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

    private static void AddPostedSheet(
        NoticeBoardPartBuilder parchment,
        NoticeBoardPartBuilder ink,
        NoticeBoardPartBuilder wax,
        float centerX,
        float centerY,
        float width,
        float height,
        float tiltDegrees,
        bool frontFace,
        int inkRows)
    {
        float sign = frontFace ? 1f : -1f;
        float boardSurfaceZ = 0.032f * sign;
        float sheetOuterZ = 0.041f * sign;
        float zMin = MathF.Min(boardSurfaceZ, sheetOuterZ);
        float zMax = MathF.Max(boardSurfaceZ, sheetOuterZ);

        float radians = tiltDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        Vector3 TransformLocal(float lx, float ly, float z) => new(
            centerX + (lx * cos) - (ly * sin),
            centerY + (lx * sin) + (ly * cos),
            z);

        // Parchment sheet body.
        AddRotatedPlate(parchment, TransformLocal, -width * 0.5f, width * 0.5f, -height * 0.5f, height * 0.5f, zMin, zMax);

        // Sepia ink heading & text bars proud of the parchment surface.
        float inkZ0 = sheetOuterZ;
        float inkZ1 = sheetOuterZ + (0.0025f * sign);
        float inkMinZ = MathF.Min(inkZ0, inkZ1);
        float inkMaxZ = MathF.Max(inkZ0, inkZ1);
        float marginX = width * 0.14f;
        float topY = (height * 0.5f) - (height * 0.24f);
        float stepY = (height * 0.56f) / Math.Max(inkRows, 1);
        for (int row = 0; row < inkRows; row++)
        {
            float rowY = topY - (row * stepY);
            float lineHalfH = row == 0 ? 0.011f : 0.007f;
            float rightTrim = row == inkRows - 1 ? width * 0.24f : marginX;
            AddRotatedPlate(ink, TransformLocal, (-width * 0.5f) + marginX, (width * 0.5f) - rightTrim,
                rowY - lineHalfH, rowY + lineHalfH, inkMinZ, inkMaxZ);
        }

        // Crimson wax seal / tack near the top center of the posted notice.
        float sealHalf = 0.019f;
        float sealY = (height * 0.5f) - 0.032f;
        float sealZ0 = sheetOuterZ;
        float sealZ1 = sheetOuterZ + (0.006f * sign);
        AddRotatedPlate(wax, TransformLocal, -sealHalf, sealHalf, sealY - sealHalf, sealY + sealHalf,
            MathF.Min(sealZ0, sealZ1), MathF.Max(sealZ0, sealZ1));
    }

    private static void AddRotatedPlate(
        NoticeBoardPartBuilder builder,
        Func<float, float, float, Vector3> map,
        float lx0,
        float lx1,
        float ly0,
        float ly1,
        float zMin,
        float zMax)
    {
        Vector3 f00 = map(lx0, ly0, zMax);
        Vector3 f10 = map(lx1, ly0, zMax);
        Vector3 f11 = map(lx1, ly1, zMax);
        Vector3 f01 = map(lx0, ly1, zMax);

        Vector3 b00 = map(lx0, ly0, zMin);
        Vector3 b10 = map(lx1, ly0, zMin);
        Vector3 b11 = map(lx1, ly1, zMin);
        Vector3 b01 = map(lx0, ly1, zMin);

        Vector3 right = Vector3.Normalize(f10 - f00);
        Vector3 up = Vector3.Normalize(f01 - f00);
        builder.AddQuad(f00, f10, f11, f01, Vector3.UnitZ);  // +Z face
        builder.AddQuad(b10, b00, b01, b11, -Vector3.UnitZ); // -Z face
        builder.AddQuad(f01, f11, b11, b01, up);             // Top edge
        builder.AddQuad(b00, b10, f10, f00, -up);            // Bottom edge
        builder.AddQuad(f10, b10, b11, f11, right);          // Right edge
        builder.AddQuad(b00, f00, f01, b01, -right);         // Left edge
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
