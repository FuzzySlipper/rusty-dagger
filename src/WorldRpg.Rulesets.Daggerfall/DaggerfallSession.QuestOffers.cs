using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private bool QuestOfferCapability(DaggerfallQuestTaskOperation operation)
    {
        switch (operation.Kind)
        {
            case DaggerfallQuestTaskOperationKind.MakePcDiseased:
            case DaggerfallQuestTaskOperationKind.CurePcDisease:
                return _definitions.QuestSources.Tables.Diseases.Lookup.TryGetValue(operation.Targets[0], out int disease)
                    && disease >= 0 && disease < QuestDiseases.Length;
            case DaggerfallQuestTaskOperationKind.Climate:
            case DaggerfallQuestTaskOperationKind.Season:
            case DaggerfallQuestTaskOperationKind.Weather:
                _ = QuestEnvironmentCondition(operation, _time.Calendar);
                return true;
            case DaggerfallQuestTaskOperationKind.PlaySound:
                return _definitions.QuestSources.Tables.Sounds.Lookup.TryGetValue(operation.Targets[0], out int sound)
                    && _sites.Projection.Inputs.Audio.Any(clip => clip.SourceNumericId == sound);
            case DaggerfallQuestTaskOperationKind.PlaySong:
                return _music is not null && _musicBundle?.CanPlay(operation.Targets[0].ToLowerInvariant()) == true;
            case DaggerfallQuestTaskOperationKind.PlayVideo:
                return _composition.VideosEnabled && Cinematics is not null
                    && int.TryParse(operation.Targets[0], out int video) && video is >= 0 and <= 9999
                    && _definitions.Cinematics.Cinematics.TryGetValue($"ANIM{video:0000}.VID", out var cinematic) && cinematic.Artifact is not null;
            default: return true;
        }
    }

    private (DaggerfallNpc Provider, int Faction, bool Member, int Reputation, int Rank)? QuestProvider(long id)
    {
        DaggerfallNpc npc;
        try { npc = State.Npcs.Require(id); } catch (InvalidOperationException) { return null; }
        bool castle = _sites.Projection.Inputs.ProfileKind == DaggerfallWorldProfileKind.Dungeon
            && State.PlayerControl.Position is { } position && _sites.Projection.Inputs.AmbientZones.Any(zone => zone.Kind == DaggerfallAmbientZoneKind.Castle && zone.Contains(position.ToVector()));
        if (DaggerfallNpcServiceFacts.IsChild(npc.Appearance)) return null;
        string? service = npc.Services.Contains("quests", StringComparer.Ordinal) ? "quests"
            : npc.Services.Contains("quest", StringComparer.Ordinal) ? "quest"
            : npc.Services.Contains("quest-candidate", StringComparer.Ordinal) ? "quest-candidate"
            : castle && npc.Kind == DaggerfallNpcKind.Static && npc.Services.Contains("talk", StringComparer.Ordinal) ? "talk" : null;
        if (!State.Npcs.IsGameplayActive(id) || service is null
            || State.Services.ProviderAvailable(new(id, npc.Site, service)) != DaggerfallServiceDenial.None || State.Quests.IsNpcMuted(id)) return null;
        int faction = npc.Appearance.FactionId;
        DaggerfallConcreteGuildDefinition? guild = null;
        if (service == "quests" && CurrentInteriorBuilding() is { BuildingType: 11 or 14 } building)
        {
            if (!TryResolveGuildProvider(npc, building, DaggerfallConcreteGuildService.Quests, out guild, out _, out _)) return null;
            faction = guild!.Kind is DaggerfallConcreteGuildKind.Temple or DaggerfallConcreteGuildKind.KnightlyOrder ? building.FactionId : guild.FactionId;
        }
        if (service == "quests" && guild is null) return null;
        if (!_definitions.Factions.Factions.TryGetValue(faction, out var definition)) return null;
        int membershipFaction = guild?.FactionId ?? faction;
        bool isGuild = guild is not null || DaggerfallConcreteGuildCatalog.All.Any(owner => owner.FactionId == faction);
        bool member = isGuild && State.Social.GuildEligibility(membershipFaction).IsMember;
        int rank = member ? State.Social.GuildEligibility(membershipFaction).Rank : 0;
        if (guild?.Kind is DaggerfallConcreteGuildKind.Mages or DaggerfallConcreteGuildKind.KnightlyOrder)
            rank = Math.Max(rank, State.Progression.Level);
        if (State.Quests.ProviderHasActiveWork(id)) return null;
        if (!isGuild && definition.Type != 8 && (castle || service == "quest-candidate" || service == "quest")
            && !State.Quests.WorkContactAvailable(id, _site.ActiveSite!.Id, castle,
                service == "quest" ? 100 : castle ? _tuning.QuestOffers.CastleProviderChancePercent : _tuning.QuestOffers.SocialProviderChancePercent)) return null;
        return (npc, faction, member, State.Social.FactionReputation(faction), rank);
    }

    internal string OfferQuestWork(long provider)
    {
        if (QuestProvider(provider) is not { } context) return "That provider has no work available.";
        try
        {
            State.Quests.ConsumeWorkContact(provider);
            State.Quests.PrepareWorkOffer(provider, context.Provider.Site, context.Faction,
                context.Member, State.Progression.Level, context.Reputation, context.Rank,
                context.Provider.Appearance.Gender == "Female" ? DaggerfallCharacterGender.Female : DaggerfallCharacterGender.Male,
                checked((int)_time.Calendar.DayNumber));
            return State.Quests.ReadOffer()!.Text;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        { return "Quest offer unavailable: " + error.Message; }
    }

    internal void AnswerQuestOffer(string identity, bool accept)
    {
        var offer = State.Quests.PendingOffer;
        bool available = offer?.Quest.QuestorId is long giver && QuestProvider(giver) is { } context
            && context.Faction == offer.Quest.FactionId && context.Provider.Site == offer.Site;
        Presentation.SetOutcome(State.Quests.AnswerOffer(identity, accept, available));
        _dialogue?.RefreshEligibility();
    }

    private void StartDueCriminalInvitations()
    {
        bool inside = _sites.ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior;
        long minute = MinuteIndex(_time.Calendar);
        foreach (var (requirement, source, faction) in new[] {
            (DaggerfallCrimeGuildRequirement.Thieving, "O0A0AL00", DaggerfallConcreteGuildCatalog.ThievesFactionId),
            (DaggerfallCrimeGuildRequirement.Murder, "L0A01L00", DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId) })
        {
            if (!State.Crime.IsInvitationDue(requirement, minute, inside)) continue;
            try
            {
                string id = "crime-invitation:" + source;
                if (!State.Quests.TryGet(id, out _))
                    State.Quests.Start(new(id, source + ".txt", source, DaggerfallQuestLifecycle.Active, null, [], []) { FactionId = faction });
                State.Crime.MarkInvitationStarted(requirement, minute, inside);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            { Presentation.SetOutcome("Guild invitation unavailable: " + error.Message); }
        }
    }
}
