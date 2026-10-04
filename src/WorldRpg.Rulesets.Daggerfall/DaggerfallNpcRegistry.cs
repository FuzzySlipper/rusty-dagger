using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What kind of person an NPC is: identity stability follows the kind.</summary>
public enum DaggerfallNpcKind
{
    /// <summary>A fixed person of a site: one stable identity across sessions.</summary>
    Static,
    /// <summary>A quest person: stable while its quest names it.</summary>
    Questor,
    /// <summary>An incidental civilian, either transient or a source placement with a stable key.</summary>
    Civilian,
}

/// <summary>Whether an NPC currently answers the world.</summary>
public enum DaggerfallNpcPresence
{
    Active,
    Hidden,
    Removed,
}

/// <summary>One NPC's site binding: where it belongs, not where it stands.</summary>
/// <param name="Region">The classic region.</param>
/// <param name="Location">The location name.</param>
/// <param name="Building">The building key, empty when the NPC belongs to no building.</param>
public readonly record struct DaggerfallNpcSite(int Region, string Location, string Building, string? ProfileId = null);

/// <summary>One NPC's appearance: what the world sees.</summary>
/// <param name="Race">The race name.</param>
/// <param name="Gender">Male or Female.</param>
/// <param name="BillboardArchive">The billboard texture archive.</param>
/// <param name="BillboardRecord">The billboard texture record.</param>
/// <param name="NameSeed">The seed the name derives from.</param>
/// <param name="FactionId">The faction the NPC answers to, or zero.</param>
public readonly record struct DaggerfallNpcAppearance(string Race, string Gender, int BillboardArchive, int BillboardRecord, int NameSeed, int FactionId);

/// <summary>One NPC: its durable identity, kind, site, appearance, role and presence.</summary>
/// <param name="DurableId">The durable actor identity.</param>
/// <param name="Kind">What kind of person it is.</param>
/// <param name="StableKey">The site-stable key for statics and questors; empty for civilians.</param>
/// <param name="Site">The site it belongs to.</param>
/// <param name="Appearance">What the world sees.</param>
/// <param name="Role">The role: questor, civilian, guard, shopkeeper.</param>
/// <param name="Services">The services it offers: talk, shop, quest.</param>
/// <param name="Presence">Whether it currently answers the world.</param>
/// <param name="X">The X position it was relocated to, when any.</param>
/// <param name="Y">The Y position it was relocated to, when any.</param>
/// <param name="Z">The Z position it was relocated to, when any.</param>
public sealed record DaggerfallNpc(
    long DurableId,
    DaggerfallNpcKind Kind,
    string StableKey,
    DaggerfallNpcSite Site,
    DaggerfallNpcAppearance Appearance,
    string Role,
    IReadOnlyList<string> Services,
    DaggerfallNpcPresence Presence,
    float? X,
    float? Y,
    float? Z)
{
    public string? DisplayName { get; init; }
    /// <summary>Current physical profile; Site retains the stable social origin.</summary>
    internal DaggerfallWorldProfileKey? Profile { get; init; }
}

/// <summary>
/// Static NPC, questor and civilian identity with site binding: one durable identity per person
/// that talk, damage, quests and saves all share. Statics and questors register under a
/// site-stable key, so re-entering a site finds the same person. Source-authored population records
/// also use their source placement key so repeated admission does not clone a live civilian; callers
/// that need an incidental passer-by can continue using the fresh-identity overload. Presence changes
/// hide, remove or relocate without duplicating the actor; the allocator that mints civilian identities
/// is wired once by the session that owns it.
/// </summary>
public sealed class DaggerfallNpcRegistry
{
    private readonly Dictionary<long, DaggerfallNpc> _npcs = new();
    private readonly Dictionary<string, long> _stable = new(StringComparer.Ordinal);

    /// <summary>The identity allocator the session wires once; civilians cannot mint without it.</summary>
    public DurableIdentityAllocator? Identities { get; set; }

