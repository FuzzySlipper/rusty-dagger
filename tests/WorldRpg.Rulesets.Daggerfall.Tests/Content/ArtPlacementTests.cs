using System.Numerics;
using System.Text;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A flat, a person or a tree of the art pack carries its own placement, and one that lacks a value its kind needs is
/// refused by name when the pack is read: nothing falls back to the classic art's size, pivot, frames or rate.
/// </summary>
public sealed class ArtPlacementTests
{
    private const string Hash = "0000000000000001000000000000000200000000000000030000000000000004";

    private static DaggerfallArtReplacements Read(string section) =>
        DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes("{\"artReplacements\": {\"images\": [], " + section.Replace("HASH", Hash, StringComparison.Ordinal) + "}}"), "test.art");

    private static string Flat(string kind, int frames, string placement) =>
        "\"" + kind + "\": [{\"replaces\": {\"archive\": 210, \"record\": 3}, \"path\": \"a.png\", \"byteLength\": 1, \"sha256\": \"HASH\", \"width\": " + (32 * frames)
        + ", \"height\": 32, \"frames\": [" + string.Join(",", Enumerable.Range(0, frames).Select(frame => "{\"x\": " + (32 * frame) + ", \"y\": 0, \"width\": 32, \"height\": 32}"))
        + "]" + placement + "}]";

    private const string Own = "\"size\": {\"width\": 2, \"height\": 4}, \"pivot\": {\"x\": 0.5, \"y\": 0.5}, \"foot\": {\"x\": 0.5, \"y\": 0.1}";

    [Fact]
    public void A_flat_and_a_person_read_their_own_size_pivot_foot_and_rate()
    {
        DaggerfallArtFlat flat = Read(Flat("flats", 3, ", \"placement\": {" + Own + ", \"framesPerSecond\": 5}")).Flats[(210, 3)];
        Assert.Equal((new Vector2(2, 4), new Vector2(.5F, .5F), new Vector2(.5F, .1F), 5F), (flat.Size, flat.Pivot, flat.Foot, flat.FramesPerSecond));
        // A person shows its one frame and states no rate.
        DaggerfallArtFlat person = Read(Flat("people", 1, ", \"placement\": {" + Own + "}")).People[(210, 3)];
        Assert.Equal((new Vector2(2, 4), 0F), (person.Size, person.FramesPerSecond));
    }

    [Theory]
    [InlineData("flats", 1, "", "states no placement for the refreshed flat for 210/3")]
    [InlineData("flats", 1, ", \"placement\": {\"pivot\": {\"x\": 0.5, \"y\": 0.5}, \"foot\": {\"x\": 0.5, \"y\": 0}}", "states no size in the placement of the refreshed flat for 210/3")]
    [InlineData("flats", 1, ", \"placement\": {\"size\": {\"width\": 2, \"height\": 4}, \"foot\": {\"x\": 0.5, \"y\": 0}}", "states no pivot in the placement of the refreshed flat for 210/3")]
    [InlineData("flats", 3, ", \"placement\": {\"size\": {\"width\": 2, \"height\": 4}, \"pivot\": {\"x\": 0.5, \"y\": 0.5}, \"foot\": {\"x\": 0.5, \"y\": 0}}", "states no framesPerSecond in the placement of the refreshed flat for 210/3")]
    [InlineData("people", 1, ", \"placement\": {\"size\": {\"width\": 2, \"height\": 4}, \"pivot\": {\"x\": 0.5, \"y\": 0.5}}", "states no foot in the placement of the refreshed person for 210/3")]
    public void An_entry_missing_a_value_its_kind_needs_is_refused_by_name(string kind, int frames, string placement, string expected)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Read(Flat(kind, frames, placement)));
        Assert.Contains(expected, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sprite_mesh_states_the_size_it_stands_at_or_is_refused_by_name()
    {
        const string mesh = "\"meshes\": [{\"replaces\": {\"archive\": 212, \"record\": 0}, \"path\": \"well.glb\", \"byteLength\": 1, \"sha256\": \"HASH\"";
        Assert.Equal(new Vector2(1.9F, 2.6F), Read(mesh + ", \"placement\": {\"size\": {\"width\": 1.9, \"height\": 2.6}}}]").Meshes[(212, 0)].Size);
        Assert.Contains("states no placement for the refreshed mesh for 212/0", Assert.Throws<InvalidOperationException>(() => Read(mesh + "}]")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tree_states_its_height_and_foot_in_metres_or_is_refused_by_name()
    {
        const string mesh = "\"mesh\": {\"path\": \"m.json\", \"sha256\": \"HASH\", \"height\": 9}";
        DaggerfallTreeArt tree = Read("\"trees\": {\"500-12\": {" + mesh + ", \"placement\": {\"height\": 8.5, \"foot\": 0.25}}}").Trees[(500, 12)];
        Assert.Equal((9F, 8.5F, .25F), (tree.MeshHeight, tree.Height, tree.Foot));
        Assert.Contains("states no placement for tree '500-12'",
            Assert.Throws<InvalidOperationException>(() => Read("\"trees\": {\"500-12\": {" + mesh + "}}")).Message, StringComparison.Ordinal);
        Assert.Contains("states no foot in the placement of tree '500-12'",
            Assert.Throws<InvalidOperationException>(() => Read("\"trees\": {\"500-12\": {" + mesh + ", \"placement\": {\"height\": 8.5}}}")).Message, StringComparison.Ordinal);
    }
}
