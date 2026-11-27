using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer runtime inspection commands for the live social and political layers:
/// active settlement households/offices, regional Great Houses/rulers/seats, and player standings/law/vouches.
/// </summary>
public interface IDeveloperSocialCommands : IDebugCommandModule
{
    [DebugCommand("social.site", Description = "Developer: inspect the active settlement's loaded households, local household grudge/goodwill standings, and local institutional offices as JSON.")]
    DebugCommandResult Site();

    [DebugCommand("social.region", Description = "Developer: inspect the active region's provincial Great Houses, Tier-2 palace court offices, regional ruler, board-game seat personas, and sparse office overrides as JSON.")]
    DebugCommandResult Region();

    [DebugCommand("social.player", Description = "Developer: inspect the player's regional reputations, non-zero faction standings, guild affiliations, legal status, and active vouches as JSON.")]
    DebugCommandResult Player();

    [DebugCommand("social.schedule", Description = "Developer: inspect the active settlement's live NPC schedule entries, why each NPC is outdoors or sheltered, their shelter target, and a site summary as JSON.")]
    DebugCommandResult Schedule();

    [DebugCommand("social.office.vacate", Description = "Developer: vacate or depose an office through OfficeLedger. OFFICE is 'ruler', an exact officeKey, 'faction:<id>', or a holder durableId; CAUSE is death, card, quest, law, or succession.")]
    DebugCommandResult VacateOffice(string office, string cause);

    [DebugCommand("social.household.standing", Description = "Developer: set a household's standing delta (-100..100) for FAMILY_ID (fam:local:..., fam:house:..., or fam:dynasty:...) through SocialConsequences and WordSpread.")]
    DebugCommandResult SetHouseholdStanding(string familyId, int standingDelta);

    [DebugCommand("social.vouch.grant", Description = "Developer: grant a structured vouch from REFERRER (durableId or castId) to TARGET (durableId or castId) of KIND (contact, trade, guild, court) through Introductions and ActorLedger.")]
    DebugCommandResult GrantVouch(string referrer, string target, string kind);

    [DebugCommand("social.vouch.revoke", Description = "Developer: revoke or void vouches on TARGET (durableId, castId, or 'all') from REFERRER (durableId, castId, or 'all') with REASON (remove, wronged, dead, hostile) through Introductions and ActorLedger.")]
    DebugCommandResult RevokeVouch(string target, string referrer, string reason);
}
