using Rusty.Engine;
using Rusty.Engine.Mechanics;
using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Daggerfall's ordered HUD resource selection and wire projection.</summary>
internal sealed class DaggerfallHudProjection(IUiService ui, IReadOnlyList<DaggerfallHudResourceDefinition> resources, ResolvedCompositionIdentity? compositionIdentity, DaggerfallUiArt? uiArt = null) : IDisposable
{
    private readonly UiStream _hud = ui.OpenStream(new UiStreamRequest("dagger.hud", "dagger.ui.snapshot.v1"));
    private ulong _sequence;

    // The art a snapshot carries is worth a few hundred kilobytes, so it travels when it is new or
    // asked for rather than on every admitted update. The UI keeps the block and asks again with the
    // revision it holds when it does not have the one the snapshot names.
    private bool _artPending = true;

    /// <summary>Publishes the current UI art on the next snapshot because the DOM asked for it.</summary>
    internal void RequestArt() => _artPending = true;

    internal void Publish(
        PlayerActorState player,
        ProgressionState progression,
        PresentationState presentation,
        ProductMode mode,
        PlayerControlState controls,
        PresentationSlots slots,
        InventoryPresentation? inventory = null,
        LootPresentation? loot = null,
        CharacterSheetPresentation? character = null,
        DaggerfallPanelRequest? panelRequest = null,
        IReadOnlyList<SaveSlotSummary>? saveSlots = null,
        string? saveSlotDiagnostic = null,
        DaggerfallControlSettings? controlSettings = null,
        string? controlDiagnostic = null,
        DaggerfallActivationView? activation = null,
        DaggerfallQuestPresentation? quests = null)
    {
        UiValueBuilder builder = new();
        uint[] rows = resources.Select(resource => ResourceRow(builder, player, resource)).ToArray();
        (string Key, uint Value)[] fields =
        [
            ("resources", builder.Array(rows)),
            ("experience", builder.Number(progression.Experience)),
            ("lastOutcome", builder.String(presentation.LastOutcome)),
            // The mode is the product's, and the session is the one place that is told it, so the
            // projection that the thin UI renders carries it rather than the UI keeping one.
            ("mode", builder.String(Mode(mode))),
            // Compass and crosshair read the same authoritative look the camera does.
            ("view", builder.Object(
                ("yawRadians", builder.Number(controls.YawRadians)),
                ("pitchRadians", builder.Number(controls.PitchRadians)),
                ("interaction", builder.String(mode == ProductMode.Modal ? "modal" : mode == ProductMode.Playing ? "aiming" : "held")))),
            // Status rows come from the owners that publish them rather than from this projection
            // guessing what an effect, an escort or a quest wants to say.
            ("slots", builder.Array(slots.Read().Select(slot => builder.Object(
                ("owner", builder.String(slot.Owner)),
                ("id", builder.String(slot.Id)),
                ("label", builder.String(slot.Label)),
                ("detail", builder.String(slot.Detail)),
                ("order", builder.Number(slot.Order)))).ToArray())),
            // The modal's own token is what a close has to name, so the UI never invents focus.
            // Focus exists only where the mode lets the interaction act: a dead or paused product
            // ignores the close its own gate would refuse, so advertising one would offer the player
            // a control that silently does nothing.
            ("focus", loot is null || mode != ProductMode.Modal
                ? builder.Null()
                : builder.Object(
                    ("interaction", builder.String("loot")),
                    ("container", builder.String(loot.Container)),
                    ("revision", builder.String(loot.Revision)),
                    ("close", builder.String("loot-close")))),
            // A pad has no pointer and the DOM, not the product, owns whether a panel is open, so a
            // button that opens one asks for the DOM's own menu action. Publishing it with a revision
            // lets the DOM act on each request exactly once while the request itself stays visible.
            ("panelRequest", panelRequest is null
                ? builder.Null()
                : builder.Object(
                    ("panel", builder.String(panelRequest.Panel)),
                    ("revision", builder.String(panelRequest.Revision.ToString(CultureInfo.InvariantCulture))))),
            ("saveSlots", builder.Object(
                ("entries", builder.Array((saveSlots ?? []).Select(slot => builder.Object(
                    ("key", builder.String(slot.Key)),
                    ("label", builder.String(slot.Label)),
                    ("savedAtUtc", builder.String(slot.SavedAtUtc.ToString("O", CultureInfo.InvariantCulture))),
                    ("ruleset", builder.String(slot.Ruleset)))).ToArray())),
                ("diagnostic", saveSlotDiagnostic is null ? builder.Null() : builder.String(saveSlotDiagnostic)))),
        ];
        if (controlSettings is not null) fields = [.. fields, ("controls", builder.Object(
            ("diagnostic", builder.String(controlDiagnostic ?? "")),
            ("bindings", builder.Array(DaggerfallControlSettings.Catalog.Select(action => builder.Object(
                ("id", builder.String(action.Id)), ("category", builder.String(action.Category)),
                ("keys", builder.Array(controlSettings.KeysFor(action.Id).Select(builder.String).ToArray())),
                ("fixed", builder.Boolean(action.Id == "menu")))).ToArray()))))];
        if (activation is not null) fields = [.. fields, ("activation", builder.Object(("mode", builder.String(activation.Mode)), ("message", builder.String(activation.Message)), ("applied", builder.Boolean(activation.Applied))))];
        if (quests is not null) fields = [.. fields, ("quests", Quests(builder, quests))];
        if (inventory is not null) fields = [.. fields, ("inventory", Inventory(builder, inventory))];
        // Contents are an affordance the same way focus is: a dead or paused product refuses the take
        // its gate would otherwise honour, so the panel is published only in the mode that lets the
        // interaction act rather than offering buttons that silently do nothing.
        fields = [.. fields, ("loot", loot is null || mode != ProductMode.Modal ? builder.Null() : Loot(builder, loot))];
        if (character is not null) fields = [.. fields, ("character", Character(builder, character, mode == ProductMode.Title, mode == ProductMode.Playing))];
        if (compositionIdentity is not null)
            fields = [.. fields, ("composition", Composition(builder, compositionIdentity))];
        if (uiArt is not null)
        {
            // The revision is cheap and always present, so the DOM can tell whether the copy it holds
            // is the one this session shows.
            fields = [.. fields, ("uiArtRevision", builder.String(uiArt.Revision))];
            if (_artPending)
            {
                fields = [.. fields, ("uiArt", Art(builder, uiArt))];
                _artPending = false;
            }
        }

        uint root = builder.Object(fields);
        ui.PublishProjection(new UiProjection(_hud, ++_sequence, builder.Build(root)));
    }

