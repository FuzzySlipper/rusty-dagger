using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallInfectionConsumption ConsumeVampireInfection(DaggerfallInfectionTransition transition)
    {
        int clan;
        try { clan = DaggerfallVampirismPolicy.GetVampireClan(_definitions.Factions, transition.InfectionRegion).Id; }
        catch (NotSupportedException error) { return new(false, "Vampire clan is unavailable: " + error.Message); }
        // Select only real, admitted cemetery closures in the player's current region. The infection
        // region remains the clan input even if the player travelled while incubating the disease.
        int region = _site.ActiveSite?.Region ?? transition.InfectionRegion;
        var candidates = _sites.Profiles?.Keys.Where(key => key.Kind == DaggerfallWorldProfileKind.Dungeon
            && key.Site.Region == region && _site.Require(key.Site).DungeonType == 18)
            .OrderBy(key => key.LogicalId, StringComparer.Ordinal).ToArray() ?? [];
        if (candidates.Length == 0) return new(false, $"No cemetery destination is published for region {region}.");
        int index = checked((int)_random.DrawKeyed(new KeyedRngRequest(0,
            "daggerfall.vampirism", transition.Instance + ":cemetery", 0, candidates.Length - 1)).Value);
        var destination = _sites.Profiles!.Require(candidates[index]);
        var position = destination.Project.PlayerPosition ?? throw new InvalidOperationException("Cemetery has no source arrival.");
        if (!_sites.TryRelocatePlayer(destination.ProfileKey, new("vampire-awakening", position,
            destination.InitialLook.YawRadians, destination.InitialLook.PitchRadians)))
            return new(false, "The cemetery destination could not be admitted.");
        _sites.ClearReturnDestination();
        foreach (long id in DefinitionsByActor.Where(pair => pair.Key != DaggerfallActorIdentity.PlayerEntityId
            && pair.Value.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass).Select(pair => pair.Key).ToArray()) _roster.Banish(id);
        // Synthetic time uses the existing calendar fan-out without an encounter request, and like the
        // donor's raised clock it charges no per-minute fatigue loss.
        AdvanceQuestTime(14L * DaggerfallCalendar.SecondsPerDay + (DaggerfallCalendar.DuskHour + 1 - _time.Calendar.Hour) * 3600L,
            idleFatigue: false);
        if (!State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, $"vampirism:{transition.Instance}",
            MinuteIndex(_time.Calendar), vampireClan: clan)) return new(false, "A racial override is already active.");
        HealRacialTransformation();
        var text = _definitions.TextPresentation.Resolve(new(DaggerfallTextKind.Resource, "401"), DungeonTextContext());
        Presentation.SetOutcome(text.Text);
        return new(true, null);
    }

    private void AdvanceVampireQuestOpportunities(DaggerfallCalendar before, DaggerfallCalendar after)
    {
        if (State.RacialOverrides.Current is not { IsVampire: true } racial) return;
        long first = Math.Max(MinuteIndex(before), racial.State.AcquiredMinute), last = MinuteIndex(after);
        // Classic's clan opportunity is nested inside the weekly and 38-day conditions (266 days).
        // The separate cure opportunity is every 84 days. Both consume this one admitted calendar.
        foreach (var period in new[] { (Minutes: 266L * 1440, Cure: false), (Minutes: 84L * 1440, Cure: true) })
            for (long boundary = ((first + period.Minutes - 1) / period.Minutes) * period.Minutes; boundary < last; boundary += period.Minutes)
                StartVampireQuestOpportunity(boundary, period.Cure);
    }

    internal bool StartVampireQuestOpportunity(long minute, bool cure)
    {
        if (State.RacialOverrides.Current is not { State.Vampire: { } vampire } racial) return false;
        string draw = $"{racial.Source}:{minute}:{cure}";
        int roll = checked((int)_random.DrawKeyed(new(0, "daggerfall.vampirism", draw + ":quest", cure ? 10 : 1, 100)).Value);
        if (roll >= (cure ? 30 : 50)) return false;
        string? quest = cure ? "$CUREVAM" : vampire.InitialQuestStarted ? null : "P0A01L00";
        int faction = 0;
        if (quest is null)
        {
            var pool = State.Quests.OrdinaryWorkPool(vampire.Clan, true, State.Progression.Level,
                State.Social.FactionReputation(vampire.Clan), State.Progression.Level, State.Character.Identity.Gender);
            if (pool.Length == 0) return false;
            quest = pool[checked((int)_random.DrawKeyed(new(0, "daggerfall.vampirism", draw + ":selection", 0, pool.Length - 1)).Value)].Name;
            faction = vampire.Clan;
        }
        string identity = $"vampire-quest:{racial.Source}:{minute}:{cure}";
        if (State.Quests.All.Any(value => value.InstanceId == identity)) return false;
        try
        {
            var source = _definitions.QuestSources.Resolve(quest + ".txt");
            State.Quests.Start(new(identity, source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], []) { FactionId = faction });
            if (!cure && !vampire.InitialQuestStarted) State.RacialOverrides.MarkInitialVampireQuestStarted();
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            Presentation.SetOutcome(cure ? "The cure for vampirism cannot be offered right now." : "Your vampire clan's quest cannot be offered right now.");
            return false;
        }
    }

    internal bool CureVampirism(bool fromQuest = false)
    {
        if (State.RacialOverrides.Current is not { IsVampire: true } racial) return false;
        State.RacialOverrides.Remove(racial.Source);

        if (fromQuest) AdvanceQuestTime(60); else AdvanceElapsedTime(60);
        Presentation.SetOutcome("Your vampirism is cured.");
        return true;
    }

    private void RacialOverrideRemoved(DaggerfallRacialOverrideView previous)
    {
        if (!previous.IsVampire) return;
        foreach (var quest in State.Quests.Capture().Instances.Where(quest => quest.Lifecycle == DaggerfallQuestLifecycle.Active
            && quest.DefinitionName.StartsWith("P0", StringComparison.OrdinalIgnoreCase)))
            State.Quests.Fail(quest.InstanceId, "Vampirism ended");
    }

    private bool VampireNeedsToFeed => State.RacialOverrides.Current?.State.Vampire is { } vampire
        && MinuteIndex(_time.Calendar) - vampire.LastFedMinute > _tuning.Vampirism.SatiationMinutes;
}
