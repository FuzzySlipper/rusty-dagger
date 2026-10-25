using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Slow world changes the player asks for in play (a journey, an entrance into a building or a dungeon) are
/// acknowledged on the host's loading screen in the update that accepts them and run on the updates after, one
/// phase each. A caller with no update to acknowledge in (a direct API, a developer command), or a host that
/// shows no loading screen, gets the same work at once.
/// </summary>
internal sealed partial class DaggerfallSession : ILoadingScreenSession
{
    private readonly LoadingScreen? _loading;
    private bool _inAdmittedUpdate;

    /// <summary>
    /// Publishes the loading screen's change, which the snapshot carries. Inside this session's own update the
    /// update's own publication carries it; between updates (while the host runs staged work) this publishes.
    /// </summary>
    public void PublishLoading()
    {
        if (!_inAdmittedUpdate && !_disposed) PublishPresentation();
    }

    /// <summary>Whether slow work accepted now is staged on the loading screen rather than run at once.</summary>
    private bool StagesSlowWork => _loading is { IsLoading: false } && _inAdmittedUpdate;

    /// <summary>
    /// Stages slow work on the loading screen. Held movement is dropped as the screen goes up: the keys that
    /// were down belong to the place being left. A phase that throws reports why; the player stays where they were.
    /// </summary>
    /// <remarks>
    /// Each phase hands the terrain collision it admits to the Engine to bake off the update, and holds the screen,
    /// showing the bake's progress, until that collision is live; an arrival is stood on the ground only then, and
    /// the next phase runs only then, so its own synchronous collision changes never wait on the bake. A refused
    /// bake fails the work as a throwing phase does.
    /// </remarks>
    private void StageWorldChange(string title, IReadOnlyList<LoadingPhase> phases, Func<Exception, string> failure)
    {
        _input.Neutralize(); _locomotion.Neutralize(); NeutralizeGuard();
        _ = _loading!.Start(title,
            [.. phases.Select(phase => new LoadingPhase(phase.Label, () => _sites.DeferCollision(phase.Run), _sites.AwaitArrivalCollision))],
            // The arrival has happened by the time its ground is refused, so the work's own failure wording does not fit.
            error => Presentation.SetOutcome(error is DaggerfallArrivalCollisionRefusedException ? error.Message : failure(error)));
    }

    /// <summary>
    /// Accepts the live travel quote. The destination's exterior is read on the first update after the screen
    /// goes up, the journey's days pass on the next, and the player arrives on the one after.
    /// </summary>
    private void AcceptTravel(string expectedQuote, ulong expectedCost)
    {
        if (!StagesSlowWork || CurrentTravelQuote() is not { } quote)
        {
            _ = ExecuteTravel(expectedQuote, expectedCost);
            return;
        }
        string place = quote.Destination.Name;
        DaggerfallJourney? journey = null;
        StageWorldChange($"Travelling to {place}", [
            new($"Reading {place}", () => _ = TravelRefusal(quote, out _)),
            new($"On the road to {place}", () => journey = Depart(expectedQuote, expectedCost)),
            new($"Arriving at {place}", () => { if (journey is { } road) _ = Arrive(road); }),
        ], error => $"The journey to {place} could not be made: {error.Message}");
    }

    /// <summary>
    /// Passes through a portal (a dungeon's entrance or its way out): the destination is read, then entered.
    /// True when the portal was taken or, staged, accepted; the staged entrance reports its own failure.
    /// </summary>
    private bool EnterPortal(DaggerfallWorldProfileKey destination, DaggerfallSitePortal portal)
    {
        if (!StagesSlowWork) return TryTransitionTo(destination, portal.ArrivalAnchor);
        string place = SiteChangePlace(destination);
        StageWorldChange($"Entering {place}", [
            new($"Reading {place}", () => _ = _sites.RequireProfiles().Require(destination)),
            new($"Entering {place}", () =>
            {
                if (!TryTransitionTo(destination, portal.ArrivalAnchor)) Presentation.SetOutcome("The entrance cannot be used.");
            }),
        ], error => error.Message);
        return true;
    }

    /// <summary>The name a loading screen gives a destination: its location, or its kind when it has none.</summary>
    private string SiteChangePlace(DaggerfallWorldProfileKey destination) =>
        _site.TryFind(destination.Site, out DaggerfallSiteRecord record) && record.Name is { Length: > 0 } name
            ? name
            : destination.Kind == DaggerfallWorldProfileKind.Dungeon ? "the dungeon" : "the location";
}