    /// <summary>
    /// Registers a static or questor: the same site and key always answer the same identity, so a
    /// quest reference survives unload and restore.
    /// </summary>
    public long RegisterStable(DaggerfallNpcKind kind, string stableKey, DaggerfallNpcSite site, DaggerfallNpcAppearance appearance, string role, IReadOnlyList<string> services)
    {
        if (kind == DaggerfallNpcKind.Civilian)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Civilians regenerate; they never hold a stable key.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(stableKey);
        ValidateSite(site);
        ValidateAppearance(appearance);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(services);
        string key = StableIdentityKey(kind, site, stableKey);
        if (_stable.TryGetValue(key, out long existing))
        {
            return existing;
        }

        DurableIdentityAllocator allocator = Identities ?? throw new InvalidOperationException("The NPC registry mints no identity before the session wires its allocator.");
        long durableId = checked((long)allocator.Allocate(DurableIdentityKind.Actor).Value);
        _npcs.Add(durableId, new DaggerfallNpc(durableId, kind, stableKey, site, appearance, role, [.. services], DaggerfallNpcPresence.Active, null, null, null));
        _stable.Add(key, durableId);
        return durableId;
    }

    /// <summary>Registers a civilian: always a fresh identity, even for identical visuals.</summary>
    public long RegisterCivilian(DaggerfallNpcSite site, DaggerfallNpcAppearance appearance, string role, IReadOnlyList<string> services)
    {
        ValidateSite(site);
        ValidateAppearance(appearance);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(services);
        DurableIdentityAllocator allocator = Identities ?? throw new InvalidOperationException("The NPC registry mints no identity before the session wires its allocator.");
        long durableId = checked((long)allocator.Allocate(DurableIdentityKind.Actor).Value);
        _npcs.Add(durableId, new DaggerfallNpc(durableId, DaggerfallNpcKind.Civilian, string.Empty, site, appearance, role, [.. services], DaggerfallNpcPresence.Active, null, null, null));
        return durableId;
    }

    /// <summary>
    /// Registers one source-backed population placement idempotently. The key is the importer placement
    /// identity, not a generated demo name, so cell re-entry returns the existing civilian and keeps its
    /// durable actor/corpse/quest relationships intact.
    /// </summary>
    public long RegisterPopulationCivilian(DaggerfallNpcSite site, string sourceKey, DaggerfallNpcAppearance appearance, string role, IReadOnlyList<string> services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ValidateSite(site);
        ValidateAppearance(appearance);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(services);
        string key = StableIdentityKey(DaggerfallNpcKind.Civilian, site, sourceKey);
        if (_stable.TryGetValue(key, out long existing)) return existing;

        DurableIdentityAllocator allocator = Identities ?? throw new InvalidOperationException("The NPC registry mints no identity before the session wires its allocator.");
        long durableId = checked((long)allocator.Allocate(DurableIdentityKind.Actor).Value);
        _npcs.Add(durableId, new DaggerfallNpc(durableId, DaggerfallNpcKind.Civilian, sourceKey, site, appearance, role, [.. services], DaggerfallNpcPresence.Active, null, null, null));
        _stable.Add(key, durableId);
        return durableId;
    }

    /// <summary>
    /// Refreshes source-authored appearance and service facts while retaining the one durable
    /// identity, current pose, presence and any actor/corpse relationship.
    /// </summary>
    public void RefreshPopulationFacts(long durableId, DaggerfallNpcAppearance appearance, string role, IReadOnlyList<string> services)
    {
        DaggerfallNpc npc = Require(durableId);
        if (npc.Kind != DaggerfallNpcKind.Civilian || string.IsNullOrWhiteSpace(npc.StableKey)
            || !npc.StableKey.StartsWith("population/", StringComparison.Ordinal))
            throw new ArgumentException($"NPC {durableId} is not a source-backed population civilian.", nameof(durableId));
        ValidateAppearance(appearance);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(services);
        _npcs[durableId] = npc with { Appearance = appearance, Role = role, Services = [.. services] };
    }

    /// <summary>Reads one NPC by its durable identity.</summary>
    public DaggerfallNpc Require(long durableId) =>
        _npcs.TryGetValue(durableId, out DaggerfallNpc? npc) ? npc : throw new InvalidOperationException($"No NPC answers durable identity {durableId}.");

    internal bool IsStatic(long durableId) => _npcs.TryGetValue(durableId, out DaggerfallNpc? npc)
        && npc.Kind == DaggerfallNpcKind.Static;

    /// <summary>
    /// Reads the registry's presence for a live actor. Actors with no NPC entry are ordinary
    /// authored or encounter actors and remain eligible; an admitted civilian becomes ineligible
    /// as soon as the population owner hides it for night or unload.
    /// </summary>
    internal bool IsGameplayActive(long durableId) =>
        !_npcs.TryGetValue(durableId, out DaggerfallNpc? npc)
        || npc.Presence == DaggerfallNpcPresence.Active;

