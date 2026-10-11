using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private (long Npc, int Faction, string? Revision)? _guildJoinOffer;

    private DaggerfallGuildProviderResult JoinGuild(DaggerfallConcreteGuildDefinition guild,
        DaggerfallNpc npc, int day, string? choice)
    {
        if (choice == "no")
        {
            _guildJoinOffer = null;
            return new(true, DaggerfallGuildProviderDenial.None, "You decline to join.");
        }
        if (State.ConcreteGuildMembership.Read(guild.FactionId, day).IsMember)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.NotEligible, "You already belong to this guild.");
        DaggerfallGuildAdmissionContext context = new(
            State.Social.FactionReputation(guild.ToMembershipPolicyDefinition().ReputationFactionId),
            guild.GuildSkills.ToDictionary(skill => skill, State.SkillUses.PermanentSkillValue), State.Crime.InvitationEvidence);
        var admission = DaggerfallConcreteGuildPolicy.AssessAdmission(guild, context);
        if (!admission.Eligible)
        {
            _guildJoinOffer = null;
            return GuildJoinText(guild, false, admission.Denial == DaggerfallGuildAdmissionDenial.InsufficientReputation ? "reputation" : "skills");
        }
        // Criminal guild membership is bestowed by the invitation quest, as in the donor.
        if (guild.InvitationRequirement != DaggerfallGuildInvitationRequirement.None)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.NotEligible, "Admission to this guild is by invitation. Follow the instructions in its letter.");
        if (choice is null)
        {
            _guildJoinOffer = (npc.DurableId, guild.FactionId, _activationPresentation.View.Dialogue?.Revision);
            return GuildJoinText(guild, true, "eligible") with { Options = [new("yes", "Yes, join"), new("no", "No")] };
        }
        if (choice != "yes" || _guildJoinOffer != (npc.DurableId, guild.FactionId, _activationPresentation.View.Dialogue?.Revision))
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.OfferExpired, "Ask the guild about joining again.");
        _guildJoinOffer = null;
        // Expulsion removes the old membership; both fresh admission and readmission use the
        // same rank-zero rules and the canonical social owner's mutually exclusive guild groups.
        var result = State.ConcreteGuildMembership.Admit(guild.FactionId, day, context);
        if (!result.Applied)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.NotEligible,
                result.Membership?.Denial == DaggerfallGuildMembershipDenial.VariantConflict
                    ? "You already belong to another temple or knightly order." : "You no longer meet the guild's admission requirements.");
        return GuildJoinText(guild, true, "welcome") with { Membership = result.Membership };
    }

    private static DaggerfallGuildProviderResult GuildJoinText(DaggerfallConcreteGuildDefinition guild, bool accepted, string stage)
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

    private DaggerfallGuildProviderResult? ReceiveGuildBenefits(DaggerfallNpc npc)
    {
        var building = CurrentInteriorBuilding();
        if (building is null || !TryResolveGuildProvider(npc, building, null, out var guild, out var provider, out _)) return null;
        int day = checked((int)_time.Calendar.DayNumber);
        (_, bool noRegen) = RestCharacterTraits();
        List<string> messages = [];
        foreach ((DaggerfallConcreteGuildService service, string trackId, string message) in new[] {
            (DaggerfallConcreteGuildService.FreeHealing, "health", "The temple restores your health."),
            (DaggerfallConcreteGuildService.FreeMagickaRecharge, "magicka", "The guild restores your spell points.") })
        {
            if (!State.ConcreteGuildServices.Evaluate(guild!.FactionId, service, day, new(provider, provider.Site.Region, noRegen)).CanUse) continue;
            var track = State.Actors.Player.Stats.GetTrack(TrackId.Parse(trackId));
            if (track.Current >= track.Maximum.Value) continue;
            track.SetCurrent(track.Maximum.Value, clamp: true);
            messages.Add(message);
        }
        bool restore = State.ConcreteGuildServices.Evaluate(guild!.FactionId, DaggerfallConcreteGuildService.FreeHealing, day).CanUse
            && State.Effects.Active.Any(effect => effect.Context.Target.Value == (ulong)State.Actors.Player.DurableId
                && effect.Definition.Spell is { Type: 7 or 11 });
        if (restore) messages.Add("Would you like the temple to restore your damaged attributes?");
        return new(true, DaggerfallGuildProviderDenial.None, string.Join("\n", messages), Options: restore
            ? [new("yes", "Restore my attributes", "restore-attributes"), new("no", "No", "restore-attributes")] : null);
    }

    private bool HasFreeGuildShipTravel() => DaggerfallConcreteGuildCatalog.All.Any(guild =>
        State.ConcreteGuildServices.Evaluate(guild.FactionId, DaggerfallConcreteGuildService.FreeShip,
            checked((int)_time.Calendar.DayNumber)).CanUse);
}
