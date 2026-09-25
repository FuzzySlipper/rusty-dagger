using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallConcreteGuildRuntimeTests
{
    [Fact]
    public void Admission_review_expel_and_rejoin_delegate_to_one_social_membership_owner()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallSocialState social = new(definitions.Factions);
        Dictionary<string, int> skills = new(StringComparer.Ordinal)
        {
            ["backstabbing"] = 22,
            ["climbing"] = 4,
        };
        DaggerfallGuildMembershipPolicy membership = new(
            social,
            skill => skills.GetValueOrDefault(skill),
            DaggerfallConcreteGuildCatalog.StandaloneMembershipPolicies);
        DaggerfallConcreteGuildMembershipRuntime runtime = new(membership);

        DaggerfallConcreteGuildMembershipOperation missingInvitation = runtime.Admit(
            42, currentDay: 0, new(0, skills, new(false, false)));
        Assert.False(missingInvitation.Applied);
        Assert.Equal(DaggerfallGuildAdmissionDenial.MissingCrimeEvidence, missingInvitation.Admission.Denial);
        Assert.False(social.GuildEligibility(42).IsMember);

        DaggerfallConcreteGuildMembershipOperation admitted = runtime.Admit(
            42, currentDay: 0, new(0, skills, new(true, false)));
        Assert.True(admitted.Applied);
        Assert.Equal(0, Assert.IsType<DaggerfallGuildMembershipResult>(admitted.Membership).View.Rank);

        social.ChangeFactionReputation(42, 10);
        skills["backstabbing"] = 23;
        skills["climbing"] = 5;
        Assert.Equal(DaggerfallGuildMembershipChange.Promoted, runtime.ReviewRank(42, currentDay: 28).Change);
        Assert.Equal(1, runtime.Read(42, currentDay: 28).Rank);

        Assert.Equal(DaggerfallGuildMembershipChange.Expelled, runtime.Expel(42, currentDay: 29).Change);
        Assert.False(social.GuildEligibility(42).IsMember);
        Assert.Equal(DaggerfallGuildMembershipChange.Rejoined,
            runtime.Rejoin(42, currentDay: 30, new(10, skills, new(true, false))).Membership!.Change);
        Assert.True(social.GuildEligibility(42).IsMember);
    }

    [Fact]
    public void Provider_bound_service_requires_the_named_npc_faction_and_existing_provider_admission()
    {
        using Fixture fixture = new();
        DaggerfallGuildMembershipPolicy membership = new(
            fixture.Social,
            _ => 0,
            DaggerfallConcreteGuildCatalog.StandaloneMembershipPolicies);
        DaggerfallConcreteGuildServiceRuntime runtime = new(membership, fixture.Npcs, fixture.Services);
        _ = fixture.Social.JoinGuild(41, currentDay: 0);
        DaggerfallNpcSite site = fixture.CurrentSite;

        DaggerfallServiceProvider provider = new(501, site, "training");
        fixture.Npcs.Restore([new DaggerfallNpc(501, DaggerfallNpcKind.Static, "trainer", site,
            new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, 999), "trainer", ["training"],
            DaggerfallNpcPresence.Active, null, null, null)]);
        DaggerfallConcreteGuildServiceRuntimeDecision wrongFaction = runtime.Evaluate(
            41, DaggerfallConcreteGuildService.Training, currentDay: 0,
            new(Provider: provider));
        Assert.Equal(DaggerfallGuildProviderAvailability.FactionMismatch, wrongFaction.ProviderAvailability);
        Assert.False(wrongFaction.CanUse);

        fixture.Npcs.Restore([new DaggerfallNpc(501, DaggerfallNpcKind.Static, "trainer", site,
            new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, 849), "trainer", ["training"],
            DaggerfallNpcPresence.Active, null, null, null)]);
        DaggerfallConcreteGuildServiceRuntimeDecision ready = runtime.Evaluate(
            41, DaggerfallConcreteGuildService.Training, currentDay: 0,
            new(Provider: provider));
        Assert.Equal(DaggerfallGuildProviderAvailability.Ready, ready.ProviderAvailability);
        Assert.Equal(DaggerfallServiceDenial.None, ready.ProviderDenial);
        Assert.True(ready.CanUse);

        fixture.Npcs.Restore([new DaggerfallNpc(501, DaggerfallNpcKind.Static, "speaker", site,
            new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, 849), "speaker", ["talk"],
            DaggerfallNpcPresence.Active, null, null, null)]);
        DaggerfallConcreteGuildServiceRuntimeDecision wrongService = runtime.Evaluate(
            41, DaggerfallConcreteGuildService.Training, currentDay: 0,
            new(Provider: new DaggerfallServiceProvider(501, site, "talk")));
        Assert.Equal(DaggerfallServiceDenial.ServiceUnavailable, wrongService.ProviderDenial);
        Assert.False(wrongService.CanUse);

        DaggerfallConcreteGuildServiceRuntimeDecision rest = runtime.Evaluate(
            41, DaggerfallConcreteGuildService.Rest, currentDay: 0);
        Assert.Equal(DaggerfallGuildProviderAvailability.NotRequired, rest.ProviderAvailability);
        Assert.True(rest.CanUse);
    }

    [Fact]
    public void Temple_runtime_uses_the_authored_group_seventeen_membership_variant()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallSocialState social = new(definitions.Factions);
        Dictionary<string, int> skills = new(StringComparer.Ordinal)
        {
            ["axe"] = 22,
            ["backstabbing"] = 4,
        };
        DaggerfallGuildMembershipPolicy membership = new(
            social,
            skill => skills.GetValueOrDefault(skill),
            DaggerfallConcreteGuildCatalog.AllMembershipPolicies);
        DaggerfallConcreteGuildMembershipRuntime runtime = new(membership);

        DaggerfallConcreteGuildMembershipOperation joined = runtime.Admit(
            DaggerfallConcreteGuildCatalog.ArkayTempleFactionId,
            currentDay: 0,
            new(0, skills, new(false, false)));

        Assert.True(joined.Applied);
        DaggerfallGuildMembershipView view = runtime.Read(DaggerfallConcreteGuildCatalog.ArkayTempleFactionId, currentDay: 0);
        Assert.Equal(17, view.GuildGroup);
        Assert.Equal(DaggerfallConcreteGuildCatalog.ArkayTempleFactionId, view.FactionId);
        Assert.True(social.GuildEligibility(DaggerfallConcreteGuildCatalog.ArkayTempleFactionId).IsMember);

        social.ChangeFactionReputation(DaggerfallConcreteGuildCatalog.ArkayFactionId, 10);
        social.ChangeFactionReputation(DaggerfallConcreteGuildCatalog.ArkayTempleFactionId, -10);
        Assert.Equal(10, runtime.Read(DaggerfallConcreteGuildCatalog.ArkayTempleFactionId, currentDay: 0).Reputation);
        Assert.Equal(10, membership.Assess(DaggerfallConcreteGuildCatalog.ArkayTempleFactionId).Reputation);
    }

    [Fact]
    public void Armor_claim_commits_only_after_a_real_unique_item_receipt_and_round_trips()
    {
        using Fixture fixture = new();
        DaggerfallGuildMembershipPolicy membership = new(
            fixture.Social,
            _ => 0,
            DaggerfallConcreteGuildCatalog.AllMembershipPolicies);
        DaggerfallConcreteGuildServiceRuntime services = new(membership, fixture.Npcs, fixture.Services);
        DaggerfallKnightlyOrderClaimState claims = new();
        DaggerfallKnightlyOrderClaimRuntime runtime = new(services, claims, FixedRandom.Create(3).Service);

        _ = fixture.Social.JoinGuild(414, currentDay: 0);
        DaggerfallServiceProvider provider = Provider(fixture, 845, "armor");
        DaggerfallConcreteGuildServiceInput input = new(provider);
        DaggerfallKnightlyArmorOfferResult offered = runtime.OfferArmor(414, currentDay: 0, "armor-test", input);

        Assert.True(offered.Offered);
        DaggerfallKnightlyArmorOffer offer = Assert.IsType<DaggerfallKnightlyArmorOffer>(offered.Offer);
        Assert.Equal(4, offer.TemplateIndices.Count);
        Assert.All(offer.TemplateIndices, template => Assert.InRange(template, 102, 108));
        Assert.Equal("iron", offer.Material);

        int callbackCount = 0;
        DaggerfallKnightlyArmorGrantRequest? grantRequest = null;
        DaggerfallKnightlyClaimTransactionResult rejected = runtime.CompleteArmor(
            offer,
            offer.TemplateIndices[0],
            currentDay: 0,
            _ =>
            {
                callbackCount++;
                return DaggerfallKnightlyArmorGrantResult.Rejected();
            },
            input);
        Assert.False(rejected.Applied);
        Assert.Equal(DaggerfallKnightlyClaimTransactionDenial.GrantRejected, rejected.Denial);
        Assert.False(claims.Read(414).HasArmorClaim(0));

        DaggerfallKnightlyClaimTransactionResult accepted = runtime.CompleteArmor(
            offer,
            offer.TemplateIndices[0],
            currentDay: 0,
            request =>
            {
                callbackCount++;
                grantRequest = request;
                return DaggerfallKnightlyArmorGrantResult.Accepted(9_001);
            },
            input);
        Assert.True(accepted.Applied);
        Assert.Equal(2, callbackCount);
        Assert.NotNull(grantRequest);
        Assert.Equal(offer.TemplateIndices[0], grantRequest!.TemplateIndex);
        Assert.Same(offer, grantRequest.Offer);
        Assert.True(claims.Read(414).HasArmorClaim(0));

        DaggerfallKnightlyArmorOfferResult repeated = runtime.OfferArmor(414, currentDay: 0, "armor-test", input);
        Assert.False(repeated.Offered);
        Assert.Equal(DaggerfallKnightlyClaimTransactionDenial.AlreadyClaimed, repeated.Denial);

        DaggerfallKnightlyOrderClaimState restored = new(claims.Capture());
        Assert.True(restored.Read(414).HasArmorClaim(0));
        Assert.False(restored.Read(414).HouseClaimed);
    }

    [Fact]
    public void House_claim_commits_only_after_an_applied_property_allocation()
    {
        using Fixture fixture = new();
        DaggerfallGuildMembershipPolicy membership = new(
            fixture.Social,
            _ => 0,
            DaggerfallConcreteGuildCatalog.AllMembershipPolicies);
        DaggerfallConcreteGuildServiceRuntime services = new(membership, fixture.Npcs, fixture.Services);
        DaggerfallKnightlyOrderClaimState claims = new();
        DaggerfallKnightlyOrderClaimRuntime runtime = new(services, claims, FixedRandom.Create(3).Service);

        _ = fixture.Social.JoinGuild(414, currentDay: 0);
        for (int rank = 0; rank < 9; rank++) _ = fixture.Social.PromoteGuild(414, currentDay: 0);
        DaggerfallServiceProvider provider = Provider(fixture, 848, "house");
        DaggerfallConcreteGuildServiceInput input = new(provider);
        DaggerfallKnightlyHouseOfferResult offered = runtime.OfferHouse(414, currentDay: 0, input);

        Assert.True(offered.Offered);
        DaggerfallKnightlyHouseOffer offer = Assert.IsType<DaggerfallKnightlyHouseOffer>(offered.Offer);
        Assert.Equal(9, offer.Rank);

        DaggerfallKnightlyClaimTransactionResult rejected = runtime.CompleteHouse(
            offer,
            currentDay: 0,
            _ => DaggerfallKnightlyHouseAllocationResult.FromPropertyTransaction(
                new(false, DaggerfallPropertyTransactionKind.Purchase, DaggerfallPropertyKind.House,
                    DaggerfallPropertyTransactionDenial.PaymentRejected, 0, new DaggerfallPropertyStorageKey("house/rejected"), "rejected")),
            input);
        Assert.False(rejected.Applied);
        Assert.Equal(DaggerfallKnightlyClaimTransactionDenial.AllocationRejected, rejected.Denial);
        Assert.False(claims.Read(414).HouseClaimed);

        DaggerfallKnightlyClaimTransactionResult accepted = runtime.CompleteHouse(
            offer,
            currentDay: 0,
            _ => DaggerfallKnightlyHouseAllocationResult.FromPropertyTransaction(
                new(true, DaggerfallPropertyTransactionKind.Purchase, DaggerfallPropertyKind.House,
                    DaggerfallPropertyTransactionDenial.None, 0, new DaggerfallPropertyStorageKey("house/accepted"), "allocated")),
            input);
        Assert.True(accepted.Applied);
        Assert.Equal("house/accepted", accepted.HouseStorageKey);
        Assert.True(claims.Read(414).HouseClaimed);

        DaggerfallKnightlyOrderClaimState restored = new(claims.Capture());
        Assert.True(restored.Read(414).HouseClaimed);
    }

    private static DaggerfallDefinitions LoadDefinitions()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
        return TestPayload.Definitions;
    }

    private static DaggerfallServiceProvider Provider(Fixture fixture, int factionId, string service)
    {
        DaggerfallNpcSite site = fixture.CurrentSite;
        fixture.Npcs.Restore([new DaggerfallNpc(501, DaggerfallNpcKind.Static, "provider", site,
            new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, factionId), "provider", [service],
            DaggerfallNpcPresence.Active, null, null, null)]);
        return new DaggerfallServiceProvider(501, site, service);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly EntityDirectory _entities = new();
        private readonly DaggerfallUniqueItemAllocator _unique = new(1_000);
        private readonly DaggerfallItemInstances _instances = new();
        private readonly MechanicsInventoryCoordinator _inventory;

        internal DaggerfallSocialState Social { get; }
        internal DaggerfallNpcRegistry Npcs { get; } = new();
        internal DaggerfallServiceTransactions Services { get; }
        internal DaggerfallNpcSite CurrentSite { get; } = new(0, "Daggerfall", "guild");

        internal Fixture()
        {
            DaggerfallDefinitions definitions = LoadDefinitions();
            Social = new(definitions.Factions);

            StatsComponent stats = new();
            stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value), new Stat(100, 0, 100, quantum: 1,
                rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero));
            Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            InventoryStore store = new();
            EntityId owner = _entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("test.player"));
            store.RegisterInventory(new InventoryState(owner, [new InventoryCapacityLimit(DaggerActorFactory.ClassicWeightMetric, ulong.MaxValue)]));
            InventoryComponent component = new(store, owner);
            _entities.Store.Add(owner, component);
            _inventory = new(component, _entities, items);
            DaggerfallEncumbrancePolicy encumbrance = new(_inventory, stats);
            DaggerfallCurrencyService currency = new(definitions, _inventory, _instances, encumbrance, _unique);
            Services = new(Npcs, Social, _inventory, _instances, currency, _unique,
                () => DaggerfallCalendar.Start, () => CurrentSite);
        }

        public void Dispose() => _entities.Dispose();
    }

    private class FixedRandom : DispatchProxy
    {
        private long _value;
        internal IRandomService Service { get; private set; } = null!;

        internal static FixedRandom Create(long value)
        {
            IRandomService service = DispatchProxy.Create<IRandomService, FixedRandom>();
            FixedRandom proxy = (FixedRandom)(object)service;
            proxy.Service = service;
            proxy._value = value;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            return new KeyedRngReceipt(Math.Clamp(_value, request.Minimum, request.Maximum));
        }
    }
}
