using Rusty.Engine;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="TestPayload"/> shares one <see cref="BundleContentFake"/> across parallel test classes, each calling it from
/// its own thread, so the fake must stay correct under concurrent calls (#10780, #10802).
/// </summary>
public sealed class BundleContentFakeTests
{
    private const string Container = "data.rpak";
    private const int Threads = 8;

    [Fact]
    public void References_opened_from_several_threads_at_once_each_read_their_own_file()
    {
        BundleContentFake fake = new();
        fake.Container(Container);
        const int FileCount = 64;
        for (int index = 0; index < FileCount; index++) fake.Add(Container, $"terrain/{index}.bin", Body(index));
        using ContentBundle bundle = fake.OpenContainer(new ContentContainerOpenRequest(Container));

        OnThreads(thread =>
        {
            for (int pass = 0; pass < 2_000; pass++)
            {
                int index = (thread * 31 + pass) % FileCount;
                string path = $"terrain/{index}.bin";
                using ContentReference reference = fake.OpenBundleReference(new ContentBundleReferenceRequest(bundle, path));
                Assert.Equal(path, fake.ReadReferenceInfo(reference).Span[0].Path);
                Assert.Equal(Body(index), fake.ReadBytes(new ContentReadBytesRequest(reference, 0, 1024)).ToArray());
            }
        });
    }

    [Fact]
    public void A_file_first_resolved_from_several_threads_at_once_keeps_its_digest()
    {
        BundleContentFake fake = new();
        const int FileCount = 2_000;
        for (int index = 0; index < FileCount; index++) fake.AddEager($"worldrpg/eager/{index}.json", Body(index));

        // Every thread resolves each file in the same order, so the first measures of its digest race one another.
        OnThreads(_ =>
        {
            for (int index = 0; index < FileCount; index++)
            {
                using ContentReference reference = fake.ResolveReference(new ContentResolveRequest($"worldrpg/eager/{index}.json", Digest(Body(index))));
            }
        });
    }

    private static byte[] Body(int index) => System.Text.Encoding.UTF8.GetBytes($"file {index} {new string('x', index % 97)}");

    /// <summary>Runs the work on several threads released together, and fails with every exception any of them met.</summary>
    private static void OnThreads(Action<int> work)
    {
        using Barrier start = new(Threads);
        Task[] running = [.. Enumerable.Range(0, Threads).Select(thread => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait();
            work(thread);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))];
        Assert.True(Task.WaitAll(running, TimeSpan.FromSeconds(60)), "The fake's calls did not finish; a dictionary written from two threads can loop.");
    }
}