    private static uint Quests(UiValueBuilder builder, DaggerfallQuestPresentation quests) => builder.Object(
        ("deliveries", builder.Array(quests.Deliveries.Select(message => QuestMessage(builder, message)).ToArray())),
        ("journal", builder.Array(quests.Journal.Select(message => QuestMessage(builder, message)).ToArray())),
        ("pending", quests.Pending is null ? builder.Null() : QuestMessage(builder, quests.Pending)));

    private static uint QuestMessage(UiValueBuilder builder, DaggerfallQuestRenderedMessage message) => builder.Object(
        ("instance", builder.String(message.InstanceId)),
        ("message", builder.Number(message.MessageId)),
        ("delivery", builder.String(message.Delivery.ToString().ToLowerInvariant())),
        ("text", builder.String(message.Text)),
        ("signoff", message.Signoff is null ? builder.Null() : builder.String(message.Signoff)),
        ("diagnostics", builder.Array(message.Diagnostics.Select(builder.String).ToArray())));

    private static uint Art(UiValueBuilder builder, DaggerfallUiArt art)
    {
        uint[] images = art.Images.Select(image => builder.Object(
            ("id", builder.String(image.Id)),
            ("image", builder.String(image.Image)))).ToArray();
        return builder.Object(("revision", builder.String(art.Revision)), ("images", builder.Array(images)));
    }

