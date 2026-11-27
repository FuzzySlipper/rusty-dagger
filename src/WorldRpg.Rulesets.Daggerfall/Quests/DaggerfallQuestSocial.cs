using System.Globalization;
using System.Text.RegularExpressions;
using WorldRpg.Rulesets.Daggerfall.Crime;

namespace WorldRpg.Rulesets.Daggerfall;

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly Regex ChangeRepute = Header(@"^change\s+repute\s+with\s+(?<person>[a-zA-Z0-9_.-]+)\s+by\s+(?<amount>[+-]\d+)$");
    private static readonly Regex LegalRepute = Header(@"^legal\s+repute\s+(?<amount>[+-]?\d+)$");
    private static readonly Regex ReputeExceeds = Header(@"^repute\s+with\s+(?<person>[a-zA-Z0-9_.-]+)\s+exceeds\s+(?<amount>\d+)\s+do\s+(?<task>[a-zA-Z0-9_.-]+)$");
    private static readonly Regex WhenRepute = Header(@"^when\s+repute\s+with\s+(?<person>[a-zA-Z0-9_.-]+)\s+is\s+at\s+least\s+(?<amount>\d+)$");
    private static readonly Regex SetCrime = Header(@"^setplayercrime\s+(?<crime>[a-zA-Z_]+)$");
    private static DaggerfallQuestTaskOperation? CompileSocial(string line, int sourceLine)
    {
        foreach (var (pattern, kind) in new[] { (ChangeRepute, DaggerfallQuestTaskOperationKind.ChangeRepute),
            (LegalRepute, DaggerfallQuestTaskOperationKind.LegalRepute), (ReputeExceeds, DaggerfallQuestTaskOperationKind.ReputeExceeds),
            (WhenRepute, DaggerfallQuestTaskOperationKind.WhenRepute), (SetCrime, DaggerfallQuestTaskOperationKind.SetCrime) })
        {
            var match = pattern.Match(line);
            if (!match.Success) continue;
            var targets = new List<string>();
            if (match.Groups["person"].Success) targets.Add(Canonical(match.Groups["person"].Value));
            if (match.Groups["task"].Success) targets.Add(Canonical(match.Groups["task"].Value));
            if (match.Groups["crime"].Success) targets.Add(match.Groups["crime"].Value);
            return new(kind, sourceLine, line, targets.ToArray(), [], null,
                Step: match.Groups["amount"].Success ? int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture) : null);
        }
        return null;
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private DaggerfallSocialState? _social;
    private DaggerfallCrimeState? _crime;
    private Func<int?>? _socialRegion;
    private Action<int>? _admitInvitationGuild;
    private Action<long, int>? _adjustQuestorDisposition;
    private Action<int, bool, long?>? _factionQuestSettled;
    internal void BindSocial(DaggerfallSocialState social, DaggerfallCrimeState crime, Func<int?> region)
    { _social = social; _crime = crime; _socialRegion = region; }
    internal void BindGuildInvitationAdmission(Action<int> admit) => _admitInvitationGuild = admit;
    internal void BindQuestorDisposition(Action<long, int> adjust) => _adjustQuestorDisposition = adjust;
    /// <summary>Who hears that a quest given for a faction settled: its faction, whether it succeeded and its questor.</summary>
    internal void BindFactionQuestSettled(Action<int, bool, long?> settled) => _factionQuestSettled = settled;

    bool IDaggerfallQuestTaskLifecycle.SocialAction(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        var social = _social ?? throw new NotSupportedException("No canonical quest social owner is composed.");
        switch (operation.Kind)
        {
            case DaggerfallQuestTaskOperationKind.ChangeRepute:
                social.ChangeFactionReputation(QuestFaction(instance, operation.Targets[0], false), operation.Step!.Value, DaggerfallFactionReputationChange.Propagate);
                return true;
            case DaggerfallQuestTaskOperationKind.LegalRepute:
                if (_socialRegion?.Invoke() is not int region) throw new NotSupportedException("Quest legal reputation needs the current region.");
                social.ChangeRegionalReputation(region, operation.Step!.Value);
                return true;
            case DaggerfallQuestTaskOperationKind.ReputeExceeds:
            case DaggerfallQuestTaskOperationKind.WhenRepute:
                return social.FactionReputation(QuestFaction(instance, operation.Targets[0], operation.Kind == DaggerfallQuestTaskOperationKind.WhenRepute)) >= operation.Step!.Value;
            case DaggerfallQuestTaskOperationKind.SetCrime:
                string name = operation.Targets[0].Replace("_", "", StringComparison.Ordinal);
                DaggerfallCrimeKind? crime = name.Equals("None", StringComparison.OrdinalIgnoreCase) ? null
                    : Enum.TryParse<DaggerfallCrimeKind>(name, true, out var parsed) && Enum.IsDefined(parsed) ? parsed
                    : throw new NotSupportedException($"Quest crime '{operation.Targets[0]}' is not a known crime.");
                (_crime ?? throw new NotSupportedException("No canonical quest crime owner is composed.")).SetScriptedCrime(crime);
                return true;
            default: throw new ArgumentException("This operation is not a quest social action.");
        }
    }

    private int QuestFaction(DaggerfallQuestRuntimeInstance instance, string symbol, bool individual)
    {
        int faction;
        if (individual)
        {
            try { faction = _definitions.QuestSources.Tables.ActorItemTables.Factions.Resolve(symbol).P3; }
            catch (KeyNotFoundException error) { throw new NotSupportedException($"Quest individual '{symbol}' is unavailable: {error.Message}"); }
        }
        else faction = instance.Resources.SingleOrDefault(resource => resource.Symbol == symbol)?.SelectedPerson?.FactionId
            ?? throw new NotSupportedException($"Quest reputation requires selected Person '{symbol}'.");
        if (!_definitions.Factions.Factions.TryGetValue(faction, out var definition) || individual && definition.Type != 4)
            throw new NotSupportedException($"Quest reputation target '{symbol}' has no {(individual ? "individual " : "")}faction definition.");
        return faction;
    }

    private void SettleFaction(DaggerfallQuestRuntimeInstance instance)
    {
        if (instance.FactionSettled) return;
        if (instance.FactionId != 0)
        {
            (_social ?? throw new InvalidOperationException("Quest retirement requires the canonical social owner."))
                .ChangeFactionReputation(instance.FactionId, instance.Succeeded == true ? 5 : -2, DaggerfallFactionReputationChange.Propagate);
            _factionQuestSettled?.Invoke(instance.FactionId, instance.Succeeded == true, instance.QuestorId);
        }
        if (instance.QuestorId is long questorId && questorId > 0)
            _adjustQuestorDisposition?.Invoke(questorId,
                instance.Succeeded == true
                    ? DaggerfallIntroductions.QuestSuccessDispositionBonus
                    : DaggerfallIntroductions.QuestFailureDispositionPenalty);
        if (instance.Succeeded == true && TryResolveInvitationGuildFaction(instance, out int invitedGuild))
            _admitInvitationGuild?.Invoke(invitedGuild);
        instance.FactionSettled = true;
    }

    private static bool TryResolveInvitationGuildFaction(DaggerfallQuestRuntimeInstance instance, out int factionId)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(instance.SourceFile, "O0A0AL00.txt")
            || StringComparer.Ordinal.Equals(instance.InstanceId, "crime-invitation:O0A0AL00"))
        {
            factionId = Guilds.DaggerfallConcreteGuildCatalog.ThievesFactionId;
            return true;
        }
        if (StringComparer.OrdinalIgnoreCase.Equals(instance.SourceFile, "L0A01L00.txt")
            || StringComparer.Ordinal.Equals(instance.InstanceId, "crime-invitation:L0A01L00"))
        {
            factionId = Guilds.DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId;
            return true;
        }
        if (instance.InstanceId.StartsWith("crime-invitation:", StringComparison.Ordinal) && instance.FactionId != 0)
        {
            factionId = instance.FactionId;
            return true;
        }
        factionId = 0;
        return false;
    }
}
