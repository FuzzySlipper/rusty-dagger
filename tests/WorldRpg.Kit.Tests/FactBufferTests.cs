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

    [Fact]
    public void Throwing_reaction_does_not_replay_the_delivered_batch()
    {
        FactBuffer<TestFact> buffer = new();
        List<string> delivered = [];
        buffer.Append(new TestFact("first"));
        buffer.Append(new TestFact("throws"));
        buffer.Append(new TestFact("after"));

        Assert.Throws<InvalidOperationException>(() => buffer.Deliver(fact =>
        {
            delivered.Add(fact.Value);
            if (fact.Value == "throws")
            {
                buffer.Append(new TestFact("published-during-failure"));
                throw new InvalidOperationException("reaction failure");
            }
        }));

        Assert.Equal(["first", "throws"], delivered);
        buffer.Deliver(fact => delivered.Add(fact.Value));
        Assert.Equal(["first", "throws", "published-during-failure"], delivered);
    }

    private sealed record TestFact(string Value) : IWorldRpgFact;
}