    private static uint Inventory(UiValueBuilder builder, InventoryPresentation value)
    {
        uint[] items = value.Items.Select(item => Item(builder, item)).ToArray();
        uint[] slots = value.Slots.Select(slot => builder.Object(
            ("id", builder.String(slot.Id)), ("label", builder.String(slot.Label)),
            ("itemKey", slot.ItemKey is null ? builder.Null() : builder.String(slot.ItemKey)))).ToArray();
        return builder.Object(("revision", builder.String(value.Revision)), ("message", builder.String(value.Message)),
            ("encumbrance", value.Encumbrance is { } encumbrance ? builder.Object(
                ("currentClassicUnits", builder.Number(encumbrance.CurrentClassicUnits)), ("maximumClassicUnits", builder.Number(encumbrance.MaximumClassicUnits)),
                ("canMove", builder.Boolean(encumbrance.CanMove))) : builder.Null()),
            ("currency", value.Currency is { } currency ? builder.Object(
                ("gold", builder.String(currency.Gold.ToString(CultureInfo.InvariantCulture))), ("lettersOfCredit", builder.String(currency.LettersOfCredit.ToString(CultureInfo.InvariantCulture))),
                ("accountGold", builder.String(currency.AccountGold.ToString(CultureInfo.InvariantCulture)))) : builder.Null()),
            ("equipmentChange", value.EquipmentChange is { } change ? builder.Object(
                ("cue", builder.String(change.Cue)), ("rightHandDelayMilliseconds", builder.Number(change.RightHandDelayMilliseconds)),
                ("leftHandDelayMilliseconds", builder.Number(change.LeftHandDelayMilliseconds))) : builder.Null()),
            ("items", builder.Array(items)), ("slots", builder.Array(slots)));
    }

    private static uint Item(UiValueBuilder builder, InventoryItemPresentation item) => builder.Object(
        ("key", builder.String(item.Key)), ("definition", builder.String(item.Definition)),
        ("label", builder.String(item.Label)), ("quantity", builder.String(item.Quantity)),
        ("weight", builder.Number(item.Weight)), ("value", builder.Number(item.Value)),
        ("details", builder.String(item.Details)), ("icon", item.Icon is null ? builder.Null() : builder.String(item.Icon)),
        ("gridSlot", item.GridSlot is int slot ? builder.Number(slot) : builder.Null()),
        ("equippedSlots", builder.Array(item.EquippedSlots.Select(builder.String).ToArray())),
        ("compatibleSlots", builder.Array(item.CompatibleSlots.Select(builder.String).ToArray())));

    private static uint Loot(UiValueBuilder builder, LootPresentation value) => builder.Object(
        ("container", builder.String(value.Container)), ("revision", builder.String(value.Revision)),
        ("title", builder.String(value.Title)), ("items", builder.Array(value.Items.Select(item => Item(builder, item)).ToArray())),
        ("message", builder.String(value.Message)));

    private static uint Character(UiValueBuilder builder, CharacterSheetPresentation value, bool creationAvailable, bool levelUpAvailable)
    {
        uint Stat(CharacterStatPresentation stat) => builder.Object(("id", builder.String(stat.Id)),
            ("label", builder.String(stat.Label)), ("value", builder.Number(stat.Value)), ("permanent", builder.Number(stat.Permanent)));
        uint creation = value.Creation is null ? builder.Null() : Creation(builder, value.Creation);
        uint identity = value.Identity is null ? builder.Null() : Identity(builder, value.Identity);
        return builder.Object(("name", builder.String(value.Name)),
            ("attributes", builder.Array(value.Attributes.Select(Stat).ToArray())),
            ("skills", builder.Array(value.Skills.Select(Stat).ToArray())),
            ("resources", builder.Array(value.Resources.Select(resource => builder.Object(
                ("id", builder.String(resource.Id)), ("label", builder.String(resource.Label)),
                ("current", builder.Number(resource.Current)), ("maximum", builder.Number(resource.Maximum)))).ToArray())),
            ("progression", builder.Object(("level", builder.Number(value.Progression.Level)), ("experience", builder.Number(value.Progression.Experience)))),
            ("equipment", builder.Array(value.Equipment.Select(item => builder.Object(("label", builder.String(item.Label)),
                ("slots", builder.Array(item.Slots.Select(builder.String).ToArray())), ("details", builder.String(item.Details)))).ToArray())),
            ("grantedSkills", builder.Array((value.GrantedSkills ?? []).Select(skill => builder.Object(
                ("id", builder.String(skill.SkillId)), ("tier", builder.String(skill.Tier.ToString().ToLowerInvariant())))).ToArray())),
            ("creationAvailable", builder.Boolean(creationAvailable)), ("creation", creation),
            ("levelUp", !levelUpAvailable || value.LevelUp is null ? builder.Null() : LevelUp(builder, value.LevelUp)),
            // The media the sheet draws from, so a consumer resolves published identities rather than
            // reconstructing a race's file names. An actor that declares no race publishes none.
            ("identity", identity));
    }

