using Rusty.Engine;

namespace WorldRpg.Kit;

public readonly record struct RulesetId(string Value);
public readonly record struct GameBundleId(string Value);
public readonly record struct ContentPackId(string Value);
public readonly record struct TuningProfileId(string Value);

public sealed class GameSessionContext(IEngineContext engine, ProductContent content)
{
    public IEngineContext Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
    public ProductContent Content { get; } = content ?? throw new ArgumentNullException(nameof(content));
}

public interface IGameRuleset
{
    RulesetId Id { get; }
    IGameSession CreateSession(GameSessionContext context);
}

public interface IGameSession : IDisposable
{
    void PublishInitial();
    void Update(ProductUpdate update);
}
