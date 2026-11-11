using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class UiProjectionPublisherTests
{
    [Fact]
    public void A_cleared_builder_writes_a_value_alike_to_a_new_one()
    {
        UiValueBuilder reused = new();
        Write(reused, "a longer label than the first", 99);
        reused.Clear();
        uint root = Write(reused, "café", 2);
        UiValueBuilder fresh = new();
        uint freshRoot = Write(fresh, "café", 2);

        UiValue built = reused.Build(root), expected = fresh.Build(freshRoot);
        Assert.Equal(expected.Root, built.Root);
        Assert.Equal(expected.Nodes.ToArray(), built.Nodes.ToArray());
        Assert.Equal(expected.Edges.ToArray(), built.Edges.ToArray());
        Assert.Equal(expected.Utf8.ToArray(), built.Utf8.ToArray());
    }

    [Fact]
    public void A_builder_grows_past_its_first_storage()
    {
        UiValueBuilder builder = new();
        uint[] items = [.. Enumerable.Range(0, 500).Select(index => builder.Object(("name", builder.String(new string('x', index % 40))), ("index", builder.Number(index))))];
        UiValue value = builder.Build(builder.Array(items));

        // Each item is two values, their two keyed copies and its object; then the array.
        Assert.Equal(2501, value.Nodes.Length);
        Assert.Equal(1500, value.Edges.Length);
    }

    [Fact]
    public void A_projection_is_published_only_when_it_changes()
    {
        RecordingUi ui = new();
        UiStream stream = ui.OpenStream(new UiStreamRequest("test.hud", "test.v1"));
        UiProjectionPublisher publisher = new(ui, stream);
        UiValueBuilder builder = new();

        Assert.True(publisher.Publish(builder, Write(builder, "north", 1)));
        builder.Clear();
        Assert.False(publisher.Publish(builder, Write(builder, "north", 1)));
        builder.Clear();
        Assert.True(publisher.Publish(builder, Write(builder, "south", 1)));
        builder.Clear();
        Assert.True(publisher.Publish(builder, Write(builder, "south", 2)));

        Assert.Equal(3, ui.Published.Count);
        UiProjection last = ui.Published[^1];
        Assert.Same(stream, last.Stream);
        UiValueBuilder expected = new();
        uint expectedRoot = Write(expected, "south", 2);
        Assert.Equal(expected.Nodes.ToArray(), last.Value.Nodes.ToArray());
        Assert.Equal(expected.Utf8.ToArray(), last.Value.Utf8.ToArray());
        Assert.Equal(expectedRoot, last.Value.Root);
    }

    private static uint Write(UiValueBuilder builder, string heading, double count) => builder.Object(
        ("heading", builder.String(heading)), ("count", builder.Number(count)),
        ("marks", builder.Array(builder.Boolean(true), builder.Null())));

    private sealed class RecordingUi : IUiService
    {
        internal List<UiProjection> Published { get; } = [];

        public UiStream OpenStream(UiStreamRequest arg0) => new(new UiStreamHandle(1), () => { });

        public void PublishProjection(UiProjection arg0) => Published.Add(arg0);

        public UiImage OpenImage(UiImageRequest arg0) => throw new NotSupportedException();

        public UiFont OpenFont(UiFontRequest arg0) => throw new NotSupportedException();
    }
}
