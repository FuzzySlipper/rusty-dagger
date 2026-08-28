using WorldRpg.Kit.Facts;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class FactBufferTests
{
    [Fact]
    public void Delivery_uses_a_stable_snapshot()
    {
        FactBuffer<TestFact> buffer = new();
        List<string> delivered = [];
        buffer.Append(new TestFact("first"));

        buffer.Deliver(fact =>
        {
            delivered.Add(fact.Value);
            buffer.Append(new TestFact("later"));
        });

        Assert.Equal(["first"], delivered);
        buffer.Deliver(fact => delivered.Add(fact.Value));
        Assert.Equal(["first", "later"], delivered);
    }

    private sealed record TestFact(string Value) : IWorldRpgFact;
}
