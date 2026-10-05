using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestOfferSave(long Sequence, DaggerfallNpcSite Site, DaggerfallQuestInstanceSave Quest);
internal sealed record DaggerfallQuestOfferView(string Identity, string Text, IReadOnlyList<string> Diagnostics);

internal sealed partial class DaggerfallQuestInstances
{
    internal DaggerfallQuestOfferSave? PendingOffer { get; private set; }
    private long _offerSequence;
    private readonly HashSet<string> _acceptedOneTimeSources = new(StringComparer.Ordinal);

    private Func<DaggerfallQuestTaskOperation, bool>? _offerCapability;
    internal void BindOfferCapabilities(Func<DaggerfallQuestTaskOperation, bool> capability) => _offerCapability = capability;
    private bool OfferActionSupported(DaggerfallQuestTaskOperation operation)
    {
        if (operation.Kind == DaggerfallQuestTaskOperationKind.Unsupported) return false;
        try
        {
            if (operation.Kind is DaggerfallQuestTaskOperationKind.CastSpellDo or DaggerfallQuestTaskOperationKind.CastSpellOnFoe) _ = QuestSpell(operation);
            if (operation.Kind == DaggerfallQuestTaskOperationKind.CastEffectDo) _ = QuestEffect(operation.Targets[0]);
            return _offerCapability?.Invoke(operation) ?? true;
        }
        catch (NotSupportedException) { return false; }
    }

    internal bool ProviderHasActiveWork(long provider) => _instances.Values.Any(instance =>
        instance.Lifecycle == DaggerfallQuestLifecycle.Active && (instance.QuestorId == provider || instance.Resources.Any(resource => resource.IsQuestor && resource.Binding.ActorIds.Contains(provider))));

    internal DaggerfallQuestOfferSave PrepareWorkOffer(long provider, DaggerfallNpcSite site, int faction,
        bool member, int level, int reputation, int rank, DaggerfallCharacterGender providerGender, int day)
    {
        if (PendingOffer is { } existing)
            return existing.Quest.QuestorId == provider ? existing : throw new NotSupportedException("Answer the current provider offer first.");
        if (ProviderHasActiveWork(provider))
            throw new NotSupportedException("This provider already has an active quest.");
        var row = SelectOrdinaryWorkOffer(faction, member, level, reputation, rank, providerGender, day)
            ?? throw new NotSupportedException("No supported quest is available from this provider.");
        var source = _definitions.QuestSources.Resolve(row.Name + ".txt");
        long sequence = checked(++_offerSequence);
        var prepared = PrepareCore(new($"work:{sequence}", source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], [])
            { FactionId = faction, QuestorId = provider });
        return PendingOffer = new(sequence, site, prepared);
    }

    internal DaggerfallQuestOfferView? ReadOffer()
    {
        if (PendingOffer is not { } offer) return null;
        var rendered = RenderPreparedOffer(offer.Quest, 1000);
        return new(offer.Quest.InstanceId, rendered.Text, rendered.Diagnostics);
    }

    internal string AnswerOffer(string identity, bool accept, bool providerAvailable)
    {
        if (PendingOffer is not { } offer || offer.Quest.InstanceId != identity) return "That quest offer is no longer available.";
        if (!providerAvailable) { PendingOffer = null; return "The quest provider is no longer available."; }
        var text = RenderPreparedOffer(offer.Quest, accept ? 1002 : 1001);
        if (accept)
        {
            _admission?.RequireRunnable(offer.Quest.SourceFile);
            RegisterPrepared(offer.Quest);
            ShowMessage(identity, 1002);
        }
        PendingOffer = null;
        return text.Text;
    }
}

internal sealed record DaggerfallQuestWorkContact(long Npc, bool Castle, bool Eligible, bool Consumed);
internal sealed record DaggerfallQuestWorkPool(DaggerfallSiteIdSave? Site, long Visit, DaggerfallQuestWorkContact[] Contacts)
{
    internal static DaggerfallQuestWorkPool Empty { get; } = new(null, 0, []);
    internal void Validate()
    {
        Site?.Validate("quest work contact site");
        if (Visit < 0 || Contacts is null || Site is null && Contacts.Length > 0
            || Contacts.Any(contact => contact is null || contact.Npc <= 0)
            || Contacts.Select(contact => contact.Npc).Distinct().Count() != Contacts.Length)
            throw new ArgumentException("Quest work contact selection is malformed.");
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private DaggerfallQuestWorkPool _workPool = DaggerfallQuestWorkPool.Empty;
    internal void EnterOfferSite(DaggerfallSiteId site, bool enteredDungeon = false)
    {
        if (_workPool.Site?.Require() != site)
            _workPool = new(new(site.Region, site.Index), checked(_workPool.Visit + 1), []);
        else if (enteredDungeon)
            _workPool = _workPool with { Visit = checked(_workPool.Visit + 1), Contacts = [.. _workPool.Contacts.Where(contact => !contact.Castle)] };
    }
    internal bool WorkContactAvailable(long npc, DaggerfallSiteId site, bool castle, int chance)
    {
        EnterOfferSite(site);
        if (PendingOffer?.Quest.QuestorId == npc) return true;
        var contact = _workPool.Contacts.SingleOrDefault(contact => contact.Npc == npc);
        if (contact is null)
        {
            bool eligible = _random.DrawKeyed(new(0, "daggerfall.quest.work-contact", $"{site.Region}:{site.Index}:{_workPool.Visit}:{npc}", 1, 100)).Value <= chance;
            contact = new(npc, castle, eligible, false);
            _workPool = _workPool with { Contacts = [.. _workPool.Contacts, contact] };
        }
        return contact.Eligible && !contact.Consumed;
    }
    internal void ConsumeWorkContact(long npc) => _workPool = _workPool with {
        Contacts = [.. _workPool.Contacts.Select(contact => contact.Npc == npc ? contact with { Consumed = true } : contact)] };
}
