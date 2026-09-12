namespace WorldRpg.Kit.World;

/// <summary>What a durable reference currently denotes, without resolving it to runtime state.</summary>
public enum DurableIdentityClassification
{
    /// <summary>The identity is allocated and currently belongs to this world.</summary>
    Live,

    /// <summary>The identity was allocated and has since been removed. Removal is not absence.</summary>
    Removed,

    /// <summary>This allocator never issued the identity: it is at or above the cursor.</summary>
    NeverIssued,

    /// <summary>The stored identity does not belong to the requested kind of world object.</summary>
    WrongKind,
}

/// <summary>Kinds of durable world object a ruleset may reference. The kind is mechanism, not ruleset vocabulary.</summary>
public enum DurableIdentityKind
{
    Actor,
    Item,
    Container,
    Resource,
}

/// <summary>
/// One durable reference to a world object, independent of any Engine handle.
/// Rulesets assign meaning to the value inside a kind; the Engine handle that
/// currently materializes the object is a separate, session-scoped fact.
/// </summary>
public readonly record struct DurableIdentityReference(DurableIdentityKind Kind, ulong Value)
{
    public DurableIdentityReference Validate()
    {
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        if (Value == 0) throw new ArgumentOutOfRangeException(nameof(Value));
        return this;
    }
}

/// <summary>Persisted allocator evidence for one kind of durable world object.</summary>
public readonly record struct KindAllocatorState(
    DurableIdentityKind Kind,
    ulong NextIdentity,
    ulong[] Reserved,
    ulong[] Removed)
{
    public KindAllocatorState Validate()
    {
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        ArgumentNullException.ThrowIfNull(Reserved);
        ArgumentNullException.ThrowIfNull(Removed);
        if (NextIdentity == 0) throw new ArgumentOutOfRangeException(nameof(NextIdentity));
        HashSet<ulong> reserved = [];
        foreach (ulong value in Reserved)
        {
            // Authored reservations may sit above the cursor: a site loaded later
            // still claims its content identities before an allocation reaches them.
            if (value == 0 || !reserved.Add(value))
                throw new ArgumentException("Durable identity reservations must be non-zero and distinct.", nameof(Reserved));
        }

        HashSet<ulong> removed = [];
        foreach (ulong value in Removed)
        {
            if (value == 0 || !removed.Add(value))
                throw new ArgumentException("Removed durable identities must be non-zero and distinct.", nameof(Removed));
            if (value >= NextIdentity)
                throw new ArgumentException("A removed durable identity must have been issued by this allocator.", nameof(Removed));
            if (reserved.Contains(value))
                throw new ArgumentException("A reserved durable identity cannot also be removed.", nameof(Removed));
        }

        return this;
    }
}

/// <summary>Complete persisted identity state for every kind of durable world object.</summary>
public sealed record DurableIdentityState
{
    private readonly KindAllocatorState[] _kinds;

    public DurableIdentityState(KindAllocatorState[] kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        _kinds = kinds.ToArray();
    }

    /// <summary>A copy of the persisted per-kind evidence, so later reuse cannot rewrite it.</summary>
    public KindAllocatorState[] Kinds => _kinds.ToArray();

    public DurableIdentityState Validate()
    {
        HashSet<DurableIdentityKind> kinds = [];
        foreach (KindAllocatorState state in _kinds)
        {
            state.Validate();
            if (!kinds.Add(state.Kind))
                throw new ArgumentException("Durable identity state must carry exactly one entry per kind.", nameof(Kinds));
        }

        return this;
    }

    /// <summary>Requires the persisted state to describe exactly the requested kinds.</summary>
    public DurableIdentityState RequireKinds(IEnumerable<DurableIdentityKind> expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        DurableIdentityKind[] ordered = expected.OrderBy(value => (int)value).ToArray<DurableIdentityKind>();
        if (ordered.Length == 0) throw new ArgumentException("At least one durable identity kind is required.", nameof(expected));
        if (ordered.Distinct().Count() != ordered.Length)
            throw new ArgumentException("Requested durable identity kinds must be distinct.", nameof(expected));
        DurableIdentityKind[] actual = Kinds.Select(state => state.Kind).OrderBy(value => (int)value).ToArray();
        if (!actual.SequenceEqual(ordered))
            throw new ArgumentException("Persisted durable identity state does not cover the requested kinds.", nameof(expected));
        return this;
    }
}

/// <summary>
/// Product-owned durable identity ledger for authored and allocated world objects.
///
/// Authored identities are reserved before gameplay starts, so an allocation can
/// never collide with content that a later site load materializes. Allocation is
/// monotonic: every identity below the cursor was issued, every identity at or
/// above it was not. Removal records a tombstone for an issued identity, which
/// keeps "removed" distinguishable from "never loaded" for as long as the save
/// keeps the tombstone.
/// </summary>
public sealed class DurableIdentityAllocator
{
    private readonly Dictionary<DurableIdentityKind, KindLedger> _kinds = [];

    public DurableIdentityAllocator(
        DurableIdentityKind kind,
        ulong firstIdentity,
        IEnumerable<ulong>? reserved = null,
        IEnumerable<ulong>? removed = null)
    {
        _kinds.Add(kind, new KindLedger(kind, new KindAllocatorState(kind, firstIdentity, (reserved ?? []).ToArray(), (removed ?? []).ToArray()).Validate()));
    }

