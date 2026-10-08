using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Facts;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Named session services; actor-local state lives on the canonical entities. The session constructs
/// it once, from the actor factory's assembly and the services it built over that assembly, so every
/// member exists from the start and none is ever replaced.
/// </summary>
internal sealed class DaggerfallState(
    DaggerActorAssembly assembled,
    GameplayServices<IProductFact> kit,
    DaggerfallEffectLifecycle effects,
    DaggerfallSkillUseReactions skillUses,
    DaggerfallLevelUpState levelUps,
    DaggerfallHeldEnchantments heldEnchantments,
    DaggerfallPoisonRuntime poisons,
    DaggerfallCrimeState crime,
    DaggerfallGuildMembershipPolicy guildMembership,
    DaggerfallConcreteGuildMembershipRuntime concreteGuildMembership,
    DaggerfallConcreteGuildServiceRuntime concreteGuildServices,
    DaggerfallKnightlyOrderClaimState knightlyClaims,
    DaggerfallKnightlyOrderClaimRuntime knightlyClaimActions,
    DaggerfallEncumbrancePolicy encumbrance,
    DaggerfallCurrencyService currency,
    DaggerfallRegionalBankState bank,
    DaggerfallLoanState loans,
    DaggerfallPropertyState property,
    DaggerfallLodgingState lodging,
    DaggerfallTravelState travel,
    DaggerfallServiceTransactions services,
    DaggerfallTempleServiceRuntime templeServices,
    DaggerfallSkillTrainingService skillTraining,
    DaggerfallRegionalPriceState regionalPrices,
    DaggerfallTradeQuoteService tradeQuotes,
    DaggerfallMerchantService merchants,
    DaggerfallTransportPolicy transport,
    DaggerfallWagonStorage wagon,
    DaggerfallSwimmingPolicy swimming,
    Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonDiscovery> dungeonDiscoveries,
    Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonActionGraph> dungeonActions,
    DaggerfallDialogueWorldState? dialogueWorld = null)
{
    internal GameplayServices<IProductFact> Kit { get; } = kit;
    /// <summary>Compiled Daggerfall effect policy over the attached per-actor Engine effect components.</summary>
    internal DaggerfallEffectLifecycle Effects { get; } = effects;
    /// <summary>Classic skill-use attribution and counters over the player's Kit progression state.</summary>
    internal DaggerfallSkillUseReactions SkillUses { get; } = skillUses;
    /// <summary>One durable, staged classic level-up allocation opened by ordinary rest eligibility.</summary>
    internal DaggerfallLevelUpState LevelUps { get; } = levelUps;
    /// <summary>Current donor quest-training timestamp, separate from the calendar that advances it.</summary>
    internal DaggerfallQuestTrainingState QuestTraining { get; } = assembled.QuestTraining;
    internal PlayerControlState PlayerControl { get; } = assembled.PlayerControl;
    /// <summary>
    /// Whether the player holds the weapon drawn. It gates the player's attacks and is what a sheathed
    /// weapon tells perception, so it is saved; the viewmodel only shows it.
    /// </summary>
    internal bool WeaponDrawn { get; set; } = true;
    internal ActorsState Actors { get; } = assembled.Actors;
    internal ProgressionState Progression => Actors.Player.Progression;
    internal MechanicsInventoryCoordinator Inventory { get; } = assembled.Inventory;

    /// <summary>The enchantments the player's worn items hold, recomputed from what they are worn on.</summary>
    internal DaggerfallHeldEnchantments HeldEnchantments { get; } = heldEnchantments;

    /// <summary>The poisons the session's actors carry, and the damage a completed one still holds.</summary>
    internal DaggerfallPoisonRuntime Poisons { get; } = poisons;
    internal MechanicsEquipmentCoordinator Equipment { get; } = assembled.Equipment;
    internal MechanicsInventoryContainerCoordinator Containers { get; } = assembled.Containers;
    /// <summary>The one managed inventory store every actor inventory and equipment registers in.</summary>
    internal InventoryStore InventoryStore { get; } = assembled.InventoryStore;
    /// <summary>Inventory and equipment coordinators over any live actor, against the admitted item catalog.</summary>
    internal DaggerfallActorInventories ActorInventories { get; } = assembled.ActorInventories;
    /// <summary>The session's scoped quest and world variables, handed explicitly to readers.</summary>
    internal DaggerfallVariableStore Variables { get; } = assembled.Variables;
    /// <summary>The session's NPC identities, handed explicitly to talk, damage and quest readers.</summary>
    internal DaggerfallNpcRegistry Npcs { get; } = assembled.Npcs;
    /// <summary>Persistent Daggerfall reputation, reaction, and guild-membership policy for talk, services, and quests.</summary>
    internal DaggerfallSocialState Social { get; } = assembled.Social;
    internal DaggerfallCrimeState Crime { get; } = crime;
    /// <summary>Guild admission and rank policy over the canonical social membership records.</summary>
    internal DaggerfallGuildMembershipPolicy GuildMembership { get; } = guildMembership;
    internal DaggerfallConcreteGuildMembershipRuntime ConcreteGuildMembership { get; } = concreteGuildMembership;
    internal DaggerfallConcreteGuildServiceRuntime ConcreteGuildServices { get; } = concreteGuildServices;
    /// <summary>Source-backed temple donation, cure, and blessing callers over common services/effects.</summary>
    internal DaggerfallTempleServiceRuntime TempleServices { get; } = templeServices;
    internal DaggerfallKnightlyOrderClaimState KnightlyClaims { get; } = knightlyClaims;
    internal DaggerfallKnightlyOrderClaimRuntime KnightlyClaimActions { get; } = knightlyClaimActions;
    /// <summary>Daggerfall instance meaning paired with Engine-backed stacks and unique items.</summary>
    internal DaggerfallItemInstances ItemInstances { get; } = assembled.ItemInstances;
    /// <summary>The committed player identity and its cancellable creation draft.</summary>
    internal DaggerfallCharacterState Character { get; } = assembled.Character;
    internal DaggerfallRacialOverrides RacialOverrides => Character.RacialOverrides!;
    /// <summary>Current Daggerfall quest instances over admitted definitions and durable product bindings.</summary>
    internal DaggerfallQuestInstances Quests { get; } = assembled.Quests;
    internal DaggerfallQuestItems QuestItems { get; set; } = null!;
    /// <summary>Live carried-weight policy over the player's canonical Engine inventory.</summary>
    internal DaggerfallEncumbrancePolicy Encumbrance { get; } = encumbrance;
    /// <summary>Inventory-backed coins, letters of credit, and the one persistent bank balance.</summary>
    internal DaggerfallCurrencyService Currency { get; } = currency;
    /// <summary>Regional account balances partitioning the currency settlement account.</summary>
    internal DaggerfallRegionalBankState Bank { get; } = bank;
    internal DaggerfallLoanState Loans { get; } = loans;
    internal DaggerfallPropertyState Property { get; } = property;
    internal DaggerfallLodgingState Lodging { get; } = lodging;
    internal DaggerfallTravelState Travel { get; } = travel;
    /// <summary>Typed Daggerfall service admission, quotes, outcomes, and pending concrete work.</summary>
    internal DaggerfallServiceTransactions Services { get; } = services;
    /// <summary>Permanent skill training through the current service and progression owners.</summary>
    internal DaggerfallSkillTrainingService SkillTraining { get; } = skillTraining;
    /// <summary>Calendar-driven regional market factors and item quote policy.</summary>
    internal DaggerfallRegionalPriceState RegionalPrices { get; } = regionalPrices;
    internal DaggerfallTradeQuoteService TradeQuotes { get; } = tradeQuotes;
    /// <summary>Provider-owned stock, repair custody, and payment-backed merchant actions.</summary>
    internal DaggerfallMerchantService Merchants { get; } = merchants;
    /// <summary>Current transport choice and its Engine-backed wagon container.</summary>
    internal DaggerfallTransportPolicy Transport { get; } = transport;
    internal DaggerfallWagonStorage Wagon { get; } = wagon;
    /// <summary>Accepted Engine swimming mode, breath continuation, and water-volume identity.</summary>
    internal DaggerfallSwimmingPolicy Swimming { get; } = swimming;
    /// <summary>Generated spoken-world events and their transition/expiry state.</summary>
    internal DaggerfallDialogueWorldState DialogueWorld { get; } = dialogueWorld ?? new();
    /// <summary>Durable discovery for admitted dungeon profiles, independent of the active scene projection.</summary>
    internal Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonDiscovery> DungeonDiscoveries { get; } = dungeonDiscoveries;
    /// <summary>Profile-scoped normalized dungeon action graphs sharing the session variable store.</summary>
    internal Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonActionGraph> DungeonActions { get; } = dungeonActions;
}
