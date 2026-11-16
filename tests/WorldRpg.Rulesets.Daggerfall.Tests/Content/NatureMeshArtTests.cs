using System.Text;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The art pack's nature meshes beyond trees (#10849): a rock stands in for its flora sprite as a mesh of stone, which its
/// entry names a texture for and the wind does not move, and a winter rock carries snow as a winter crown does.
/// </summary>
public sealed class NatureMeshArtTests
{
    private const string Hash = "0000000000000001000000000000000200000000000000030000000000000004";

    private static DaggerfallArtReplacements Art(string trees) =>
        DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes("""{"artReplacements": {"images": [], "trees": """ + trees.Replace("HASH", Hash, StringComparison.Ordinal) + "}}"),
            "test.art");

    private const string Mesh = """ "mesh": {"path": "trees/rock.prop-mesh.json", "sha256": "HASH", "height": 0.9}, "height": 0.7, "foot": 0.05""";

    [Fact]
    public void A_rock_names_its_stone_and_no_bark_and_a_winter_one_its_snow()
    {
        DaggerfallTreeArt rock = Art("{\"505-3\": {" + Mesh + """, "stone": {"path": "trees/rock-stone.png", "sha256": "HASH"}, "snow": {"share": 0.4, "colour": [0.8, 0.8, 0.85], "texels": 64}}}""")
            .Trees[(505, 3)];
        Assert.Null(rock.Bark);
        Assert.Equal("trees/rock-stone.png", rock.Stone!.Path);
        Assert.Equal((0.4F, 64), (rock.Snow!.Value.Share, rock.Snow.Value.Texels));
    }

    /// <summary>The mesh's stone part draws with the stone's material, and a part the art has no texture for is refused by name.</summary>
    [Fact]
    public void A_rock_mesh_s_stone_part_draws_its_stone_and_bark_it_has_no_texture_for_is_refused()
    {
        const string part = """{"role": "ROLE", "positions": [0, 0, 0, 1, 0, 0, 0, 1, 0], "normals": [0, 0, 1, 0, 0, 1, 0, 0, 1], "uvs": [0, 0, 1, 0, 0, 1], "colors": [1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0], "indices": [0, 1, 2]}""";
        byte[] mesh = Encoding.UTF8.GetBytes("""{"height": 1, "parts": [""" + part.Replace("ROLE", "stone", StringComparison.Ordinal) + "]}");
        ContentFake content = new([]);
        content.Add("trees/rock.prop-mesh.json", mesh);
        DaggerfallTreeArt art = new(new DaggerfallArtFile("trees/rock.prop-mesh.json", DaggerfallContentHash.Parse(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(mesh)), "rock")),
            1, 1, 0, null, null, Stone: new DaggerfallArtFile("trees/rock-stone.png", DaggerfallContentHash.Parse(Hash, "stone")));
        Material stone = new(new MaterialHandle(7), () => { });

        MeshResourceCreateRequest request = DaggerfallTreeMesh.Read(content, art, 2, null, null, stone);
        Assert.Equal(stone, Assert.Single(request.Bindings.ToArray()).Material);
        Assert.Equal(2F, request.Positions.Span[2].Y);

        byte[] bark = Encoding.UTF8.GetBytes("""{"height": 1, "parts": [""" + part.Replace("ROLE", "bark", StringComparison.Ordinal) + "]}");
        content.Add("trees/rock.prop-mesh.json", bark);
        DaggerfallTreeArt barked = art with { Mesh = new DaggerfallArtFile("trees/rock.prop-mesh.json", DaggerfallContentHash.Parse(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bark)), "bark")) };
        Assert.Contains("has bark, but its art names no bark texture",
            Assert.Throws<InvalidDataException>(() => DaggerfallTreeMesh.Read(content, barked, 2, null, null, stone)).Message, StringComparison.Ordinal);
    }
}