    private static uint LevelUp(UiValueBuilder builder, DaggerfallLevelUpPresentation levelUp) => builder.Object(
        ("level", builder.Number(levelUp.Level)), ("bonusPool", builder.Number(levelUp.BonusPool)),
        ("remainingPoints", builder.Number(levelUp.RemainingPoints)), ("healthGain", builder.Number(levelUp.HealthGain)),
        ("canCommit", builder.Boolean(levelUp.CanCommit)),
        ("attributes", builder.Array(levelUp.Attributes.Select(attribute => builder.Object(
            ("id", builder.String(attribute.Id)), ("label", builder.String(attribute.Label)),
            ("permanent", builder.Number(attribute.Permanent)), ("live", builder.Number(attribute.Live)),
            ("pending", builder.Number(attribute.Pending)), ("canAllocate", builder.Boolean(attribute.CanAllocate)))).ToArray())));

    private static uint Custom(UiValueBuilder builder, DaggerfallCustomCareerPresentation custom) => builder.Object(
        ("name", builder.String(custom.Current.Name)),
        ("primarySkills", builder.Array(custom.Current.PrimarySkills.Select(builder.String).ToArray())),
        ("majorSkills", builder.Array(custom.Current.MajorSkills.Select(builder.String).ToArray())),
        ("minorSkills", builder.Array(custom.Current.MinorSkills.Select(builder.String).ToArray())),
        ("hitPointsPerLevel", builder.Number(custom.Current.HitPointsPerLevel)),
        ("advantages", Traits(builder, custom.Current.Advantages)),
        ("disadvantages", Traits(builder, custom.Current.Disadvantages)),
        ("eligibility", builder.Array(custom.Eligibility.Select(builder.String).ToArray())),
        ("skills", builder.Array(custom.Skills.Select(builder.String).ToArray())),
        ("supportedAdvantages", builder.Array(custom.SupportedAdvantages.Select(builder.String).ToArray())),
        ("supportedDisadvantages", builder.Array(custom.SupportedDisadvantages.Select(builder.String).ToArray())));

    private static uint Creation(UiValueBuilder builder, DaggerfallCharacterCreationPresentation creation) => builder.Object(
        ("editing", builder.Boolean(creation.Editing)),
        ("current", builder.Object(("name", builder.String(creation.Current.Name)), ("race", builder.String(creation.Current.RaceId)),
            ("gender", builder.String(creation.Current.Gender == DaggerfallCharacterGender.Female ? "female" : "male")), ("faceIndex", builder.Number(creation.Current.FaceIndex)),
            ("reflexes", builder.Number((int)creation.Current.Reflexes)), ("career", builder.String(creation.Current.CareerId)))),
        ("races", Choices(builder, creation.Races)), ("careers", Choices(builder, creation.Careers)),
        ("faces", builder.Array(creation.Faces.Select(face => builder.Object(("index", builder.Number(face.Index)), ("mediaId", builder.String(face.MediaId)))).ToArray())),
        ("reflexes", builder.Array(creation.Reflexes.Select(reflex => builder.Object(("value", builder.Number(reflex.Value)), ("label", builder.String(reflex.Label)))).ToArray())),
        ("custom", creation.Custom is null ? builder.Null() : Custom(builder, creation.Custom)),
        ("background", creation.Background is null ? builder.Null() : Background(builder, creation.Background)));

