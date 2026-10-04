using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Quest-scoped NPC relations and HUD faces over the existing selected resource identities.</summary>
internal sealed partial class DaggerfallQuestInstances
{
    internal bool IsNpcMuted(long id) => ActiveNpcResources(id).Any(value => value.Resource.IsMuted);
    internal IReadOnlyList<DaggerfallQuestContact> QuestContacts(long id) => ActiveNpcResources(id)
        .Where(value => value.Resource.IsQuestor).Select(value => new DaggerfallQuestContact(value.Instance.InstanceId, value.Resource.Symbol)).ToArray();

    /// <summary>
    /// Reads the actual anyInfo/rumors message references carried by active NPC resources. This is
    /// deliberately derived from the normalized declaration and current binding, so a stale or
    /// unbound quest symbol cannot manufacture a dialogue option.
    /// </summary>
    internal IReadOnlyList<DaggerfallQuestDialogueTopic> DialogueTopics(long id) =>
        ActiveNpcResources(id)
            .SelectMany(value => DialogueTopics(value.Instance, value.Resource))
            .GroupBy(topic => topic.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(topic => topic.Label, StringComparer.Ordinal)
            .ThenBy(topic => topic.Id, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Answers one source-backed topic through the canonical quest message store.</summary>
    internal bool TryResolveDialogueTopic(long npcId, string topicId,
        out string? text, out IReadOnlyList<string> diagnostics)
    {
        text = null;
        diagnostics = [];
        DaggerfallQuestDialogueTopic? topic = DialogueTopics(npcId).SingleOrDefault(value => value.Id == topicId);
        if (topic is null || !_instances.TryGetValue(topic.InstanceId, out DaggerfallQuestRuntimeInstance? instance)
            || instance.Lifecycle != DaggerfallQuestLifecycle.Active)
            return false;
        DaggerfallQuestResourceState resource = instance.Resources.SingleOrDefault(value =>
            value.Symbol == topic.ResourceSymbol && value.Binding.ActorIds.Contains(npcId))!;
        if (resource is null) return false;

        if (topic.PublishesRumor)
        {
            (text, diagnostics) = Messages.PublishRumorAndRender(instance, topic.MessageId, _textContext(instance));
        }
        else
        {
            text = Messages.NoteText(instance, topic.MessageId, _textContext(instance));
        }
        return true;
    }

    private IEnumerable<DaggerfallQuestDialogueTopic> DialogueTopics(
        DaggerfallQuestRuntimeInstance instance, DaggerfallQuestResourceState resource)
    {
        DaggerfallQuestResourceDefinition? declaration = _definitions.QuestSources.Resources.SingleOrDefault(value =>
            value.SourceFile == instance.SourceFile && value.CanonicalId == resource.Symbol);
        if (declaration is null) yield break;
        DaggerfallQuestSourceDefinition source = _definitions.QuestSources.Resolve(instance.SourceFile);
        string subject = resource.Text?.Name ?? resource.SelectedPerson?.DisplayName
            ?? declaration.Person?.Named?.Replace('_', ' ')
            ?? declaration.TargetSourceSpelling?.Replace('_', ' ')
            ?? declaration.CanonicalId;

        string? anyInfoReference = declaration.Item?.AnyInfoMessage ?? MessageParameter(declaration, "anyInfo");
        if (TryResolveMessage(instance, source, anyInfoReference, out int info))
            yield return new($"quest-info:{Uri.EscapeDataString(instance.InstanceId)}:{Uri.EscapeDataString(resource.Symbol)}",
                $"Ask about {subject}", instance.InstanceId, resource.Symbol, info, PublishesRumor: false);

        if (TryResolveMessage(instance, source, MessageParameter(declaration, "rumors"), out int rumorId))
            yield return new($"quest-rumor:{Uri.EscapeDataString(instance.InstanceId)}:{Uri.EscapeDataString(resource.Symbol)}",
                $"Ask for rumors about {subject}", instance.InstanceId, resource.Symbol, rumorId, PublishesRumor: true);
    }

    private bool TryResolveMessage(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestSourceDefinition source,
        string? reference, out int messageId)
    {
        messageId = 0;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        int? direct = int.TryParse(reference, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
        if (direct is int value && !source.Messages.Any(message => message.Id == value)) return false;
        try
        {
            return Messages.TryResolveMessage(instance, direct, direct is null ? reference : null,
                out messageId, out _);
        }
        catch (ArgumentException)
        {
            // A stale or unsupported static alias is not a topic the live NPC can answer.
            return false;
        }
    }

    private static string? MessageParameter(DaggerfallQuestResourceDefinition declaration, string key)
    {
        for (int index = 0; index + 1 < declaration.Parameters.Count; index++)
            if (string.Equals(declaration.Parameters[index], key, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(declaration.Parameters[index + 1]))
                return declaration.Parameters[index + 1];
        return null;
    }

    private IEnumerable<(DaggerfallQuestRuntimeInstance Instance, DaggerfallQuestResourceState Resource)> ActiveNpcResources(long id) =>
        _instances.Values.Where(instance => instance.Lifecycle == DaggerfallQuestLifecycle.Active)
            .SelectMany(instance => instance.Resources.Where(resource => resource.SelectedPerson is not null && resource.Binding.ActorIds.Contains(id))
                .Select(resource => (instance, resource)));
    // The classic HUD shows three escort portraits; all remaining overlays stay durable.
    private DaggerfallQuestEscortFace[] EscortFaces() => _instances.Values.Where(instance => instance.Lifecycle == DaggerfallQuestLifecycle.Active)
        .SelectMany(instance => instance.Resources.Where(resource => resource.EscortFaceMedia is not null)
            .Select(resource => (instance, resource))).OrderBy(value => value.resource.EscortFaceOrder).Take(3)
        .Select(value => new DaggerfallQuestEscortFace(value.instance.InstanceId, value.resource.Symbol,
            value.resource.SelectedPerson?.DisplayName ?? value.resource.Text?.Name ?? value.resource.Symbol, value.resource.EscortFaceMedia!)).ToArray();

    void IDaggerfallQuestTaskLifecycle.RearmMute(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        var resource = OverlayResource(instance, operation);
        SetResource(instance.InstanceId, resource with { IsMuted = false });
    }

    void IDaggerfallQuestTaskLifecycle.NpcOverlay(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        var resource = OverlayResource(instance, operation);
        switch (operation.Kind)
        {
            case DaggerfallQuestTaskOperationKind.AddQuestor:
                SetResource(instance.InstanceId, resource with { IsQuestor = true }); break;
            case DaggerfallQuestTaskOperationKind.DropQuestor:
                SetResource(instance.InstanceId, resource with { IsQuestor = false }); break;
            case DaggerfallQuestTaskOperationKind.MuteNpc:
                SetResource(instance.InstanceId, resource with { IsMuted = true }); break;
            case DaggerfallQuestTaskOperationKind.DropFace:
                SetResource(instance.InstanceId, resource with { EscortFaceMedia = null, EscortFaceOrder = 0 }); break;
            case DaggerfallQuestTaskOperationKind.AddFace:
                // Resolve before publishing optional speech, so invalid media cannot leave half an action.
                string media = resource.EscortFaceMedia ?? ResolveEscortFace(instance, resource);
                if (operation.MessageId is > 0 and var saying) Messages.Popup(instance, saying);
                SetResource(instance.InstanceId, resource with { EscortFaceMedia = media, EscortFaceOrder = resource.EscortFaceOrder > 0 ? resource.EscortFaceOrder
                    : checked(_instances.Values.SelectMany(value => value.Resources).Select(value => value.EscortFaceOrder).DefaultIfEmpty().Max() + 1) }); break;
            default: throw new ArgumentException($"'{operation.Kind}' is not an NPC overlay action.");
        }
    }

    private static DaggerfallQuestResourceState OverlayResource(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        string symbol = DaggerfallQuestInstanceSave.Canonical(operation.Targets[0], "NPC overlay");
        var resource = instance.Resources.SingleOrDefault(value => value.Symbol == symbol)
            ?? throw new ArgumentException($"Quest NPC overlay names missing resource '{symbol}'.");
        bool foe = operation.Targets.Length > 1 && operation.Targets[1] == "foe";
        if (foe ? resource.SelectedFoe is null : resource.SelectedPerson is null)
            throw new ArgumentException($"Quest NPC overlay '{symbol}' has the wrong selected resource kind.");
        return resource;
    }

    private string ResolveEscortFace(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestResourceState resource)
    {
        string key = instance.InstanceId + "/" + resource.Symbol + "/escort-face";
        int Draw(int low, int high) => checked((int)_random.DrawKeyed(new(0, "daggerfall.quest.escort-face", key, low, high)).Value);
        string[] eligible = EligibleEscortFaces(_definitions, resource).ToArray();
        if (eligible.Length == 0) throw new NotSupportedException($"Quest escort '{resource.Symbol}' has no published portrait for its selected meaning.");
        return eligible[eligible.Length == 1 ? 0 : Draw(0, eligible.Length - 1)];
    }

    internal static IEnumerable<string> EligibleEscortFaces(DaggerfallDefinitions definitions, DaggerfallQuestResourceState resource)
    {
        var presentation = definitions.CharacterPresentation;
        var person = resource.SelectedPerson;
        var gender = (person?.Gender == "Female" || resource.SelectedFoe?.Female == true)
            ? DaggerfallCharacterGender.Female : DaggerfallCharacterGender.Male;
        if (person?.Individual == true && definitions.Factions.Factions.TryGetValue(person.FactionId, out var faction) && faction.Face is >= 0 and <= 60)
            return presentation.FactionFaces.Where(value => value.Index == faction.Face).Select(value => value.MediaId);
        if (person?.FactionId == 514) // Classic Children faction; portrait has two variants per gender.
            return presentation.ChildFaces.Where(value => value.Index is >= 0 and <= 3 && value.Index % 2 == (gender == DaggerfallCharacterGender.Female ? 1 : 0))
                .OrderBy(value => value.Index).Select(value => value.MediaId);
        string race = person?.Race == "redguard" ? "redguard" : "breton";
        return presentation.RequireRace(race).Heads(gender).Where(value => person is null ? value.HeadIndex is >= 0 and <= 9 : value.HeadIndex == person.HudFace)
            .Select(value => value.MediaId);
    }
}
