using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Joining a guild: whether the player meets its reputation and skill requirements, the offer the guild makes and
/// holds for one dialogue page, and the admission with the guild's own words for each stage.
/// </summary>
/// <param name="dialogueRevision">The revision of the dialogue page now open, which an offer is good for.</param>
internal sealed class DaggerfallGuildAdmission(
    DaggerfallConcreteGuildMembershipRuntime membership,
    DaggerfallSocialState social,
    DaggerfallSkillUseReactions skillUses,
    DaggerfallCrimeState crime,
    Func<string?> dialogueRevision)
{
    private (long Npc, int Faction, string? Revision)? _offer;

    /// <summary>Asks to join (<paramref name="choice"/> null), or answers the guild's offer with "yes" or "no".</summary>
    internal DaggerfallGuildProviderResult Join(DaggerfallConcreteGuildDefinition guild,
        DaggerfallNpc npc, int day, string? choice)
    {
        if (choice == "no")
        {
            _offer = null;
            return new(true, DaggerfallGuildProviderDenial.None, "You decline to join.");
        }
        if (membership.Read(guild.FactionId, day).IsMember)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.NotEligible, "You already belong to this guild.");
        DaggerfallGuildAdmissionContext context = new(
            social.FactionReputation(guild.ToMembershipPolicyDefinition().ReputationFactionId),
            guild.GuildSkills.ToDictionary(skill => skill, skillUses.PermanentSkillValue), crime.InvitationEvidence);
        var admission = DaggerfallConcreteGuildPolicy.AssessAdmission(guild, context);
        if (!admission.Eligible)
        {
            _offer = null;
            return JoinText(guild, false, admission.Denial == DaggerfallGuildAdmissionDenial.InsufficientReputation ? "reputation" : "skills");
        }
        // Criminal guild membership is bestowed by the invitation quest, as in the donor.
        if (guild.InvitationRequirement != DaggerfallGuildInvitationRequirement.None)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.NotEligible, "Admission to this guild is by invitation. Follow the instructions in its letter.");
        if (choice is null)
        {
            _offer = (npc.DurableId, guild.FactionId, dialogueRevision());
            return JoinText(guild, true, "eligible") with { Options = [new("yes", "Yes, join"), new("no", "No")] };
        }
        if (choice != "yes" || _offer != (npc.DurableId, guild.FactionId, dialogueRevision()))
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.OfferExpired, "Ask the guild about joining again.");
        _offer = null;
        // Expulsion removes the old membership; both fresh admission and readmission use the
        // same rank-zero rules and the canonical social owner's mutually exclusive guild groups.
        var result = membership.Admit(guild.FactionId, day, context);
        if (!result.Applied)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.NotEligible,
                result.Membership?.Denial == DaggerfallGuildMembershipDenial.VariantConflict
                    ? "You already belong to another temple or knightly order." : "You no longer meet the guild's admission requirements.");
        return JoinText(guild, true, "welcome") with { Membership = result.Membership };
    }

    private static DaggerfallGuildProviderResult JoinText(DaggerfallConcreteGuildDefinition guild, bool accepted, string stage)
    {
        (int rep, int skills, int eligible, int welcome) = guild.Kind switch
        {
            DaggerfallConcreteGuildKind.Fighters => (679, 680, 681, 684),
            DaggerfallConcreteGuildKind.Mages => (612, 611, 606, 5293),
            DaggerfallConcreteGuildKind.KnightlyOrder => (751, 750, 752, 5291),
            DaggerfallConcreteGuildKind.Temple => (745, 744, 740, guild.ParentFactionId switch
            { 21 => 5287, 22 => 5288, 24 or 33 => 5289, 27 => 6610, _ => 5290 }),
            _ => (0, 0, 0, 0),
        };
        int resource = stage switch { "reputation" => rep, "skills" => skills, "eligible" => eligible, _ => welcome };
        return new(accepted, accepted ? DaggerfallGuildProviderDenial.None : DaggerfallGuildProviderDenial.NotEligible,
            accepted ? "Welcome to the guild." : "You do not meet this guild's admission requirements.",
            TextResource: resource == 0 ? null : resource, GuildName: guild.DisplayName,
            GodName: guild.ParentFactionId switch { 21 => "Arkay", 22 => "Zenithar", 24 => "Mara", 26 => "Akatosh", 27 => "Julianos", 29 => "Dibella", 33 => "Stendarr", 35 => "Kynareth", _ => null });
    }
}
