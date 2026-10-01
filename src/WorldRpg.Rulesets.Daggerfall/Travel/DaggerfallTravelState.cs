using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

internal enum DaggerfallTravelOutcome { Arrived, Encounter, Defeated, Relocated, Unavailable, SaveBoundary, Stopped }

/// <summary>The meaningful result of a paid journey, alongside the ordinary currency, calendar and site save.</summary>
internal sealed record DaggerfallTravelResult([property: JsonRequired] DaggerfallSiteId Origin, [property: JsonRequired] DaggerfallSiteId Destination,
    [property: JsonRequired] DaggerfallSiteId ActualSite, [property: JsonRequired] DaggerfallTravelMapPixel OriginPixel,
    [property: JsonRequired] DaggerfallTravelMapPixel ActualPixel, [property: JsonRequired] int PaidGold, [property: JsonRequired] long StartedSeconds, [property: JsonRequired] long EndedSeconds,
    [property: JsonRequired] long QuotedSeconds, [property: JsonRequired] DaggerfallTravelOutcome Outcome, [property: JsonRequired] string Message)
{
    internal long ElapsedSeconds => EndedSeconds - StartedSeconds;
    internal void Validate()
    {
        OriginPixel.RequireInBounds(nameof(OriginPixel)); ActualPixel.RequireInBounds(nameof(ActualPixel));
        if (Origin.Region < 0 || Origin.Index < 0 || Destination.Region < 0 || Destination.Index < 0
            || ActualSite.Region < 0 || ActualSite.Index < 0 || PaidGold < 0 || StartedSeconds < 0
            || EndedSeconds < StartedSeconds || QuotedSeconds <= 0 || !Enum.IsDefined(Outcome)
            || string.IsNullOrWhiteSpace(Message)) throw new ArgumentException("Travel result contains invalid current journey state.");
        if (Outcome == DaggerfallTravelOutcome.Arrived && (ActualSite != Destination || ElapsedSeconds < QuotedSeconds))
            throw new ArgumentException("An arrived journey must reach its destination after its quoted duration.");
    }
}

internal sealed record DaggerfallTravelSave([property: JsonRequired] DaggerfallTravelResult? LastResult)
{
    internal static DaggerfallTravelSave Empty { get; } = new((DaggerfallTravelResult?)null);
    internal void Validate(DaggerfallLocationSet? locations = null)
    {
        LastResult?.Validate();
        if (locations is null || LastResult is not { } result) return;
        foreach (DaggerfallSiteId site in new[] { result.Origin, result.Destination, result.ActualSite })
            if (!locations.Keys.Contains((site.Region, site.Index)))
                throw new ArgumentException($"Travel result names missing site {site}.");
    }
}

/// <summary>A paid attempt is terminated at a save boundary; restoring never repeats its payment or automatically travels.</summary>
internal sealed class DaggerfallTravelState
{
    private DaggerfallTravelResult? _active;
    internal DaggerfallTravelResult? LastResult { get; private set; }
    internal DaggerfallTravelState(DaggerfallTravelSave? saved = null)
    {
        saved?.Validate(); LastResult = saved?.LastResult;
    }
    internal void Begin(DaggerfallSiteId origin, DaggerfallTravelQuote quote, long now)
    {
        if (_active is not null) throw new InvalidOperationException("A journey is already being executed.");
        _active = new(origin, quote.Destination.Id, origin, quote.Origin, quote.Origin, quote.TotalCost, now, now, quote.TravelSeconds,
            DaggerfallTravelOutcome.Stopped, "Journey is executing.");
    }
    internal DaggerfallTravelResult Complete(long now, DaggerfallSiteId actual, DaggerfallTravelMapPixel pixel, DaggerfallTravelOutcome outcome, string message)
    {
        DaggerfallTravelResult active = _active ?? throw new InvalidOperationException("There is no paid journey to complete.");
        LastResult = active with { EndedSeconds = now, ActualSite = actual, ActualPixel = pixel, Outcome = outcome, Message = message };
        LastResult.Validate(); _active = null; return LastResult;
    }
    internal DaggerfallTravelSave Capture(long now, DaggerfallSiteId? actual, Func<DaggerfallTravelMapPixel> currentPixel) => new(_active is null ? LastResult
        : _active with { EndedSeconds = now, ActualSite = actual ?? throw new InvalidOperationException("A paid journey must retain its actual site."), ActualPixel = currentPixel(), Outcome = DaggerfallTravelOutcome.SaveBoundary,
            Message = "Journey stopped at this saved location; its payment and elapsed time are retained." });
}
