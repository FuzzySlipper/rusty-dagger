namespace WorldRpg.Kit.World;

/// <summary>What a durable reference currently denotes, without resolving it to runtime state.</summary>
public enum DurableIdentityClassification
{
    /// <summary>The identity is allocated and currently belongs to this world.</summary>
    Live,

    /// <summary>The identity was allocated and has since been removed. Removal is not absence.</summary>
    Removed,

    /// <summary>This ledger has no record of the identity: nothing issued it and nothing reserved it.</summary>
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

/// <summary>
/// Persisted allocator evidence for one kind of durable world object.
/// <paramref name="NextIdentity"/> is the progress marker one past the last issued
/// identity, or zero when the kind has issued every identity it can.
/// </summary>
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
            // The exhaustion marker is not a boundary: a kind that has issued every
            // identity it can still records the ones it has since removed.
            if (NextIdentity != 0 && value >= NextIdentity)
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
/// monotonic: an issued identity is never handed out again, and the cursor only
/// moves forward. Removal records a tombstone for an identity the allocator issued,
/// which keeps "removed" distinguishable from "never loaded" for as long as the save
/// keeps the tombstone.
///
/// Classification is answered from what the ledger records — tombstones and
/// reservations — rather than from the cursor, because a reservation may sit above
/// the cursor (a site load claims its content identities before an allocation
/// reaches them) and an older save may not have listed every issued identity. The
/// cursor's meaning is narrowed to two facts: an exhausted kind has no next
/// identity, and an identity the cursor never passed was never issued.
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
        if (state.Kinds.Length == 0)
            throw new ArgumentException("Durable identity state must carry at least one kind.", nameof(state));
        return new DurableIdentityAllocator(state.Kinds);
    }

    /// <summary>Captures every kind's progress marker, reservations, and tombstones for the product save owner.</summary>
    public DurableIdentityState CaptureState() => new(_kinds.Values
        .OrderBy(ledger => (int)ledger.Kind)
        .Select(ledger => new KindAllocatorState(
            ledger.Kind,
            ledger.PersistedCursor,
            ledger.Reserved.Order().ToArray(),
            ledger.Removed.Order().ToArray()))
        .ToArray());

    public ulong NextIdentity(DurableIdentityKind kind) => Require(kind).NextIssued;

    /// <summary>A copied view of every identity this allocator must never issue again: authored reservations and the identities it issued.</summary>
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
        private ulong _cursor;
        private bool _exhausted;

        internal KindLedger(DurableIdentityKind kind, KindAllocatorState state)
        {
            Kind = kind;
            _reserved = state.Reserved.ToHashSet();
            _removed = state.Removed.ToHashSet();
            // The marker is authoritative: a kind that reports no next identity stays
            // exhausted, and one that reports a cursor keeps the identity before it as
            // the last issued, which is where the search for the next one starts.
            _exhausted = state.NextIdentity == ExhaustedCursor;
            _cursor = _exhausted ? 0 : state.NextIdentity - 1;
            if (!_exhausted && Scan() == 0) _exhausted = true;
        }

        /// <summary>
        /// The persisted marker for a kind that has issued every identity it can. Zero
        /// is never an identity and never a cursor, so it is free to mean "nothing left".
        /// </summary>
        internal const ulong ExhaustedCursor = 0;

        internal DurableIdentityKind Kind { get; }
        internal IReadOnlyCollection<ulong> Reserved => _reserved;
        internal IReadOnlyCollection<ulong> Removed => _removed;

        /// <summary>
        /// The persisted progress marker: one past the last issued identity, or the
        /// exhaustion marker. It is deliberately not the identity the next allocation
        /// will issue — the next identity may be higher, because reserved and tombstoned
        /// identities are skipped — and it may therefore sit on a reservation. Moving it
        /// forward to the next issuable identity instead would lose the boundary between
        /// an identity that was issued and a reservation below it, and a restored ledger
        /// would be free to issue that identity a second time.
        /// </summary>
        internal ulong PersistedCursor => _exhausted ? ExhaustedCursor : _cursor + 1;

        /// <summary>
        /// The identity the next allocation will issue. Reserved and tombstoned
        /// identities are skipped, and an exhausted kind reports none rather than
        /// advertising a value that allocation would refuse. Peeking never moves the
        /// cursor; see <see cref="PersistedCursor"/> for what is saved.
        /// </summary>
        internal ulong NextIssued => !_exhausted && Scan() is ulong value && value != 0
            ? value
            : throw new InvalidOperationException($"The {Kind} durable identity space is exhausted.");

        internal ulong Allocate()
        {
            ulong allocated = Scan();
            if (allocated == 0 || _exhausted)
                throw new InvalidOperationException($"The {Kind} durable identity space is exhausted.");
            _cursor = allocated;
            if (Scan() == 0) _exhausted = true;
            // An issued identity joins the authored reservations: this set is the
            // ledger's record of everything it must never hand out again.
            _reserved.Add(allocated);
            return allocated;
        }

        /// <summary>
        /// Records a tombstone for an identity this allocator issued. An authored
        /// reservation is refused: it was never issued, so tombstoning it would report
        /// content as removed. A restored ledger can only enforce this for reservations
        /// above its progress marker; below it, an authored reservation is
        /// indistinguishable from an issued identity, and the caller that still knows
        /// which identities the selected content claims owns that distinction.
        /// </summary>
        internal void Remove(ulong value)
        {
            if (_removed.Contains(value)) return;
            if (!_reserved.Contains(value) || value > _cursor)
            {
                throw new InvalidOperationException($"Durable {Kind} identity {value} was not issued by this allocator and cannot be removed.");
            }

            _reserved.Remove(value);
            _removed.Add(value);
        }

        internal DurableIdentityClassification Classify(ulong value)
        {
            // Membership is the record: a tombstone is removed, and anything the
            // ledger must still account for is live. A value the cursor has passed is
            // accepted as live even when the caller supplies a reservation set that
            // omits it, because an older save may not have listed every issued
            // identity; only a value the cursor never passed was provably unissued.
            if (_removed.Contains(value)) return DurableIdentityClassification.Removed;
            if (_reserved.Contains(value)) return DurableIdentityClassification.Live;
            return value <= _cursor
                ? DurableIdentityClassification.Live
                : DurableIdentityClassification.NeverIssued;
        }

        /// <summary>
        /// Finds the first free identity above the cursor. The last valid identity is
        /// issued in full; only after it is taken does the search report exhaustion.
        /// </summary>
        private ulong Scan()
        {
            ulong value = _cursor == ulong.MaxValue ? 0 : _cursor + 1;
            while (value != 0 && !IsFree(value))
            {
                if (value == ulong.MaxValue) return 0;
                value++;
            }

            return value;
        }

        private bool IsFree(ulong value) => value != 0 && !_reserved.Contains(value) && !_removed.Contains(value);
    }
}
