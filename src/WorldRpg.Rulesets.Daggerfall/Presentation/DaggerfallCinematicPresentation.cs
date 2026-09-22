using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record DaggerfallCinematicResult(string Source, VideoRealizationFactKind Kind, string? Failure);

/// <summary>Ruleset narrative media selection over the Engine-owned playback and content lifetime.</summary>
internal sealed class DaggerfallCinematicPresentation(IEngineContext engine, ProductContent content,
    DaggerfallCinematicSet catalog) : IDisposable
{
    internal const string BundleId = "daggerfall.cinematics";
    private const string Root = "worldrpg/media/cinematics/";
    private (string Source, VideoPlaybackHandle Handle)? _active;
    private ulong _lastFact;
    private ulong _lostFacts;
    internal string? ActiveSource => _active?.Source;
    internal DaggerfallCinematicResult? LastResult { get; private set; }

    internal void Play(string sourceFile)
    {
        DaggerfallCinematicDefinition source = catalog.Resolve(sourceFile);
        DaggerfallCinematicArtifact artifact = source.Artifact
            ?? throw new InvalidOperationException($"Cinematic '{sourceFile}' has source provenance but no accepted packaged artifact.");
        Stop();
        _lostFacts = engine.Video.ReadRealization().EvictedFactCount;
        using ProductContentBundle bundle = content.OpenBundle(BundleId);
        using ContentReference reference = bundle.OpenReference(artifact.Path[Root.Length..]);
        VideoPlaybackHandle handle = engine.Video.PlayFromContent(new PlayVideoFromContentRequest(reference));
        _active = (source.FileName, handle);
        LastResult = null;
    }

    internal void Skip()
    {
        if (_active is not { } active) return;
        engine.Video.Skip(active.Handle);
        LastResult = new(active.Source, VideoRealizationFactKind.Skipped, null);
        _active = null;
    }

    internal void Stop()
    {
        if (_active is not { } active) return;
        engine.Video.Stop(active.Handle);
        _active = null;
    }

    internal void Poll()
    {
        if (_active is not { } active) return;
        VideoRealizationReadout readout = engine.Video.ReadRealization();
        for (uint index = 0; index < readout.RetainedFactCount; index++)
        {
            VideoRealizationFactAtReceipt fact = engine.Video.ReadRealizationFactAt(new VideoRealizationFactAtRequest(index));
            if (!fact.Present || fact.FactId <= _lastFact) continue;
            _lastFact = fact.FactId;
            if (fact.Handle != active.Handle) continue;
            if (fact.Kind is not (VideoRealizationFactKind.Completed or VideoRealizationFactKind.Skipped or VideoRealizationFactKind.Failed)) continue;
            LastResult = new(active.Source, fact.Kind,
                fact.Kind == VideoRealizationFactKind.Failed ? fact.Failure.ToString() : null);
            Stop();
            break;
        }
        if (_active is not null && readout.EvictedFactCount > _lostFacts)
        {
            LastResult = new(active.Source, VideoRealizationFactKind.Failed, "Engine video completion observations were lost.");
            Stop();
        }
        _lostFacts = readout.EvictedFactCount;
    }

    public void Dispose() => Stop();
}