    /// <summary>Every registered NPC.</summary>
    public IReadOnlyList<DaggerfallNpc> All => [.. _npcs.Values.OrderBy(npc => npc.DurableId)];

    /// <summary>Retains the selected display name on the canonical NPC identity.</summary>
    public void SetDisplayName(long durableId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _npcs[durableId] = Require(durableId) with { DisplayName = name };
    }

    /// <summary>Hides or reveals one NPC without duplicating it.</summary>
    public void SetPresence(long durableId, DaggerfallNpcPresence presence)
    {
        if (!Enum.IsDefined(presence))
        {
            throw new ArgumentOutOfRangeException(nameof(presence), presence, "An NPC presence names a state the contract does not declare.");
        }

        _npcs[durableId] = Require(durableId) with { Presence = presence };
    }

    /// <summary>Relocates one NPC; the site binding stays where it belongs.</summary>
    public void Relocate(long durableId, float x, float y, float z) =>
        _npcs[durableId] = Require(durableId) with { X = x, Y = y, Z = z };

    /// <summary>Every written NPC in durable order, for the save owner.</summary>
    internal void Place(long durableId, DaggerfallWorldProfileKey profile, WorldPoint position)
    {
        profile.Validate();
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentException("NPC profile position must be finite.");
        _npcs[durableId] = Require(durableId) with { Profile = profile, X = position.X, Y = position.Y, Z = position.Z,
            Presence = DaggerfallNpcPresence.Active };
    }

    /// <summary>Removes a failed admission's current placement while retaining its stable identity.</summary>
    internal void Unplace(long durableId) =>
        _npcs[durableId] = Require(durableId) with { Profile = null, X = null, Y = null, Z = null };

    internal IReadOnlyList<DaggerfallNpc> Capture() => All;

    /// <summary>Restores NPCs; refuses a record the registry would not accept live.</summary>
    internal void Restore(IEnumerable<DaggerfallNpc> npcs)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        _npcs.Clear();
        _stable.Clear();
        foreach (DaggerfallNpc npc in npcs)
        {
            ArgumentNullException.ThrowIfNull(npc);
            ValidateSite(npc.Site);
            ValidateAppearance(npc.Appearance);
            npc.Profile?.Validate();
            if (npc.Profile is not null && (npc.X is null || npc.Y is null || npc.Z is null)
                || npc.X is float x && !float.IsFinite(x) || npc.Y is float y && !float.IsFinite(y) || npc.Z is float z && !float.IsFinite(z))
                throw new ArgumentException($"NPC {npc.DurableId} has invalid current profile coordinates.");
            if (npc.DisplayName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(npc.DisplayName);
            if (!Enum.IsDefined(npc.Kind) || !Enum.IsDefined(npc.Presence))
            {
                throw new ArgumentOutOfRangeException(nameof(npcs), npc.Kind, "A saved NPC names a kind or presence the contract does not declare.");
            }

            if (npc.Kind != DaggerfallNpcKind.Civilian || !string.IsNullOrWhiteSpace(npc.StableKey))
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(npc.StableKey);
                _stable.Add(StableIdentityKey(npc.Kind, npc.Site, npc.StableKey), npc.DurableId);
            }

            _npcs.Add(npc.DurableId, npc);
        }
    }

    private static string StableIdentityKey(DaggerfallNpcKind kind, DaggerfallNpcSite site, string stableKey) =>
        $"{kind}\0{site.Region}\0{site.Location}\0{site.Building}\0{site.ProfileId}\0{stableKey}";

    private static void ValidateSite(DaggerfallNpcSite site)
    {
        if (site.Region is < 0 or > 61 || string.IsNullOrWhiteSpace(site.Location))
        {
            throw new ArgumentOutOfRangeException(nameof(site), site.Region, "An NPC site names no classic region or location.");
        }
    }

    private static void ValidateAppearance(DaggerfallNpcAppearance appearance)
    {
        if (string.IsNullOrWhiteSpace(appearance.Race) || (appearance.Gender != "Male" && appearance.Gender != "Female"))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance), appearance.Race, "An NPC appearance states no race or gender.");
        }

        if (appearance.BillboardArchive < 0 || appearance.BillboardRecord < 0 || appearance.FactionId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appearance), appearance.BillboardArchive, "An NPC appearance states no billboard or faction.");
        }
    }
}