    private DurableIdentityAllocator(IEnumerable<KindAllocatorState> kinds)
    {
        foreach (KindAllocatorState state in kinds)
        {
            state.Validate();
            if (!_kinds.TryAdd(state.Kind, new KindLedger(state.Kind, state)))
                throw new ArgumentException($"Durable identity state repeats kind '{state.Kind}'.", nameof(kinds));
        }
    }

    /// <summary>Recreates a ledger from persisted evidence, validating it before any identity is issued.</summary>
    public static DurableIdentityAllocator Restore(DurableIdentityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        return new DurableIdentityAllocator(state.Kinds);
    }

    /// <summary>Captures every kind's next issued identity, reservations, and tombstones for the product save owner.</summary>
    public DurableIdentityState CaptureState() => new(_kinds.Values
        .OrderBy(ledger => (int)ledger.Kind)
        .Select(ledger => new KindAllocatorState(
            ledger.Kind,
            ledger.NextIssued,
            ledger.Reserved.Order().ToArray(),
            ledger.Removed.Order().ToArray()))
        .ToArray());

    public ulong NextIdentity(DurableIdentityKind kind) => Require(kind).NextIssued;

    /// <summary>A copied view of the authored identities this allocator must never issue.</summary>
    public IReadOnlyCollection<ulong> ReservedIdentities(DurableIdentityKind kind) => Array.AsReadOnly(Require(kind).Reserved.Order().ToArray());

    /// <summary>A copied view of the tombstones recorded for this kind.</summary>
    public IReadOnlyCollection<ulong> RemovedIdentities(DurableIdentityKind kind) => Array.AsReadOnly(Require(kind).Removed.Order().ToArray());

    /// <summary>Issues the next unused identity and records it as live.</summary>
    public DurableIdentityReference Allocate(DurableIdentityKind kind) => new(kind, Require(kind).Allocate());

    /// <summary>Records a legitimate removal. The identity stays tombstoned and is never reissued.</summary>
    public void Remove(DurableIdentityReference reference)
    {
        reference.Validate();
        Require(reference.Kind).Remove(reference.Value);
    }

    /// <summary>Classifies a stored reference without materializing Engine state for it.</summary>
    public DurableIdentityClassification Classify(DurableIdentityReference reference)
    {
        if (!Enum.IsDefined(reference.Kind) || reference.Value == 0) return DurableIdentityClassification.WrongKind;
        return _kinds.TryGetValue(reference.Kind, out KindLedger? ledger)
            ? ledger.Classify(reference.Value)
            : DurableIdentityClassification.WrongKind;
    }

    private KindLedger Require(DurableIdentityKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        return _kinds.TryGetValue(kind, out KindLedger? ledger)
            ? ledger
            : throw new InvalidOperationException($"This durable identity ledger does not track kind '{kind}'.");
    }

    private sealed class KindLedger
    {
        private readonly HashSet<ulong> _reserved;
        private readonly HashSet<ulong> _removed;
        private ulong NextIdentity;

        internal KindLedger(DurableIdentityKind kind, KindAllocatorState state)
        {
            Kind = kind;
            NextIdentity = state.NextIdentity;
            _reserved = state.Reserved.ToHashSet();
            _removed = state.Removed.ToHashSet();
        }

        internal DurableIdentityKind Kind { get; }
        internal IReadOnlyCollection<ulong> Reserved => _reserved;
        internal IReadOnlyCollection<ulong> Removed => _removed;

        /// <summary>
        /// The identity the next allocation will issue. It is reported and persisted
        /// past reserved and removed identities so a restored ledger cannot describe a
        /// value it would never issue.
        /// </summary>
        internal ulong NextIssued
        {
            get
            {
                ulong value = NextIdentity;
                while (_reserved.Contains(value) || _removed.Contains(value)) value++;
                return value;
            }
        }

        internal ulong Allocate()
        {
            while (_reserved.Contains(NextIdentity) || _removed.Contains(NextIdentity))
            {
                if (NextIdentity == ulong.MaxValue)
                    throw new InvalidOperationException($"The {Kind} durable identity space is exhausted.");
                NextIdentity++;
            }

            if (NextIdentity == 0 || NextIdentity == ulong.MaxValue)
                throw new InvalidOperationException($"The {Kind} durable identity space is exhausted.");
            ulong allocated = NextIdentity++;
            // An issued identity joins the authored reservations: this set is the
            // ledger's record of everything it must never hand out again.
            _reserved.Add(allocated);
            return allocated;
        }

        internal void Remove(ulong value)
        {
            if (_removed.Contains(value)) return;
            if (!_reserved.Contains(value) || value >= NextIdentity)
            {
                throw new InvalidOperationException($"Durable {Kind} identity {value} was never issued by this allocator and cannot be removed.");
            }

            _reserved.Remove(value);
            _removed.Add(value);
        }

        internal DurableIdentityClassification Classify(ulong value)
        {
            // Membership is the record: a tombstone is removed, anything the ledger
            // must still account for is live, and an absent value was never issued.
            if (_removed.Contains(value)) return DurableIdentityClassification.Removed;
            return _reserved.Contains(value)
                ? DurableIdentityClassification.Live
                : DurableIdentityClassification.NeverIssued;
        }
    }
}
