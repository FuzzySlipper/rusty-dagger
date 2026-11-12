using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Quest policy overlays the current actor; definitions and base disposition remain owned by the roster and senses.</summary>
internal sealed record DaggerfallQuestFoeActorRelation(long ActorId, int? Team, bool? Infighting);
internal sealed record DaggerfallQuestFoeRelations(DaggerfallQuestFoeActorRelation[] Actors, bool Restrained, long[] ReleasedRestraint)
{
    internal DaggerfallQuestFoeRelations Copy() => this with { Actors = [.. Actors], ReleasedRestraint = [.. ReleasedRestraint] };
    internal static readonly DaggerfallQuestFoeRelations None = new([], false, []);
}

internal static class DaggerfallFoeTeams
{
    private static readonly string[] SourceNames = ["PlayerEnemy", "PlayerAlly", "Vermin", "Spriggans", "Bears", "Tigers", "Spiders", "Orcs", "Centaurs", "Werecreatures", "Nymphs", "Aquatic", "Harpies", "Undead", "Giants", "Scorpions", "Magic", "Daedra", "Dragonlings", "KnightsAndMages", "Criminals", "CityWatch"];
    private static readonly string[] Names = ["player-enemy", "player-ally", "vermin", "spriggans", "bears", "tigers", "spiders", "orcs", "centaurs", "werecreatures", "nymphs", "aquatic", "harpies", "undead", "giants", "scorpions", "magic", "daedra", "dragonlings", "knights-and-mages", "criminals", "city-watch"];
    internal static int Parse(string value)
    {
        if (int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number)) return number;
        int index = Array.FindIndex(SourceNames, name => name.Equals(value, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : throw new ArgumentException($"Quest foe team '{value}' is not a source MobileTeams name or nonnegative number.");
    }
    internal static string Name(int team) => team >= 0 && team < Names.Length ? Names[team] : "team." + team.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly System.Text.RegularExpressions.Regex FoeRelation = Header(@"^(?:change\s+foe\s+(?<foe>[a-zA-Z0-9_.-]+)\s+(?:(?<team>team)\s+(?<value>\w+)|(?<infighting>infighting)\s+(?<value>true|false))|(?<restrain>restrain|unrestrain)\s+foe\s+(?<foe>[a-zA-Z0-9_.-]+)|enemies\s+(?<enemies>makehostile|clear))$");
    private static DaggerfallQuestTaskOperation? CompileFoeRelations(string line, int sourceLine)
    {
        if (FoeRelation.Match(line) is not { Success: true } match) return null;
        var kind = match.Groups["team"].Success ? DaggerfallQuestTaskOperationKind.FoeTeam
            : match.Groups["infighting"].Success ? DaggerfallQuestTaskOperationKind.FoeInfighting
            : match.Groups["restrain"].Success ? DaggerfallQuestTaskOperationKind.FoeRestraint : DaggerfallQuestTaskOperationKind.Enemies;
        int value = kind switch
        {
            DaggerfallQuestTaskOperationKind.FoeTeam => DaggerfallFoeTeams.Parse(match.Groups["value"].Value),
            DaggerfallQuestTaskOperationKind.FoeInfighting => bool.Parse(match.Groups["value"].Value) ? 1 : 0,
            DaggerfallQuestTaskOperationKind.FoeRestraint => match.Groups["restrain"].Value.Equals("restrain", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            _ => match.Groups["enemies"].Value.Equals("clear", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
        };
        return new(kind, sourceLine, line, kind == DaggerfallQuestTaskOperationKind.Enemies ? [] : [Canonical(match.Groups["foe"].Value)], [], null, Step: value);
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private Action<bool>? _allEnemies;
    private Func<long, bool>? _relationActorActive;
    internal void BindEnemyRelations(Action<bool> allEnemies, Func<long, bool> actorActive)
    { _allEnemies = allEnemies; _relationActorActive = actorActive; }
    private DaggerfallQuestFoeRelations? FoeResourceRelations(long actorId) => _instances.Values
        .Where(instance => instance.Lifecycle == DaggerfallQuestLifecycle.Active)
        .SelectMany(instance => instance.Resources).LastOrDefault(resource => resource.SelectedFoe is not null && resource.Binding.ActorIds.Contains(actorId))?.FoeRelations;
    internal DaggerfallQuestFoeActorRelation? FoeRelationsFor(long actorId) => FoeResourceRelations(actorId)?.Actors.SingleOrDefault(value => value.ActorId == actorId);
    internal void ReleaseFoeRestraint(long actorId)
    {
        foreach (var instance in _instances.Values.Where(instance => instance.Lifecycle == DaggerfallQuestLifecycle.Active))
            instance.Resources = instance.Resources.Select(resource => resource.Binding.ActorIds.Contains(actorId)
                && resource.FoeRelations is { Restrained: true } relation && !relation.ReleasedRestraint.Contains(actorId)
                    ? resource with { FoeRelations = relation with { ReleasedRestraint = [.. relation.ReleasedRestraint, actorId] } } : resource).ToArray();
    }
    internal bool HasInfightingFoes => _instances.Values.Any(instance => instance.Lifecycle == DaggerfallQuestLifecycle.Active
        && instance.Resources.Any(resource => resource.SelectedFoe is not null && resource.FoeRelations?.Actors.Any(actor => actor.Infighting == true) == true));
    internal bool IsQuestFoe(long actorId) => _instances.Values.Any(instance => instance.Lifecycle == DaggerfallQuestLifecycle.Active
        && instance.Resources.Any(resource => resource.SelectedFoe is not null && resource.Binding.ActorIds.Contains(actorId)));
    internal bool IsFoeRestrained(long actorId) => FoeResourceRelations(actorId) is { Restrained: true } relation && !relation.ReleasedRestraint.Contains(actorId);
    internal bool AllowsFoeInfighting(long actorId) => FoeRelationsFor(actorId)?.Infighting ?? !IsQuestFoe(actorId);
    internal string? FoeTeam(long actorId, string? original) => FoeRelationsFor(actorId)?.Team is { } team ? DaggerfallFoeTeams.Name(team) : original;
    bool IDaggerfallQuestTaskLifecycle.FoeRelation(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        if (operation.Kind == DaggerfallQuestTaskOperationKind.Enemies)
        {
            (_allEnemies ?? throw new InvalidOperationException("No canonical enemy command owner is composed."))(operation.Step == 1);
            return true;
        }
        var resource = FoeResource(instance, operation);
        var relation = resource.FoeRelations ?? DaggerfallQuestFoeRelations.None;
        if (operation.Kind == DaggerfallQuestTaskOperationKind.FoeRestraint)
            relation = relation with { Restrained = operation.Step == 1, ReleasedRestraint = [] };
        else
        {
            var active = resource.Binding.ActorIds.Where(id => (_relationActorActive ?? throw new InvalidOperationException("No active foe owner is composed."))(id)).ToArray();
            if (active.Length == 0) return false;
            var actors = relation.Actors.ToDictionary(value => value.ActorId);
            foreach (long id in active)
            {
                var prior = actors.GetValueOrDefault(id) ?? new(id, null, null);
                actors[id] = operation.Kind == DaggerfallQuestTaskOperationKind.FoeTeam
                    ? prior with { Team = operation.Step } : prior with { Infighting = operation.Step == 1 };
            }
            relation = relation with { Actors = actors.Values.OrderBy(value => value.ActorId).ToArray() };
        }
        SetResource(instance.InstanceId, resource with { FoeRelations = relation });
        return true;
    }
}
