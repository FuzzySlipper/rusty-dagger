using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>
/// The loading screen acknowledges slow work before running any of it, runs one phase per update it is given,
/// keeps its full bar for one more update, then clears; a failing phase clears it and reaches the work's handler.
/// </summary>
public sealed class LoadingScreenTests
{
    [Fact]
    public void Work_is_published_before_any_phase_runs_and_each_update_runs_one_phase_then_the_screen_clears()
    {
        int changes = 0;
        LoadingScreen screen = new(() => changes++);
        List<string> ran = [];

        Assert.False(screen.Advance());
        Assert.True(screen.Start("Travelling to Daggerfall", [
            new("Reading Daggerfall", () => ran.Add("read")),
            new("Travelling to Daggerfall", () => ran.Add("travel")),
        ], _ => throw new InvalidOperationException("No phase fails here.")));

        // Accepted: shown at once, nothing run.
        Assert.Empty(ran);
        Assert.True(screen.IsLoading);
        Assert.Equal(new LoadingState("Travelling to Daggerfall", "Reading Daggerfall", 0, 2, 0d), screen.Current);
        Assert.Equal(new Dictionary<string, object?> { ["title"] = "Travelling to Daggerfall", ["label"] = "Reading Daggerfall",
            ["step"] = 0d, ["steps"] = 2d, ["fraction"] = 0d }, Written(screen));
        // One screen shows one piece of work.
        Assert.False(screen.Start("Something else", [new("Else", () => ran.Add("else"))], _ => { }));

        Assert.True(screen.Advance());
        Assert.Equal(["read"], ran);
        Assert.Equal(new LoadingState("Travelling to Daggerfall", "Travelling to Daggerfall", 1, 2, .5d), screen.Current);
        Assert.True(screen.Advance());
        Assert.Equal(["read", "travel"], ran);
        // The finished work keeps a full bar for the update that draws what it loaded.
        Assert.Equal(new LoadingState("Travelling to Daggerfall", "Travelling to Daggerfall", 2, 2, 1d), screen.Current);

        // The next update clears it and belongs to the caller again.
        Assert.False(screen.Advance());
        Assert.False(screen.IsLoading);
        Assert.Null(screen.Current);
        Assert.Null(Written(screen));
        Assert.Equal(4, changes);
    }

    [Fact]
    public void A_single_phase_is_unmeasured_and_shows_a_moving_bar_rather_than_a_percentage()
    {
        LoadingScreen screen = new(() => { });
        screen.Start("Entering the temple", [new("Entering the temple", () => { })], _ => { });
        Assert.Null(screen.Current!.Fraction);
        Assert.Null(Written(screen)!["fraction"]);
        Assert.True(screen.Advance());
        Assert.Null(screen.Current!.Fraction);
    }

    [Fact]
    public void A_phase_that_throws_clears_the_screen_skips_the_rest_and_reaches_the_failure_handler()
    {
        int changes = 0;
        LoadingScreen screen = new(() => changes++);
        List<string> ran = [];
        Exception? reported = null;
        screen.Start("Loading the saved game", [
            new("Restoring the saved world", () => throw new InvalidOperationException("The save names a missing site.")),
            new("Entering the world", () => ran.Add("enter")),
        ], error => reported = error);

        Assert.True(screen.Advance());
        Assert.Equal("The save names a missing site.", reported?.Message);
        Assert.Empty(ran);
        Assert.False(screen.IsLoading);
        Assert.Null(Written(screen));
        Assert.Equal(2, changes);
        // The screen is free for the next piece of work.
        Assert.True(screen.Start("Again", [new("Again", () => ran.Add("again"))], _ => { }));
    }

    [Fact]
    public void Work_run_at_once_runs_every_phase_in_order_and_a_failure_reaches_the_caller()
    {
        List<string> ran = [];
        LoadingScreen.RunNow([new("One", () => ran.Add("one")), new("Two", () => ran.Add("two"))]);
        Assert.Equal(["one", "two"], ran);
        Assert.Throws<ArgumentException>(() => LoadingScreen.RunNow([new("Bad", () => throw new ArgumentException("bad")), new("Never", () => ran.Add("never"))]));
        Assert.DoesNotContain("never", ran);
    }

    /// <summary>The <c>loading</c> value the screen writes into a projection, decoded; null when nothing is loading.</summary>
    private static Dictionary<string, object?>? Written(LoadingScreen screen)
    {
        UiValueBuilder builder = new();
        UiValue value = builder.Build(screen.Write(builder));
        return (Dictionary<string, object?>?)Decode(value, value.Root);
    }

    private static object? Decode(UiValue value, uint index)
    {
        StructuredValueNode node = value.Nodes.Span[checked((int)index)];
        return node.Kind switch
        {
            StructuredValueKind.Null => null,
            StructuredValueKind.String => Text(value, node.TextOffset, node.TextLen),
            StructuredValueKind.Number => node.NumberValue,
            StructuredValueKind.Object => Enumerable.Range(0, checked((int)node.ChildCount))
                .Select(offset => value.Edges.Span[checked((int)(node.FirstEdge + offset))])
                .ToDictionary(edge => Text(value, value.Nodes.Span[checked((int)edge)].KeyOffset, value.Nodes.Span[checked((int)edge)].KeyLen),
                    edge => Decode(value, edge), StringComparer.Ordinal),
            _ => throw new NotSupportedException(node.Kind.ToString()),
        };
    }

    private static string Text(UiValue value, uint offset, uint length) =>
        System.Text.Encoding.UTF8.GetString(value.Utf8.Span[checked((int)offset)..checked((int)(offset + length))]);
}
