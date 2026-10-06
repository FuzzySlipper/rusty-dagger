using Rusty.Engine;
using Rusty.Engine.Mechanics;
using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Everything one HUD snapshot projects, gathered by the session from the owners that hold it.</summary>
internal sealed record DaggerfallHudFrame(
    PlayerActorState Player,
    ProgressionState Progression,
    PresentationState Presentation,
    ProductMode Mode,
    PlayerControlState Controls,
    PresentationSlots Slots,
    InventoryPresentation? Inventory = null,
    LootPresentation? Loot = null,
    CharacterSheetPresentation? Character = null,
    DaggerfallPanelRequest? PanelRequest = null,
    IReadOnlyList<SaveSlotSummary>? SaveSlots = null,
    string? SaveSlotDiagnostic = null,
    DaggerfallControlSettings? ControlSettings = null,
    string? ControlDiagnostic = null,
    DaggerfallActivationView? Activation = null,
    DaggerfallQuestPresentation? Quests = null,
    DaggerfallNotebookPresentation? Notebook = null,
    DaggerfallTransportPresentation? Transport = null,
    DaggerfallDungeonTextProjection? DungeonText = null,
    DaggerfallDeathView? Death = null,
    DaggerfallRestView? Rest = null,
    DaggerfallTravelPresentation? Travel = null,
    string? SiteName = null,
    DaggerfallLodgingView? Lodging = null,
    DaggerfallMapPresentation? Map = null, DaggerfallDispelView? Dispel = null, DaggerfallIdentifyView? Identify=null, IReadOnlyList<DaggerfallDetectorView>? Detectors = null, DaggerfallSpellbookView? Spells=null, bool CharacterCreationAvailable = true, DaggerfallPropertyView? Property = null, DaggerfallTeleportView? Teleport = null, DaggerfallCreateItemView? CreateItem = null, DaggerfallLegalView? Legal = null);

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

    internal void Publish(DaggerfallHudFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var (player, progression, presentation, mode, controls, slots, inventory, loot, character, panelRequest,
            saveSlots, saveSlotDiagnostic, controlSettings, controlDiagnostic, activation, quests, notebook, transport,
            dungeonText, death, rest, travel, siteName, lodging, map, dispel, identifyView, detectors, spells, _, _, _, _, _) = frame;
        UiValueBuilder builder = new();
        uint[] rows = resources.Select(resource => ResourceRow(builder, player, resource)).ToArray();
        (string Key, uint Value)[] fields =
        [
            ("legal", frame.Legal is not { } legal ? builder.Null() : builder.Object(
                ("revision", builder.String(legal.Revision)), ("phase", builder.String(legal.Phase)),
                ("title", builder.String(legal.Title)), ("message", builder.String(legal.Message)),
                ("charges", builder.Array(legal.Charges.Select(builder.String).ToArray())),
                ("choices", builder.Array(legal.Choices.Select(choice => builder.Object(("id", builder.String(choice.Id)), ("label", builder.String(choice.Label)))).ToArray())))),
            ("dispel", dispel is null ? builder.Null() : builder.Object(("revision", builder.String(dispel.Revision)),
                ("options", builder.Array(dispel.Options.Select(option => builder.Object(("id", builder.String(option.Id)), ("label", builder.String(option.Label)))).ToArray())))),
            ("detectors", builder.Array((detectors ?? []).Select(source => builder.Object(
                ("source", builder.String(source.Source)), ("kind", builder.String(source.Kind)),
                ("contacts", builder.Array(source.Contacts.Select(contact => builder.Object(
                    ("kind", builder.String(contact.Kind)), ("id", builder.String(contact.Id)), ("label", builder.String(contact.Label)),
                    ("distance", builder.Number(contact.Distance)), ("bearingRadians", builder.Number(contact.BearingRadians)),
                    ("items", builder.Array(contact.Items.Select(item => builder.Object(("id", builder.String(item.Id)),
                        ("definition", builder.String(item.Definition)), ("quantity", builder.Number(checked((long)item.Quantity))))).ToArray())))).ToArray())))).ToArray())),
            ("identify", frame.Identify is not { } identify ? builder.Null() : builder.Object(
                ("revision",builder.String(identify.Revision)),("cost",builder.Number(identify.Cost)),
                ("options",builder.Array(identify.Options.Select(option=>builder.Object(("id",builder.String(option.Id)),("label",builder.String(option.Label)))).ToArray())))),
            ("spells",frame.Spells is null ? builder.Null() : builder.Object(
                ("available",builder.Array(frame.Spells.Available.Select(spell=>builder.Object(("key",builder.String(spell.Key)),
                    ("name",builder.String(spell.Name)),("cost",builder.Number(spell.Cost)),("canCast",builder.Boolean(spell.CanCast)))).ToArray())),
                ("ready",frame.Spells.Ready is null ? builder.Null() : builder.String(frame.Spells.Ready)),
                ("result",builder.String(frame.Spells.Result)),
                ("sale", frame.Spells.Sale is not { } sale ? builder.Null() : builder.Object(
                    ("revision", builder.String(sale.Revision)), ("provider", builder.String(sale.Provider)),
                    ("offers", builder.Array(sale.Offers.Select(offer => builder.Object(
                        ("key", builder.String(offer.Key)), ("name", builder.String(offer.Name)),
                        ("castingCost", builder.Number(offer.CastingCost)), ("price", builder.Number(checked((long)offer.Price))),
                        ("known", builder.Boolean(offer.Known)))).ToArray())))),
                ("summoning", frame.Spells.Summoning is not { } summon ? builder.Null() : builder.Object(
                    ("revision",builder.String(summon.Revision)),("provider",builder.String(summon.Provider)),
                    ("offerRevision",summon.OfferRevision is null ? builder.Null() : builder.String(summon.OfferRevision)),
                    ("prince",summon.Prince is null ? builder.Null() : builder.String(summon.Prince)),
                    ("message",summon.Message is null ? builder.Null() : builder.String(summon.Message)),
                    ("diagnostics",builder.Array(summon.Diagnostics.Select(builder.String).ToArray())),
                    ("quote",summon.Quote is not { } quote ? builder.Null() : builder.Object(
                        ("key",builder.String(quote.Key)),("name",builder.String(quote.Name)),("quest",builder.String(quote.Quest)),
                        ("gold",builder.Number(checked((long)quote.Gold))),("eligible",builder.Boolean(quote.Eligible)),
                        ("reason",quote.Reason is null ? builder.Null() : builder.String(DaggerfallSession.SummoningOutcomeText(quote.Reason))))))),
                ("maker", frame.Spells.Maker is not { } spellMaker ? builder.Null() : SpellMaker(builder, spellMaker)),
                ("itemMaker", frame.Spells.ItemMaker is not { } itemMaker ? builder.Null() : ItemMaker(builder, itemMaker)),
                ("potionMaker", frame.Spells.PotionMaker is not { } potionMaker ? builder.Null() : PotionMaker(builder, potionMaker)),
                ("information", frame.Spells.Information is not { } information ? builder.Null() : builder.Object(
                    ("key", builder.String(information.Key)), ("name", builder.String(information.Name)),
                    ("element", builder.String(information.Element)), ("target", builder.String(information.Target)),
                    ("details", builder.Array(information.Details.Select(builder.String).ToArray())))))),
            ("resources", builder.Array(rows)),
            ("experience", builder.Number(progression.Experience)),
            ("lastOutcome", builder.String(presentation.LastOutcome)),
            // The mode is the product's, and the session is the one place that is told it, so the
            // projection that the thin UI renders carries it rather than the UI keeping one.
            ("mode", builder.String(Mode(mode))),
            // The HUD names where the player is from the site the session projects, not from a label
            // the DOM carries, so every site a bundle starts at or moves to names itself.
            ("site", siteName is null ? builder.Null() : builder.Object(("name", builder.String(siteName)))),
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
        if (activation is not null) fields = [.. fields, ("activation", builder.Object(
            ("mode", builder.String(activation.Mode)),
            ("message", builder.String(activation.Message)),
            ("applied", builder.Boolean(activation.Applied)),
            ("dialogue", activation.Dialogue is null ? builder.Null() : Dialogue(builder, activation.Dialogue))))];
        if (quests is not null) fields = [.. fields, ("quests", Quests(builder, quests))];
        if (notebook is not null) fields = [.. fields, ("notebook", Notebook(builder, notebook))];
        if (frame.CreateItem is { } createItem) fields = [.. fields, ("createItem", builder.Object(("revision", builder.String(createItem.Revision)),
            ("options", builder.Array(createItem.Options.Select(option => builder.Object(("id", builder.String(option.Id)), ("label", builder.String(option.Label)))).ToArray()))))];
        if (frame.Teleport is { } teleport) fields = [.. fields, ("teleport", builder.Object(("revision", builder.String(teleport.Revision)), ("anchorSet", builder.Boolean(teleport.AnchorSet))))];
        if (frame.Property is { } property) fields = [.. fields, ("property", Property(builder, property))];
        if (transport is not null) fields = [.. fields, ("transport", Transport(builder, transport))];
        fields = [.. fields, ("dungeonText", dungeonText is null ? builder.Null() : builder.Object(
            ("actionId", builder.String(dungeonText.ActionId)),
            ("kind", builder.String(dungeonText.Kind.ToString())),
            ("text", builder.String(dungeonText.Text)),
            ("revision", builder.String(dungeonText.Revision.ToString(CultureInfo.InvariantCulture))),
            ("requiresAnswer", builder.Boolean(dungeonText.Kind == DaggerfallDungeonTextActionKind.ShowTextWithInput))))];
        fields = [.. fields, ("death", death is null ? builder.Null() : Death(builder, death))];
        fields = [.. fields, ("rest", rest is null ? builder.Null() : Rest(builder, rest))];
        fields = [.. fields, ("lodging", lodging is null ? builder.Null() : builder.Object(
            ("key", builder.String(lodging.Key)), ("name", builder.String(lodging.Name)),
            ("days", builder.Number(lodging.Days)), ("price", builder.Number(lodging.Price)),
            ("remainingHours", builder.Number(lodging.RemainingHours)), ("canBook", builder.Boolean(lodging.CanBook)),
            ("maximumDays", builder.Number(DaggerfallLodgingState.MaximumDays))))];
        fields = [.. fields, ("map", map is null ? builder.Null() : DaggerfallMapProjection.Wire(builder, map))];
        if (travel is not null) fields = [.. fields, ("travel", Travel(builder, travel))];
        if (inventory is not null) fields = [.. fields, ("inventory", Inventory(builder, inventory))];
        // Contents are an affordance the same way focus is: a dead or paused product refuses the take
        // its gate would otherwise honour, so the panel is published only in the mode that lets the
        // interaction act rather than offering buttons that silently do nothing.
        fields = [.. fields, ("loot", loot is null || mode != ProductMode.Modal ? builder.Null() : Loot(builder, loot))];
        if (character is not null) fields = [.. fields, ("character", Character(builder, character, mode == ProductMode.Title && frame.CharacterCreationAvailable, mode == ProductMode.Playing))];
        if (compositionIdentity is not null)
            fields = [.. fields, ("composition", Composition(builder, compositionIdentity))];
        if (uiArt is not null)
        {
            // The revision is cheap and always present, so the DOM can tell whether the copy it holds
            // is the one this session shows.
            fields = [.. fields, ("uiArtRevision", builder.String(uiArt.Revision)), ("pickScreens", builder.Array(uiArt.PickScreens.Select(builder.String).ToArray()))];
            if (_artPending)
            {
                fields = [.. fields, ("uiArt", Art(builder, uiArt))];
                _artPending = false;
            }
        }

        uint root = builder.Object(fields);
        ui.PublishProjection(new UiProjection(_hud, ++_sequence, builder.Build(root)));
    }

    private static uint Travel(UiValueBuilder builder, DaggerfallTravelPresentation travel) => builder.Object(
        ("destinations", builder.Array(travel.Destinations.Select(destination => builder.Object(
            ("region", builder.Number(destination.Id.Region)),
            ("index", builder.Number(destination.Id.Index)),
            ("name", builder.String(destination.Name)),
            ("kind", builder.String(DaggerfallSiteKinds.Label(destination.Kind))),
            ("regionName", builder.String(travel.RegionName(destination.Id.Region))))).ToArray())),
        ("message", travel.Message is null ? builder.Null() : builder.String(travel.Message)),
        ("executionAvailable", builder.Boolean(travel.ExecutionAvailable)),
        ("lastResult", travel.LastResult is null ? builder.Null() : builder.Object(
            ("paidGold", builder.Number(travel.LastResult.PaidGold)),
            ("elapsedSeconds", builder.Number(travel.LastResult.ElapsedSeconds)),
            ("actualRegion", builder.Number(travel.LastResult.ActualSite.Region)),
            ("actualIndex", builder.Number(travel.LastResult.ActualSite.Index)),
            ("actualX", builder.Number(travel.LastResult.ActualPixel.X)),
            ("actualY", builder.Number(travel.LastResult.ActualPixel.Y)),
            ("message", builder.String(travel.LastResult.Message)))),
        ("quote", travel.Quote is null ? builder.Null() : builder.Object(
            ("identity", builder.String(travel.Quote.Identity)),
            ("destination", builder.String(travel.Quote.Destination.Name)),
            ("minutes", builder.Number(travel.Quote.TravelMinutes)),
            ("duration", builder.String(DaggerfallCalendar.DescribeDuration(checked((long)travel.Quote.TravelMinutes * DaggerfallCalendar.SecondsPerMinute)))),
            ("distance", builder.Number(travel.Quote.DistanceMapPixels)),
            ("oceanPixels", builder.Number(travel.Quote.OceanPixels)),
            ("innCost", builder.Number(travel.Quote.InnCost)),
            ("shipCost", builder.Number(travel.Quote.ShipCost)),
            ("totalCost", builder.Number(travel.Quote.TotalCost)),
            ("options", builder.Object(
                ("cautious", builder.Boolean(travel.Quote.Options.SpeedCautious)),
                ("inn", builder.Boolean(travel.Quote.Options.SleepModeInn)),
                ("ship", builder.Boolean(travel.Quote.Options.TravelShip)),
                ("hasHorse", builder.Boolean(travel.Quote.Options.HasHorse)),
                ("hasCart", builder.Boolean(travel.Quote.Options.HasCart)),
                ("hasShip", builder.Boolean(travel.Quote.Options.HasShip)),
                ("availableGold", builder.String(travel.Quote.Options.AvailableGold.ToString(CultureInfo.InvariantCulture))),
                ("availableGoldPieces", builder.String(travel.Quote.Options.AvailableGoldPieces.ToString(CultureInfo.InvariantCulture))))),
            ("canAfford", builder.Boolean(travel.Quote.CanAfford)))));

    private static uint Property(UiValueBuilder builder, DaggerfallPropertyView property) => builder.Object(
        ("bankAvailable", builder.Boolean(property.BankAvailable)),
        ("offers", builder.Array(property.Offers.Select(offer => builder.Object(
            ("key", builder.String(offer.Key)), ("name", builder.String(offer.Name)),
            ("price", builder.String(offer.Price)), ("salePrice", builder.String(offer.SalePrice)),
            ("owned", builder.Boolean(offer.Owned)), ("canBuy", builder.Boolean(offer.CanBuy)),
            ("canSell", builder.Boolean(offer.CanSell)), ("canEnter", builder.Boolean(offer.CanEnter)),
            ("enterable", builder.Boolean(offer.Enterable)))).ToArray())),
        ("storage", property.Storage is not { } storage ? builder.Null() : builder.Object(
            ("key", builder.String(storage.Key)), ("revision", builder.String(storage.Revision)),
            ("items", builder.Array(storage.Items.Select(item => builder.Object(
                ("key", builder.String(item.Key)), ("definition", builder.String(item.Definition)),
                ("label", builder.String(item.Label)), ("quantity", builder.String(item.Quantity)))).ToArray())))));

    private static uint Transport(UiValueBuilder builder, DaggerfallTransportPresentation transport) => builder.Object(
        ("mode", builder.String(transport.Mode.ToString().ToLowerInvariant())),
        ("onShip", builder.Boolean(transport.OnShip)),
        ("canRun", builder.Boolean(transport.CanRun)),
        ("summary", builder.String(transport.Summary)),
        ("travelModifier", builder.Number(transport.TravelModifier)),
        ("oceanMinutesPerMapPixel", builder.Number(transport.OceanMinutesPerMapPixel)),
        ("options", builder.Array(transport.Options.Select(option => builder.Object(
            ("id", builder.String(option.Id)),
            ("mode", builder.String(option.Mode.ToString().ToLowerInvariant())),
            ("available", builder.Boolean(option.Available)),
            ("selected", builder.Boolean(option.Selected)),
            ("label", builder.String(option.Label)),
            ("message", builder.String(option.Message)),
            ("travelModifier", builder.Number(option.TravelModifier)))).ToArray())),
        ("wagon", builder.Object(
            ("exists", builder.Boolean(transport.Wagon.Exists)),
            ("accessible", builder.Boolean(transport.Wagon.Accessible)),
            ("id", transport.Wagon.Id is long wagonId ? builder.Number(wagonId) : builder.Null()),
            ("usedClassicUnits", builder.Number(transport.Wagon.UsedClassicUnits)),
            ("capacityClassicUnits", builder.Number(transport.Wagon.CapacityClassicUnits)),
            ("storeRevision", transport.Wagon.StoreRevision is ulong revision
                ? builder.String(revision.ToString(CultureInfo.InvariantCulture)) : builder.Null()),
                ("message", builder.String(transport.Wagon.Message)),
                ("items", builder.Array(transport.Wagon.Items.Select(item => builder.Object(
                    ("key", builder.String(item.Key)),
                    ("definition", builder.String(item.Definition)),
                    ("label", builder.String(item.Label)),
                    ("quantity", builder.String(item.Quantity)))).ToArray())),
                ("refusedDefinitions", builder.Array(transport.Wagon.RefusedDefinitions.Select(builder.String).ToArray())))));

    private static uint Dialogue(UiValueBuilder builder, DaggerfallDialogueView dialogue) => builder.Object(
        ("revision", builder.String(dialogue.Revision)),
        ("targetLabel", builder.String(dialogue.TargetLabel)),
        ("questContacts", builder.Array(dialogue.QuestContacts.Select(contact => builder.Object(
            ("instance", builder.String(contact.InstanceId)), ("symbol", builder.String(contact.Symbol)))).ToArray())),
        ("bankAvailable", builder.Boolean(dialogue.BankAvailable)),
        ("merchant", dialogue.Merchant is null ? builder.Null() : Merchant(builder, dialogue.Merchant)),
        ("greeting", builder.String(dialogue.Greeting)),
        ("comprehendLanguagesBonus", builder.Number(dialogue.ComprehendLanguagesBonus)),
        ("tone", builder.String(dialogue.Tone)),
        ("question", dialogue.Question is null ? builder.Null() : builder.String(dialogue.Question)),
        ("reply", dialogue.Reply is null ? builder.Null() : builder.String(dialogue.Reply)),
        ("topics", builder.Array(dialogue.Topics.Select(topic => builder.Object(
            ("id", builder.String(topic.Id)),
            ("label", builder.String(topic.Label)),
            ("key", topic.Key is null ? builder.Null() : builder.String(topic.Key)))).ToArray())),
        ("training", dialogue.Training is not { } training ? builder.Null() : builder.Object(
            ("providerFaction", builder.Number(training.ProviderFactionId)),
            ("membershipFaction", builder.Number(training.MembershipFactionId)),
            ("member", builder.Boolean(training.IsMember)),
            ("rank", builder.Number(training.Rank)),
            ("price", builder.Number(training.Price)),
            ("durationSeconds", builder.Number(training.DurationSeconds)),
            ("cooldownReadySecond", builder.Number(training.CooldownReadySecond)),
            ("skills", builder.Array(training.Skills.Select(skill => builder.Object(
                ("id", builder.String(skill.Id)),
                ("label", builder.String(DaggerfallCharacterPresentation.Label(skill.Id))),
                ("permanentValue", builder.Number(skill.PermanentValue)),
                ("maximumValue", builder.Number(skill.MaximumValue)))).ToArray())))),
        ("diagnostics", builder.Array(dialogue.Diagnostics.Select(builder.String).ToArray())));

    private static uint ItemMakerSetting(UiValueBuilder builder, DaggerfallItemMakerSetting setting) => builder.Object(
        ("key", builder.String(setting.Key)), ("name", builder.String(setting.Name)), ("cost", builder.Number(setting.Cost)),
        ("forced", builder.Array(setting.Forced.Select(builder.String).ToArray())));

    private static uint Options(UiValueBuilder builder, IReadOnlyList<string> labels) =>
        builder.Array(labels.Select((label, value) => builder.Object(("value", builder.Number(value)), ("label", builder.String(label)))).ToArray());

    private static uint SpellMaker(UiValueBuilder builder, DaggerfallSpellMakerView maker) => builder.Object(
        ("revision", builder.String(maker.Revision)), ("provider", builder.String(maker.Provider)),
        ("targets", Options(builder, Policies.DaggerfallMagicCostPolicy.TargetLabels)),
        ("elements", Options(builder, Policies.DaggerfallMagicCostPolicy.ElementLabels)),
        ("effects", builder.Array(maker.Effects.Select(effect => builder.Object(("key", builder.String(effect.Key)),
            ("name", builder.String(DaggerfallEffectCatalog.Label(effect.Key))), ("school", builder.String(effect.School)),
            ("type", builder.Number(effect.Type)), ("subType", builder.Number(effect.SubType)),
            ("duration", builder.Boolean(effect.Duration)), ("chance", builder.Boolean(effect.Chance)), ("magnitude", builder.Boolean(effect.Magnitude)),
            ("targets", builder.Number(effect.Targets)), ("elements", builder.Number(effect.Elements)))).ToArray())),
        ("draft", builder.Object(("name", builder.String(maker.Draft.Name)), ("element", builder.Number(maker.Draft.Element)),
            ("rangeType", builder.Number(maker.Draft.RangeType)), ("icon", builder.Number(maker.Draft.Icon)),
            ("effects", builder.Array(maker.Draft.Effects.Select(effect => builder.Object(("key", builder.String(effect.Key)),
                ("type", builder.Number(effect.Type)), ("subType", builder.Number(effect.SubType)),
                ("durationBase", builder.Number(effect.DurationBase)), ("durationMod", builder.Number(effect.DurationMod)),
                ("durationPerLevel", builder.Number(effect.DurationPerLevel)), ("chanceBase", builder.Number(effect.ChanceBase)),
                ("chanceMod", builder.Number(effect.ChanceMod)), ("chancePerLevel", builder.Number(effect.ChancePerLevel)),
                ("magnitudeBaseLow", builder.Number(effect.MagnitudeBaseLow)), ("magnitudeBaseHigh", builder.Number(effect.MagnitudeBaseHigh)),
                ("magnitudeLevelBase", builder.Number(effect.MagnitudeLevelBase)), ("magnitudeLevelHigh", builder.Number(effect.MagnitudeLevelHigh)),
                ("magnitudePerLevel", builder.Number(effect.MagnitudePerLevel)))).ToArray())))),
        ("quote", maker.Quote is not { } quote ? builder.Null() : builder.Object(("key", builder.String(quote.Key)),
            ("gold", builder.Number(quote.Gold)), ("spellPoints", builder.Number(quote.SpellPoints)), ("eligible", builder.Boolean(quote.Eligible)),
            ("reason", quote.Reason is null ? builder.Null() : builder.String(DaggerfallSession.SpellMakerOutcomeText(quote.Reason))))));

    private static uint ItemMaker(UiValueBuilder builder, DaggerfallItemMakerView maker) => builder.Object(
        ("revision", builder.String(maker.Revision)), ("provider", builder.String(maker.Provider)), ("eligible", builder.Boolean(maker.Eligible)),
        ("items", builder.Array(maker.Items.Select(item => builder.Object(("key", builder.String(item.Key)), ("name", builder.String(item.Name)),
            ("capacity", builder.Number(item.Capacity)), ("quantity", builder.Number(item.Quantity)))).ToArray())),
        ("settings", builder.Array(maker.Settings.Select(setting => ItemMakerSetting(builder, setting)).ToArray())),
        ("draft", builder.Object(("item", builder.String(maker.Draft.Item)), ("name", builder.String(maker.Draft.Name)),
            ("settings", builder.Array(maker.Draft.Settings.Select(builder.String).ToArray())))),
        ("quote", maker.Quote is not { } quote ? builder.Null() : builder.Object(("key", builder.String(quote.Key)),
            ("capacity", builder.Number(quote.Capacity)), ("power", builder.Number(quote.Power)), ("gold", builder.Number(quote.Gold)),
            ("eligible", builder.Boolean(quote.Eligible)), ("reason", quote.Reason is null ? builder.Null() : builder.String(DaggerfallSession.ItemMakerOutcomeText(quote.Reason))),
            ("payloads", builder.Array(quote.Payloads.Select(setting => ItemMakerSetting(builder, setting)).ToArray())))));

    private static uint PotionMaker(UiValueBuilder builder, DaggerfallPotionMakerView maker) => builder.Object(
        ("revision", builder.String(maker.Revision)), ("provider", builder.String(maker.Provider)), ("eligible", builder.Boolean(maker.Eligible)),
        ("ingredients", builder.Array(maker.Ingredients.Select(value => builder.Object(
            ("template", builder.Number(value.Template)), ("name", builder.String(value.Name)), ("quantity", builder.Number(checked((long)value.Quantity))))).ToArray())),
        ("recipes", builder.Array(maker.Recipes.Select(value => builder.Object(
            ("key", builder.Number(value.Key)), ("name", builder.String(value.Name)), ("available", builder.Boolean(value.Available)),
            ("ingredients", builder.Array(value.Ingredients.Select(item => builder.Number(item)).ToArray())))).ToArray())));

    private static uint Merchant(UiValueBuilder builder, DaggerfallMerchantView merchant) => builder.Object(
        ("revision", builder.String(merchant.Revision)),
        ("provider", builder.String(merchant.Provider)),
        ("quality", builder.Number(merchant.Quality)),
        ("gold", builder.String(merchant.PlayerGold.ToString(CultureInfo.InvariantCulture))),
        ("buyAvailable", builder.Boolean(merchant.CanBuy)),
        ("shopliftAvailable", builder.Boolean(merchant.CanShoplift)),
        ("sellAvailable", builder.Boolean(merchant.CanSell)),
        ("repairAvailable", builder.Boolean(merchant.CanRepair)),
        ("identifyAvailable", builder.Boolean(merchant.CanIdentify)),
        ("result", builder.String(merchant.Result)),
        ("stock", builder.Array(merchant.Stock.Select(item => MerchantItem(builder, item, false, false)).ToArray())),
        ("playerItems", builder.Array(merchant.PlayerItems.Select(item => MerchantItem(builder, item,
            merchant.CanRepair && item.Repairable, merchant.CanIdentify && item.Identifiable)).ToArray())),
        ("repairs", builder.Array(merchant.Repairs.Select(repair => builder.Object(
            ("requestId", builder.String(repair.RequestId)),
            ("durableItemId", builder.String(repair.DurableItemId.ToString(CultureInfo.InvariantCulture))),
            ("definition", builder.String(repair.Definition)),
            ("dueMinute", builder.Number(repair.DueMinute)),
            ("ready", builder.Boolean(repair.Ready)))).ToArray())));

    private static uint MerchantItem(UiValueBuilder builder, DaggerfallMerchantItemView item, bool canRepair, bool canIdentify) => builder.Object(
        ("key", builder.String(item.Key)),
        ("definition", builder.String(item.Definition)),
        ("label", builder.String(item.Label)),
        ("quantity", builder.String(item.Quantity.ToString(CultureInfo.InvariantCulture))),
        ("unitPrice", builder.String(item.UnitPrice.ToString(CultureInfo.InvariantCulture))),
        ("currentCondition", builder.Number(item.CurrentCondition)),
        ("maximumCondition", builder.Number(item.MaximumCondition)),
        ("identified", builder.Boolean(item.Identified)),
        ("stolen", builder.Boolean(item.Stolen)),
        ("canBuy", builder.Boolean(item.CanBuy)),
        ("canSell", builder.Boolean(item.CanSell)),
        ("canRepair", builder.Boolean(canRepair)),
        ("canIdentify", builder.Boolean(canIdentify)));

    private static uint Death(UiValueBuilder builder, DaggerfallDeathView death) => builder.Object(
        ("active", builder.Boolean(death.Active)),
        ("screen", builder.String(death.Screen)),
        ("revision", builder.String(death.Revision.ToString(CultureInfo.InvariantCulture))),
        ("message", builder.String(death.Message)),
        ("controlsSuppressed", builder.Boolean(death.ControlsSuppressed)),
        ("cameraEffect", builder.String(death.CameraEffect == DaggerfallDeathCameraEffect.Fall ? "fall" : "none")),
        ("fadeEffect", builder.String(death.FadeEffect == DaggerfallDeathFadeEffect.ToBlack ? "to-black" : "none")),
        ("audioCue", builder.String(death.AudioCue == DaggerfallDeathAudioCue.PlayerDeath ? "player-death" : "none")),
        ("choices", builder.Array(death.Choices.Select(choice => builder.Object(
            ("action", builder.String(choice.Action)),
            ("id", builder.String(choice.Id switch
            {
                DaggerfallDeathChoiceId.NewGame => "new-game",
                DaggerfallDeathChoiceId.LoadGame => "load-game",
                DaggerfallDeathChoiceId.QuitToTitle => "quit-to-title",
                _ => "unknown",
            })),
            ("label", builder.String(choice.Label)),
            ("available", builder.Boolean(choice.Available)))).ToArray())),
        ("selected", death.Selected is { } selected ? builder.String(selected switch
        {
            DaggerfallDeathChoiceId.NewGame => "new-game",
            DaggerfallDeathChoiceId.LoadGame => "load-game",
            DaggerfallDeathChoiceId.QuitToTitle => "quit-to-title",
            _ => "unknown",
        }) : builder.Null()));

    private static uint Rest(UiValueBuilder builder, DaggerfallRestView rest) => builder.Object(
        ("hasResult", builder.Boolean(rest.HasResult)),
        ("revision", builder.String(rest.Revision.ToString(CultureInfo.InvariantCulture))),
        ("mode", rest.Mode is null ? builder.Null() : builder.String(rest.Mode)),
        ("requestedSeconds", builder.Number(rest.RequestedSeconds)),
        ("elapsedSeconds", builder.Number(rest.ElapsedSeconds)),
        ("recoveryHours", builder.Number(rest.RecoveryHours)),
        ("healthRecovered", builder.Number(rest.HealthRecovered)),
        ("fatigueRecovered", builder.Number(rest.FatigueRecovered)),
        ("spellPointsRecovered", builder.Number(rest.SpellPointsRecovered)),
        ("interruption", builder.String(DaggerfallRestPresentation.InterruptionText(rest.Interruption))),
        ("message", rest.Message is null ? builder.Null() : builder.String(rest.Message)));

    private static uint Quests(UiValueBuilder builder, DaggerfallQuestPresentation quests) => builder.Object(
        ("offer", quests.Offer is not { } offer ? builder.Null() : builder.Object(
            ("instance", builder.String(offer.Identity)), ("text", builder.String(offer.Text)),
            ("diagnostics", builder.Array(offer.Diagnostics.Select(builder.String).ToArray())))),
        ("escortFaces", builder.Array(quests.EscortFaces.Select(face => builder.Object(
            ("instance", builder.String(face.InstanceId)), ("symbol", builder.String(face.Symbol)),
            ("name", builder.String(face.Name)), ("mediaId", builder.String(face.MediaId)))).ToArray())),
        ("deliveries", builder.Array(quests.Deliveries.Select(message => QuestMessage(builder, message)).ToArray())),
        ("journal", builder.Array(quests.Journal.Select(message => QuestMessage(builder, message)).ToArray())),
        ("pending", quests.Pending is null ? builder.Null() : QuestMessage(builder, quests.Pending)));

    private static uint Notebook(UiValueBuilder builder, DaggerfallNotebookPresentation notebook) => builder.Object(
        ("revision", builder.String(notebook.Revision)),
        ("book", notebook.Book is null ? builder.Null() : builder.Object(
            ("id", builder.Number(notebook.Book.BookId)), ("title", builder.String(notebook.Book.Title)),
            ("author", builder.String(notebook.Book.Author)), ("page", builder.Number(notebook.Book.Page)),
            ("pageCount", builder.Number(notebook.Book.PageCount)), ("text", builder.String(notebook.Book.Text)))),
        ("notes", builder.Array(notebook.Notes.Select(note => builder.Object(
            ("id", builder.String(note.Id)), ("text", builder.String(note.Text)))).ToArray())));

    private static uint QuestMessage(UiValueBuilder builder, DaggerfallQuestRenderedMessage message) => builder.Object(
        ("entryId", message.EntryId is null ? builder.Null() : builder.String(message.EntryId)),
        ("instance", builder.String(message.InstanceId)),
        ("message", builder.Number(message.MessageId)),
        ("delivery", builder.String(message.Delivery.ToString().ToLowerInvariant())),
        ("heading", builder.String(DaggerfallQuestMessageDeliveries.Heading(message.Delivery))),
        ("text", builder.String(message.Text)),
        ("signoff", message.Signoff is null ? builder.Null() : builder.String(message.Signoff)),
        ("promptId", message.PromptId is null ? builder.Null() : builder.String(message.PromptId)),
        ("options", builder.Array((message.Options ?? []).Select(option => builder.Object(
            ("id", builder.Number(option.Id)), ("label", builder.String(option.Label)))).ToArray())),
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
            ("bank", value.Bank is { } bank ? builder.Object(
                ("currentRegion", builder.Number(bank.CurrentRegion)),
                ("currentRegionName", builder.String(bank.CurrentRegionName)),
                ("currentBalance", builder.String(bank.CurrentBalance)),
                ("maximumNewLoan", builder.String(bank.MaximumNewLoan.ToString(CultureInfo.InvariantCulture))),
                ("loan", bank.Loan is { } loan ? builder.Object(
                    ("principal", builder.String(loan.Principal)),
                    ("remaining", builder.String(loan.Remaining)),
                    ("dueMinute", builder.Number(loan.DueMinute)),
                    ("daysRemaining", builder.Number(loan.DaysRemaining)),
                    ("defaulted", builder.Boolean(loan.Defaulted))) : builder.Null()),
                ("accounts", builder.Array(bank.Accounts.Select(account => builder.Object(
                    ("region", builder.Number(account.Region)), ("regionName", builder.String(account.RegionName)),
                    ("gold", builder.String(account.Gold)))).ToArray()))) : builder.Null()),
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
        ("condition", item.Condition is { } condition ? builder.Object(("current", builder.Number(condition.Current)),
            ("maximum", builder.Number(condition.Maximum)), ("percentage", builder.Number(condition.Percentage)),
            ("broken", builder.Boolean(condition.Broken))) : builder.Null()),
        ("identified", builder.Boolean(item.Identified)),
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
        uint Requirement(CharacterGuildRequirementPresentation? rank) => rank is null ? builder.Null() : builder.Object(
            ("rank", builder.Number(rank.Rank)), ("reputation", builder.Number(rank.Reputation)),
            ("highSkill", builder.Number(rank.HighSkill)), ("lowSkill", builder.Number(rank.LowSkill)));
        uint creation = value.Creation is null ? builder.Null() : Creation(builder, value.Creation);
        uint identity = value.Identity is null ? builder.Null() : Identity(builder, value.Identity);
        return builder.Object(("name", builder.String(value.Name)),
            ("attributes", builder.Array(value.Attributes.Select(Stat).ToArray())),
            ("skills", builder.Array(value.Skills.Select(Stat).ToArray())),
            ("resources", builder.Array(value.Resources.Select(resource => builder.Object(
                ("id", builder.String(resource.Id)), ("label", builder.String(resource.Label)),
                ("current", builder.Number(resource.Current)), ("maximum", builder.Number(resource.Maximum)))).ToArray())),
            ("progression", builder.Object(("level", builder.Number(value.Progression.Level)), ("experience", builder.Number(value.Progression.Experience)),
                ("skillProgress", value.Progression.SkillProgress is int progress ? builder.Number(progress) : builder.Null()),
                ("nextLevelSkillProgress", value.Progression.NextLevelSkillProgress is int next ? builder.Number(next) : builder.Null()),
                ("pendingLevelUp", builder.Boolean(value.Progression.PendingLevelUp)))),
            ("equipment", builder.Array(value.Equipment.Select(item => builder.Object(("label", builder.String(item.Label)),
                ("slots", builder.Array(item.Slots.Select(builder.String).ToArray())), ("details", builder.String(item.Details)),
                ("condition", item.Condition is null ? builder.Null() : builder.Object(("current", builder.Number(item.Condition.Current)),
                    ("maximum", builder.Number(item.Condition.Maximum)), ("percentage", builder.Number(item.Condition.Percentage)), ("broken", builder.Boolean(item.Condition.Broken)))),
                ("identified", builder.Boolean(item.Identified)))).ToArray())),
            ("resistances", builder.Array(value.Resistances.Select(Stat).ToArray())),
            ("affiliations", builder.Array(value.Affiliations.Select(affiliation => builder.Object(
                ("faction", builder.String(affiliation.Faction)), ("guildGroup", builder.String(affiliation.GuildGroup)),
                ("rank", builder.Number(affiliation.Rank)), ("reputation", builder.Number(affiliation.Reputation)),
                ("recognition", builder.Number(affiliation.Recognition)),
                ("currentRequirement", Requirement(affiliation.CurrentRequirement)),
                ("nextRequirement", Requirement(affiliation.NextRequirement)),
                ("daysUntilReview", affiliation.DaysUntilReview is int days ? builder.Number(days) : builder.Null()),
                ("privileges", builder.Array((affiliation.Privileges ?? []).Select(builder.String).ToArray())))).ToArray())),
            ("history", value.History is null ? builder.Null() : builder.Object(("biography", builder.Array(value.History.Biography.Select(builder.String).ToArray())))),
            ("grantedSkills", builder.Array((value.GrantedSkills ?? []).Select(skill => builder.Object(
                ("id", builder.String(skill.SkillId)), ("tier", builder.String(skill.Tier.ToString().ToLowerInvariant())),
                ("label", builder.String(DaggerfallCharacterPresentation.Label(skill.SkillId))),
                ("tierLabel", builder.String(skill.Tier switch
                {
                    DaggerfallCareerSkillTier.Primary => "Primary skill",
                    DaggerfallCareerSkillTier.Major => "Major skill",
                    _ => "Minor skill",
                })))).ToArray())),
            ("creationAvailable", builder.Boolean(creationAvailable)), ("creation", creation),
            ("levelUp", !levelUpAvailable || value.LevelUp is null ? builder.Null() : LevelUp(builder, value.LevelUp)),
            // The media the sheet draws from, so a consumer resolves published identities rather than
            // reconstructing a race's file names. An actor that declares no race publishes none.
            ("identity", identity));
    }

    private static uint LevelUp(UiValueBuilder builder, DaggerfallLevelUpPresentation levelUp) => builder.Object(
        ("title", builder.String(levelUp.Title)),
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
        ("mode", creation.Mode is null ? builder.Null() : builder.String(creation.Mode)),
        ("classQuestionsAvailable", builder.Boolean(creation.ClassQuestionsAvailable)),
        ("classQuiz", creation.ClassQuiz is null ? builder.Null() : ClassQuiz(builder, creation.ClassQuiz)),
        ("current", builder.Object(("name", builder.String(creation.Current.Name)), ("race", builder.String(creation.Current.RaceId)),
            ("gender", builder.String(creation.Current.Gender == DaggerfallCharacterGender.Female ? "female" : "male")), ("faceIndex", builder.Number(creation.Current.FaceIndex)),
            ("reflexes", builder.Number((int)creation.Current.Reflexes)), ("career", builder.String(creation.Current.CareerId)))),
        ("races", Choices(builder, creation.Races)), ("careers", Choices(builder, creation.Careers)),
        ("faces", builder.Array(creation.Faces.Select(face => builder.Object(("index", builder.Number(face.Index)), ("mediaId", builder.String(face.MediaId)))).ToArray())),
        ("reflexes", builder.Array(creation.Reflexes.Select(reflex => builder.Object(("value", builder.Number(reflex.Value)), ("label", builder.String(reflex.Label)))).ToArray())),
        ("custom", creation.Custom is null ? builder.Null() : Custom(builder, creation.Custom)),
        ("summary", creation.Summary is null ? builder.Null() : builder.Array(creation.Summary.Select(builder.String).ToArray())),
        ("background", creation.Background is null ? builder.Null() : Background(builder, creation.Background)));

    private static uint ClassQuiz(UiValueBuilder builder, DaggerfallClassQuizPresentation quiz) => builder.Object(
        ("answered", builder.Number(quiz.Answered)), ("total", builder.Number(quiz.Total)),
        ("question", builder.Object(("number", builder.Number(quiz.Question.Number)), ("text", builder.String(quiz.Question.Text)),
            ("answers", builder.Array(quiz.Question.Answers.Select((answer, index) => builder.Object(
                ("index", builder.Number(index)), ("text", builder.String(answer.Text)))).ToArray())))));

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
        ("startingGrants", builder.Array(background.StartingGrants.Select(grant => builder.Object(("itemId", builder.String(grant.ItemId)), ("label", builder.String(grant.Label)), ("templateIndex", builder.Number(grant.TemplateIndex)), ("quantity", builder.Number((long)grant.Quantity)), ("sourceEffect", builder.String(grant.SourceEffect)))).ToArray())),
        ("unsupportedEffects", builder.Array(background.UnsupportedEffects.Select(builder.String).ToArray())));

    private static uint Choices(UiValueBuilder builder, IEnumerable<DaggerfallCharacterChoice> choices) => builder.Array(choices.Select(choice => builder.Object(
        ("id", builder.String(choice.Id)), ("label", builder.String(choice.Label)), ("available", builder.Boolean(choice.Available)),
        ("restriction", choice.Restriction is null ? builder.Null() : builder.String(choice.Restriction)))).ToArray());

    private static uint Traits(UiValueBuilder builder, IEnumerable<DaggerfallCustomCareerTrait> traits) => builder.Array(traits
        .Select(trait => builder.Object(("id", builder.String(trait.Id)), ("target", trait.Target is null ? builder.Null() : builder.String(trait.Target)))).ToArray());

    private static uint Identity(UiValueBuilder builder, CharacterIdentityPresentation identity) => builder.Object(
        ("race", builder.String(identity.Race)), ("donorRaceId", builder.Number(identity.DonorRaceId)), ("portrait", builder.String(identity.Portrait)),
        ("gender", builder.String(identity.Gender)), ("faceIndex", builder.Number(identity.FaceIndex)), ("career", builder.String(identity.Career)),
        ("racialOverride", identity.RacialOverride is not { } racial ? builder.Null() : builder.Object(
            ("name", builder.String(racial.Name)), ("vampireClan", identity.VampireClan is null ? builder.Null() : builder.String(identity.VampireClan)), ("beastForm", builder.Boolean(racial.State.BeastForm)),
            ("suppressInventory", builder.Boolean(racial.SuppressInventory)))),
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
        CharacterResourcePresentation value = DaggerfallCharacterPresentation.Resource(player, resource);
        return builder.Object(("id", builder.String(value.Id)), ("label", builder.String(value.Label)), ("current", builder.Number(value.Current)), ("maximum", builder.Number(value.Maximum)));
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