    private static uint Background(UiValueBuilder builder, DaggerfallCharacterBackgroundPresentation background) => builder.Object(
        ("biographyClassIndex", builder.Number(background.BiographyClassIndex)),
        ("biography", builder.Array(background.Biography.Select(builder.String).ToArray())),
        ("attributeBonusPool", builder.Number(background.AttributeBonusPool)), ("remainingAttributePoints", builder.Number(background.RemainingAttributePoints)),
        ("primarySkillPoints", builder.Number(background.PrimarySkillPoints)), ("majorSkillPoints", builder.Number(background.MajorSkillPoints)), ("minorSkillPoints", builder.Number(background.MinorSkillPoints)),
        ("questions", builder.Array(background.Questions.Select(question => builder.Object(("number", builder.Number(question.Number)), ("text", builder.String(question.Text)),
            ("selectedLetter", question.SelectedLetter is null ? builder.Null() : builder.String(question.SelectedLetter)), ("answers", builder.Array(question.Answers.Select(answer => builder.Object(("letter", builder.String(answer.Letter)), ("text", builder.String(answer.Text)))).ToArray())))).ToArray())),
        ("attributes", builder.Array(background.Attributes.Select(attribute => builder.Object(("id", builder.String(attribute.Id)), ("label", builder.String(attribute.Label)),
            ("rolled", builder.Number(attribute.Rolled)), ("allocated", builder.Number(attribute.Allocated)), ("value", builder.Number(attribute.Value)), ("canAllocate", builder.Boolean(attribute.CanAllocate)))).ToArray())),
        ("skills", builder.Array(background.Skills.Select(skill => builder.Object(("id", builder.String(skill.Id)), ("tier", builder.String(skill.Tier)),
            ("rolled", builder.Number(skill.Rolled)), ("allocated", builder.Number(skill.Allocated)), ("biographyBonus", builder.Number(skill.BiographyBonus)), ("value", builder.Number(skill.Value)), ("canAllocate", builder.Boolean(skill.CanAllocate)))).ToArray())),
        ("startingGrants", builder.Array(background.StartingGrants.Select(grant => builder.Object(("itemId", builder.String(grant.ItemId)), ("templateIndex", builder.Number(grant.TemplateIndex)), ("quantity", builder.Number((long)grant.Quantity)), ("sourceEffect", builder.String(grant.SourceEffect)))).ToArray())),
        ("unsupportedEffects", builder.Array(background.UnsupportedEffects.Select(builder.String).ToArray())));

    private static uint Choices(UiValueBuilder builder, IEnumerable<DaggerfallCharacterChoice> choices) => builder.Array(choices.Select(choice => builder.Object(
        ("id", builder.String(choice.Id)), ("label", builder.String(choice.Label)), ("available", builder.Boolean(choice.Available)),
        ("restriction", choice.Restriction is null ? builder.Null() : builder.String(choice.Restriction)))).ToArray());

    private static uint Traits(UiValueBuilder builder, IEnumerable<DaggerfallCustomCareerTrait> traits) => builder.Array(traits
        .Select(trait => builder.Object(("id", builder.String(trait.Id)), ("target", trait.Target is null ? builder.Null() : builder.String(trait.Target)))).ToArray());

    private static uint Identity(UiValueBuilder builder, CharacterIdentityPresentation identity) => builder.Object(
        ("race", builder.String(identity.Race)), ("donorRaceId", builder.Number(identity.DonorRaceId)), ("portrait", builder.String(identity.Portrait)),
        ("gender", builder.String(identity.Gender)), ("faceIndex", builder.Number(identity.FaceIndex)), ("career", builder.String(identity.Career)),
        ("media", Media(builder, identity.Media)), ("selectedMedia", Media(builder, identity.SelectedMedia ?? [])));

    private static uint Media(UiValueBuilder builder, IEnumerable<CharacterMediaIdentity> media) => builder.Array(media
        .Select(value => builder.Object(("layer", builder.String(value.Layer)), ("mediaId", builder.String(value.MediaId)))).ToArray());

    private static uint Composition(UiValueBuilder builder, ResolvedCompositionIdentity identity) => builder.Object(
        ("bundle", builder.String(identity.Bundle.Value)),
        ("ruleset", builder.String(identity.Ruleset.Value)),
        ("contentPacks", builder.Array(identity.ContentPacks.Select(pack => builder.String(pack.Value)).ToArray())),
        ("tuning", builder.String(identity.Tuning.Value)));

    private uint ResourceRow(UiValueBuilder builder, PlayerActorState player, DaggerfallHudResourceDefinition resource)
    {
        Track value = player.Stats.GetTrack(TrackId.Parse(resource.Track.Value));
        return builder.Object(("id", builder.String(resource.Id)), ("label", builder.String(resource.Label)), ("current", builder.Number(value.ValueInt64)), ("maximum", builder.Number(value.Maximum.ValueInt64)));
    }


    /// <summary>The wire name of the entry-screen mode, stated once for the projection and its readers.</summary>
    internal const string TitleModeName = "title";

    /// <summary>The wire name of the mode the product decided, lowercased for a thin DOM consumer.</summary>
    private static string Mode(ProductMode mode) => mode switch
    {
        ProductMode.Title => TitleModeName,
        ProductMode.Playing => "playing",
        ProductMode.Paused => "paused",
        ProductMode.Modal => "modal",
        ProductMode.Dead => "dead",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), $"{mode} has no wire name."),
    };

    public void Dispose() => _hud.Dispose();
}
