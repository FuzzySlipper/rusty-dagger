using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestOfferTuning(int SocialProviderChancePercent, int CastleProviderChancePercent)
{
    internal DaggerfallQuestOfferTuning Validate() => SocialProviderChancePercent is >= 0 and <= 100 && CastleProviderChancePercent is >= 0 and <= 100
        ? this : throw new ArgumentException("Quest provider chances must be percentages.");
}

/// <summary>
/// Who offers quest work, and the offers: whether a person can give work now (their service, the guild hall they keep,
/// a castle's talkers and the social contacts), offering it, answering the offer, the dialogue's "has work" answer, and
/// the criminal guilds' invitations once they are due.
/// </summary>
/// <remarks>The interior building, the active profile's kind and whether the player stands in a castle zone change with
/// the site, so they are read as they are asked. Which guild of a hall gives a person's quests is Society's answer
/// (<c>DaggerfallGuildProviders.TryResolve</c> for the quests service), handed in.</remarks>
internal sealed class DaggerfallQuestWork(
    DaggerfallQuestInstances quests,
    DaggerfallNpcRegistry npcs,
    DaggerfallServiceTransactions services,
    DaggerfallSocialState social,
    DaggerfallCrimeState crime,
    Func<DaggerfallNpc, DaggerfallInteriorBuilding, DaggerfallConcreteGuildDefinition?> questGuild,
    DaggerfallDefinitions definitions,
    ProgressionState progression,
    DaggerfallWorldTime time,
    DaggerfallSiteContext site,
    DaggerfallQuestOfferTuning tuning,
    Func<DaggerfallInteriorBuilding?> interiorBuilding,
    Func<DaggerfallWorldProfileKind> profileKind,
    Func<bool> insideCastle)
{
    private (DaggerfallNpc Provider, int Faction, bool Member, int Reputation, int Rank)? Provider(long id)
    {
        DaggerfallNpc npc;
        try { npc = npcs.Require(id); } catch (InvalidOperationException) { return null; }
        bool castle = insideCastle();
        if (DaggerfallNpcServiceFacts.IsChild(npc.Appearance)) return null;
        string? service = npc.Services.Contains("quests", StringComparer.Ordinal) ? "quests"
            : npc.Services.Contains("quest", StringComparer.Ordinal) ? "quest"
            : npc.Services.Contains("quest-candidate", StringComparer.Ordinal) ? "quest-candidate"
            : castle && npc.Kind == DaggerfallNpcKind.Static && npc.Services.Contains("talk", StringComparer.Ordinal) ? "talk" : null;
        if (!npcs.IsGameplayActive(id) || service is null
            || services.ProviderAvailable(new(id, npc.Site, service)) != DaggerfallServiceDenial.None || quests.IsNpcMuted(id)) return null;
        int faction = npc.Appearance.FactionId;
        DaggerfallConcreteGuildDefinition? guild = null;
        if (service == "quests" && interiorBuilding() is { BuildingType: 11 or 14 or 18 } building)
        {
            if (questGuild(npc, building) is not { } served) return null;
            guild = served;
            faction = guild.Kind is DaggerfallConcreteGuildKind.Temple or DaggerfallConcreteGuildKind.KnightlyOrder ? building.FactionId : guild.FactionId;
        }
        if (service == "quests" && guild is null) return null;
        if (!definitions.Factions.Factions.TryGetValue(faction, out var definition)) return null;
        int membershipFaction = guild?.FactionId ?? faction;
        bool isGuild = guild is not null || DaggerfallConcreteGuildCatalog.All.Any(owner => owner.FactionId == faction);
        bool member = isGuild && social.GuildEligibility(membershipFaction).IsMember;
        int rank = member ? social.GuildEligibility(membershipFaction).Rank : 0;
        if (guild?.Kind is DaggerfallConcreteGuildKind.Mages or DaggerfallConcreteGuildKind.KnightlyOrder)
            rank = Math.Max(rank, progression.Level);
        if (quests.ProviderHasActiveWork(id)) return null;
        if (!isGuild && definition.Type != 8 && (castle || service == "quest-candidate" || service == "quest")
            && !quests.WorkContactAvailable(id, site.ActiveSite!.Id, castle,
                service == "quest" ? 100 : castle ? tuning.CastleProviderChancePercent : tuning.SocialProviderChancePercent)) return null;
        return (npc, faction, member, social.FactionReputation(faction), rank);
    }

    private static DaggerfallCharacterGender Gender(DaggerfallNpc npc) =>
        npc.Appearance.Gender == "Female" ? DaggerfallCharacterGender.Female : DaggerfallCharacterGender.Male;

    /// <summary>Whether this person has ordinary work to offer the dialogue now.</summary>
    internal bool HasWork(long npcId)
    {
        if (Provider(npcId) is not { } context) return false;
        return quests.HasOrdinaryWorkOffer(context.Faction, context.Member, progression.Level, context.Reputation, context.Rank,
            Gender(context.Provider), checked((int)time.Calendar.DayNumber));
    }

    /// <summary>Prepares this person's work offer and answers its text, or why there is none.</summary>
    internal string Offer(long provider)
    {
        if (Provider(provider) is not { } context) return "That provider has no work available.";
        try
        {
            quests.ConsumeWorkContact(provider);
            quests.PrepareWorkOffer(provider, context.Provider.Site, context.Faction,
                context.Member, progression.Level, context.Reputation, context.Rank,
                Gender(context.Provider), checked((int)time.Calendar.DayNumber));
            return quests.ReadOffer()!.Text;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        { return "Quest offer unavailable: " + error.Message; }
    }

    /// <summary>Accepts or declines the pending offer, still available only while its giver offers the same work; answers its line.</summary>
    internal string Answer(string identity, bool accept)
    {
        var offer = quests.PendingOffer;
        bool available = offer?.Quest.QuestorId is long giver && Provider(giver) is { } context
            && context.Faction == offer.Quest.FactionId && context.Provider.Site == offer.Site;
        return quests.AnswerOffer(identity, accept, available);
    }

    /// <summary>Starts the criminal guilds' invitations that are due, and answers a line for each that could not be delivered.</summary>
    internal IReadOnlyList<string> StartDueInvitations()
    {
        List<string>? undelivered = null;
        bool inside = profileKind() != DaggerfallWorldProfileKind.Exterior;
        long minute = time.Calendar.MinuteNumber;
        foreach (var (requirement, source, faction) in new[] {
            (DaggerfallCrimeGuildRequirement.Thieving, "O0A0AL00", DaggerfallConcreteGuildCatalog.ThievesFactionId),
            (DaggerfallCrimeGuildRequirement.Murder, "L0A01L00", DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId) })
        {
            if (!crime.IsInvitationDue(requirement, minute, inside)) continue;
            try
            {
                string id = "crime-invitation:" + source;
                if (!quests.TryGet(id, out _))
                    quests.Start(new(id, source + ".txt", source, DaggerfallQuestLifecycle.Active, null, [], []) { FactionId = faction });
                crime.MarkInvitationStarted(requirement, minute, inside);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            { (undelivered ??= []).Add("A guild invitation could not be delivered."); }
        }
        return undelivered ?? [];
    }
}
