using WorldRpg.Rulesets.Daggerfall.Content;
using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Crime;

internal enum DaggerfallLegalPhase { Pursuit, Arrest, Resisting, Escaped, Court, Sentence, Prison }
internal sealed record DaggerfallCrimeDisposition(DaggerfallCourtOutcome Outcome, long ResolvedMinute);
internal sealed record DaggerfallLegalResponseSave(
    string Id, int Region, string[] Charges, DaggerfallLegalPhase Phase, DaggerfallWorldProfileKeySave Profile,
    double ResponseDelaySeconds, long[] Guards, DaggerfallCourtPenalty? Penalty = null,
    DaggerfallCourtSentence? Sentence = null, long PrisonSecondsRemaining = 0)
{
    public long ChoiceSequence { get; init; }
    internal string Revision => $"{Id}:{ChoiceSequence}:{Phase}:{Charges.Length}";
    internal bool Modal => Phase is DaggerfallLegalPhase.Arrest or DaggerfallLegalPhase.Court or DaggerfallLegalPhase.Sentence or DaggerfallLegalPhase.Prison;
    internal DaggerfallLegalResponseSave Validate(IReadOnlyDictionary<string, DaggerfallCrimeIncidentSave> incidents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentNullException.ThrowIfNull(Charges); ArgumentNullException.ThrowIfNull(Guards);
        ArgumentNullException.ThrowIfNull(Profile); Profile.Validate();
        if (Region < 0 || ChoiceSequence < 0 || !Enum.IsDefined(Phase) || Charges.Length == 0 || Charges.Distinct(StringComparer.Ordinal).Count() != Charges.Length
            || Charges.Any(id => !incidents.TryGetValue(id, out var charge) || charge.Region != Region || charge.Disposition is not null)
            || Guards.Any(id => id <= 0) || Guards.Distinct().Count() != Guards.Length
            || !double.IsFinite(ResponseDelaySeconds) || ResponseDelaySeconds < 0 || PrisonSecondsRemaining < 0
            || Penalty is { } p && (p.Fine < 0 || p.PrisonDays < 0)
            || Phase is DaggerfallLegalPhase.Court or DaggerfallLegalPhase.Sentence or DaggerfallLegalPhase.Prison && Penalty is null
            || Phase is DaggerfallLegalPhase.Sentence or DaggerfallLegalPhase.Prison && Sentence is null)
            throw new ArgumentException("Legal response has invalid charges, guards, phase or sentence.");
        Sentence?.Validate();
        return this;
    }
}

/// <summary>Current regional charges and their response live beside the incident that created them.</summary>
internal sealed partial class DaggerfallCrimeState
{
    private readonly Dictionary<int, DaggerfallLegalResponseSave> _responses = [];
    private readonly HashSet<int> _banishedRegions = [];
    internal IReadOnlyCollection<DaggerfallLegalResponseSave> LegalResponses => _responses.Values;
    internal IReadOnlySet<int> BanishedRegions => _banishedRegions;
    internal DaggerfallLegalResponseSave? Response(int region) => _responses.GetValueOrDefault(region);
    internal IReadOnlyList<DaggerfallCrimeIncidentSave> PendingCharges(int region) => Incidents.Where(incident =>
        incident.Region == region && incident.Disposition is null
        && (incident.Reported || incident.Witnesses.Query == DaggerfallCrimeWitnessQuery.CompletedWithWitnesses)).ToArray();

    internal void SetResponse(DaggerfallLegalResponseSave response)
    {
        response.Validate(_incidents);
        if (_responses.TryGetValue(response.Region, out var previous))
        {
            bool changedChoice = previous.Phase != response.Phase || previous.PrisonSecondsRemaining != response.PrisonSecondsRemaining
                || !previous.Charges.SequenceEqual(response.Charges);
            response = response with { ChoiceSequence = checked(previous.ChoiceSequence + (changedChoice ? 1 : 0)) };
        }
        _responses[response.Region] = response;
    }
    internal void ResolveResponse(int region, DaggerfallCourtOutcome outcome, long minute)
    {
        if (!_responses.Remove(region, out var response)) throw new InvalidOperationException("There is no current regional case to resolve.");
        foreach (string id in response.Charges)
            _incidents[id] = _incidents[id] with { Disposition = new(outcome, minute) };
        if (outcome == DaggerfallCourtOutcome.Banished) _banishedRegions.Add(region);
    }
    private void RestoreLegal(DaggerfallCrimeSave saved)
    {
        foreach (var response in saved.LegalResponses) _responses.Add(response.Region, response);
        foreach (int region in saved.BanishedRegions) _banishedRegions.Add(region);
    }
}
