using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

internal sealed class GameplayTimeFake : IGameplayTimeService
{
    internal List<double> Rates { get; } = [];
    private GameplayTimeReadout _readout = new(false, 1, 0, 60);
    public GameplayTimeReadout SelectRate(GameplayTimeRateRequest request)
    {
        Rates.Add(request.Rate);
        return _readout = new(true, request.Rate, 0, 60);
    }
    public GameplayTimeReadout Advance(GameplayTimeAdvanceRequest request) =>
        _readout = new(true, request.Rate, (uint)Math.Ceiling(request.Seconds * 60), 60);
    public GameplayTimeReadout Read() => _readout;
}
