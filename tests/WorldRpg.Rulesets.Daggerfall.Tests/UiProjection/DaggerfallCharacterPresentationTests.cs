using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCharacterPresentationTests
{
    [Fact]
    public void Sheet_reads_current_modeled_values_resources_progression_and_actual_equipment()
    {
        using Fixture f = new();
        // The default mage uses its career's 25 + 6 starting health maximum, not the
        // former provisional endurance-derived maximum.
        f.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(21);
        f.Progression.AdvanceTo(250, 2);

        CharacterSheetPresentation sheet = f.Presentation.Read(f.Player, f.Progression);

        Assert.Equal("Player", sheet.Name);
        Assert.Equal(["strength", "intelligence", "willpower", "agility", "endurance", "personality", "speed", "luck", "reflexes"], sheet.Attributes.Select(value => value.Id));
        Assert.Equal(50, sheet.Attributes.Single(value => value.Id == "strength").Value);
        Assert.Equal(2, sheet.Attributes.Single(value => value.Id == "reflexes").Value);
        Assert.Equal(35, sheet.Skills.Length);
        Assert.Equal("medical", sheet.Skills[0].Id);
        Assert.Equal(0, sheet.Skills.Single(value => value.Id == "etiquette").Value);
        Assert.Equal(30, sheet.Skills.Single(value => value.Id == "backstabbing").Value);
        Assert.Equal(60, sheet.Skills.Single(value => value.Id == "long-blade").Value);
        Assert.Equal((21L, 31L), Resource(sheet, "health"));
        Assert.Equal((0L, 90L), Resource(sheet, "fatigue"));
        Assert.Equal((100L, 100L), Resource(sheet, "magicka"));
        Assert.Equal(2, sheet.Progression.Level);
        Assert.Equal(250, sheet.Progression.Experience);
        Assert.Collection(sheet.Equipment,
            item =>
            {
                Assert.Equal("Iron Cuirass", item.Label);
                Assert.Equal(["Chest Armor"], item.Slots);
                Assert.Equal("Armor material: Iron", item.Details);
            },
            item =>
            {
                Assert.Equal("Iron Longsword", item.Label);
                Assert.Equal(["Right Hand"], item.Slots);
                Assert.Equal("Attack 2–16; Iron; Long Blade", item.Details);
            });
    }

    [Fact]
    public void Sheet_displays_fatigue_rising_in_player_units_without_changing_the_live_track()
    {
        using Fixture f = new();
        Track fatigue = f.Player.Stats.GetTrack(TrackId.Parse("fatigue"));
        fatigue.SetCurrent(4_192);

        Assert.Equal((25L, 90L), Resource(f.Presentation.Read(f.Player, f.Progression), "fatigue"));
        Assert.Equal(4_192, fatigue.ValueInt64);
        Assert.Equal(5_760, fatigue.Maximum.ValueInt64);
    }

    [Fact]
    public void Sheet_distinguishes_live_from_permanent_values_and_reads_current_affiliation_standing()
    {
        using Fixture f = new();
        DaggerfallCharacterState character = new(f.Definitions, f.Player.Stats, f.PlayerDefinition);
        long strengthBefore = checked((long)f.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).BaseValue);
        DaggerfallStatModifiers.AdjustPermanent(f.Player.Stats, DaggerfallMechanicsIds.Strength, 4);
        _ = DaggerfallStatModifiers.ApplyMod(f.Player.Stats, DaggerfallMechanicsIds.Strength, -7);
        DaggerfallStatModifiers.AdjustPermanent(f.Player.Stats, DaggerfallMechanicsIds.ResistanceFire, 25);
        _ = DaggerfallStatModifiers.ApplyMod(f.Player.Stats, DaggerfallMechanicsIds.ResistanceFire, -10);
        f.Progression.AdvanceTo(750, 2);

        DaggerfallSocialState social = new(f.Definitions.Factions);
        DaggerfallFactionDefinition faction = f.Definitions.Factions.Factions[15];
        _ = social.JoinGuild(faction.Id, currentDay: 3);
        _ = social.PromoteGuild(faction.Id, currentDay: 4);
        _ = social.ChangeFactionReputation(faction.Id, 6);
        DaggerfallSkillUseReactions skills = new(f.Progression, f.Player.Stats, f.Definitions, () => character.Career);
        DaggerfallLevelProgress expectedProgress = skills.ReadLevelProgress();
        DaggerfallCharacterPresentation presentation = new(f.Definitions, character, f.PlayerDefinition, f.Equipment, social, skills);

        CharacterSheetPresentation sheet = presentation.Read(f.Player, f.Progression);

        Assert.Equal((strengthBefore - 3, strengthBefore + 4), Value(sheet.Attributes, "strength"));
        Assert.Equal((15L, 25L), Value(sheet.Resistances, "resistance-fire"));
        Assert.Equal((2, 750), (sheet.Progression.Level, sheet.Progression.Experience));
        Assert.Equal((expectedProgress.CurrentSkillSum, expectedProgress.NextLevelSkillSum, expectedProgress.PendingLevelUp),
            (sheet.Progression.SkillProgress, sheet.Progression.NextLevelSkillProgress, sheet.Progression.PendingLevelUp));
        CharacterAffiliationPresentation affiliation = Assert.Single(sheet.Affiliations);
        Assert.Equal(faction.Name, affiliation.Faction);
        Assert.Equal(1, affiliation.Rank);
        Assert.Equal(social.FactionReputation(faction.Id), affiliation.Reputation);
    }

    [Fact]
    public void Sheet_names_guild_rank_titles_and_regional_legal_standing_with_banishment()
    {
        using Fixture f = new();
        DaggerfallCharacterState character = new(f.Definitions, f.Player.Stats, f.PlayerDefinition);
        DaggerfallSocialState social = new(f.Definitions.Factions);
        _ = social.JoinGuild(Guilds.DaggerfallConcreteGuildCatalog.MagesFactionId, currentDay: 3);
        _ = social.PromoteGuild(Guilds.DaggerfallConcreteGuildCatalog.MagesFactionId, currentDay: 4);
        _ = social.PromoteGuild(Guilds.DaggerfallConcreteGuildCatalog.MagesFactionId, currentDay: 5);
        int current = f.Definitions.Factions.Regions.Keys.Where(key => key is >= 0 and <= 61).Min();
        int region = f.Definitions.Factions.Regions.Keys.Where(key => key is >= 0 and <= 61 && key != current).Min();
        _ = social.ChangeRegionalReputation(region, -25);
        DaggerfallCharacterPresentation presentation = new(f.Definitions, character, f.PlayerDefinition, f.Equipment, social);
        presentation.UseLegalStanding(() => current, candidate => candidate == region);

        CharacterSheetPresentation sheet = presentation.Read(f.Player, f.Progression);

        Assert.Equal("Evoker", Assert.Single(sheet.Affiliations).RankTitle);
        Assert.NotNull(sheet.LegalStandings);
        CharacterLegalStandingPresentation home = Assert.Single(sheet.LegalStandings, value => value.Region == current);
        Assert.True(home.Current);
        Assert.Equal("a common citizen", home.Standing);
        CharacterLegalStandingPresentation wanted = Assert.Single(sheet.LegalStandings, value => value.Region == region);
        Assert.Equal((-25, "a criminal", true, false), (wanted.Reputation, wanted.Standing, wanted.Banished, wanted.Current));
        Assert.Equal($"{f.Definitions.BuildingNames.RegionName(region)}: a criminal, banished", wanted.Label);
    }

    [Theory]
    [InlineData(81, "revered")]
    [InlineData(11, "respected")]
    [InlineData(1, "dependable")]
    [InlineData(0, "a common citizen")]
    [InlineData(-1, "undependable")]
    [InlineData(-11, "a scoundrel")]
    [InlineData(-61, "pond scum")]
    [InlineData(-81, "hated")]
    public void Legal_standing_uses_the_donor_reputation_words(int reputation, string expected)
        => Assert.Equal(expected, DaggerfallCharacterPresentation.LegalStanding(reputation));

    [Theory]
    [InlineData(Guilds.DaggerfallConcreteGuildCatalog.FightersFactionId, 9, false, "Master")]
    [InlineData(Guilds.DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId, 8, true, "Dark Sister")]
    [InlineData(Guilds.DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId, 8, false, "Dark Brother")]
    [InlineData(Guilds.DaggerfallConcreteGuildCatalog.ThievesFactionId, 0, false, "Apprentice")]
    public void Guild_rank_titles_follow_the_donor_guild_tables(int faction, int rank, bool female, string expected)
        => Assert.Equal(expected, Guilds.DaggerfallGuildRankTitles.Title(faction, rank, female));

    [Fact]
    public void Sheet_reuses_identification_and_condition_presentation_for_equipped_magic_items()
    {
        using Fixture f = new();
        foreach (WorldRpg.Kit.Inventory.UniqueInventoryItem equipped in f.Equipment.Read().Assignments.Select(assignment => assignment.Item).Distinct())
            f.Equipment.Unequip(equipped);

        const string magicKey = "magic-item.0010";
        DaggerfallMagicItemDefinition magic = f.Definitions.Magic.MagicItems[magicKey];
        string itemId = DaggerfallMagicItemIds.For("template-113-iron", magicKey);
        WorldRpg.Kit.Inventory.UniqueInventoryItem item = f.Equipment.Materialize(
            new DurableIdentityReference(DurableIdentityKind.Item, 700), new InventoryItemId(itemId));
        f.Equipment.Equip(item, [new SlotId("right-hand")]);

        DaggerfallItemInstances instances = new();
        DaggerfallEquipmentMoves moves = new(f.Inventory, f.Equipment, f.Definitions, itemInstances: instances);
        DaggerfallItemConditionService condition = new(f.Definitions, instances, moves);
        DaggerfallInventoryPresentation inventory = new(moves, f.Definitions, new Dictionary<string, string>());
        inventory.UseItemValuation(new DaggerfallItemValuation(f.Definitions), instances, DaggerfallItemOwner.Player,
            entity => f.Actors.Entities.IdentityOf(new EntityId(entity)).Value);
        inventory.UseItemCondition(condition);
        instances.RegisterUnique(700, new DaggerfallItemInstanceMetadata(itemId, "iron", 0, 2, 2, Identified: false,
            Stolen: false, QuestId: null, QuestItemSymbol: null, magicKey, DaggerfallItemOwner.Player));
        f.Presentation.UseItemPresentation(inventory);

        CharacterEquipmentPresentation unknown = Assert.Single(f.Presentation.Read(f.Player, f.Progression).Equipment);
        Assert.False(unknown.Identified);
        Assert.Equal(f.Definitions.RequireItem(new DaggerfallItemId(itemId)).Template!.Name, unknown.Label);
        Assert.Contains("Unidentified magical item", unknown.Details, StringComparison.Ordinal);

        _ = condition.Identify(item);
        _ = condition.Damage(item, 1);
        CharacterEquipmentPresentation identified = Assert.Single(f.Presentation.Read(f.Player, f.Progression).Equipment);
        Assert.True(identified.Identified);
        Assert.Equal("Iron " + magic.Name.Replace("%it", f.Definitions.RequireItem(new DaggerfallItemId(itemId)).Template!.Name, StringComparison.Ordinal), identified.Label);
        Assert.Equal((1, 2, 50), (identified.Condition!.Current, identified.Condition.Maximum, identified.Condition.Percentage));
        Assert.Contains("Condition: 1/2 (50%)", identified.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Paper_doll_draws_the_body_head_and_worn_items_placed_and_ordered_as_the_donor_draws_them()
    {
        using Fixture f = new();
        DaggerfallCharacterState character = new(f.Definitions, f.Player.Stats, f.PlayerDefinition);
        DaggerfallCharacterPresentation presentation = new(f.Definitions, character, f.PlayerDefinition, f.Equipment);
        DaggerfallCharacterIdentity identity = character.Identity;
        DaggerfallRaceLayers race = f.Definitions.CharacterPresentation.RequireRace(identity.RaceId);
        DaggerfallCharacterLayerDefinition nude = race.Body(identity.Gender, clothed: false);
        DaggerfallCharacterLayerDefinition clothed = race.Body(identity.Gender, clothed: true);
        DaggerfallCharacterLayerDefinition head = race.Heads(identity.Gender).Single(layer => layer.HeadIndex == identity.FaceIndex);
        int morphology = identity.RaceId switch { "argonian" => 0, "dark-elf" or "high-elf" or "wood-elf" => 1, "khajiit" => 3, _ => 2 };
        int armor = (identity.Gender == DaggerfallCharacterGender.Female ? 245 : 249) + morphology;

        // The player wears an iron cuirass and holds an iron longsword in the right hand.
        PaperDollPresentation doll = Assert.IsType<PaperDollPresentation>(presentation.Read(f.Player, f.Progression).PaperDoll);
        Assert.Equal((110, 184), (doll.Width, doll.Height));
        Assert.Equal(new PaperDollLayerPresentation("background", race.Background.MediaId, -8, -7, 125, 198), doll.Background);
        Assert.Collection(doll.Layers,
            layer => Assert.Equal(new PaperDollLayerPresentation("body", nude.MediaId, nude.Placement!.X, nude.Placement.Y, nude.Placement.Width, nude.Placement.Height), layer),
            // A cuirass covers the upper body, so only the clothed body's lower half - from the waist - is drawn.
            layer => Assert.Equal((clothed.MediaId, 40, clothed.Placement!.Height), (layer.MediaId, layer.ClipTop!.Value, layer.ClipBottom!.Value)),
            layer => Assert.Equal(("head", head.MediaId, head.Placement!.X, head.Placement.Y), (layer.Kind, layer.MediaId, layer.X, layer.Y)),
            // The authored cuirass draws from its native template's record in its material's dye.
            layer => Assert.Equal(("item", "chest-armor", f.Definitions.CharacterPresentation.PaperDollItems[(armor, 3)].Dyes["iron"]), (layer.Kind, layer.Slot, layer.MediaId)),
            // A one-handed weapon in the right hand is drawn from the record after the template's.
            layer => Assert.Equal(("item", "right-hand", "paper-doll.234.13.iron"), (layer.Kind, layer.Slot, layer.MediaId)));
        Assert.Empty(doll.Unplaced);
        DaggerfallPaperDollPlacement sword = f.Definitions.CharacterPresentation.PaperDollItems[(234, 13)].Placement;
        Assert.Equal((sword.X, sword.Y, sword.Width, sword.Height), (doll.Layers[^1].X, doll.Layers[^1].Y, doll.Layers[^1].Width, doll.Layers[^1].Height));
        Assert.All(doll.Layers, layer => Assert.False(layer.Smooth));

        // Equipping a helm and moving the sword to the left hand change the doll: the helm (draw order 0) is
        // the first item drawn, its mask clears what is beneath it, and the left hand draws the template's record.
        WorldRpg.Kit.Inventory.UniqueInventoryItem held = f.Equipment.Read().Assignments.Single(assignment => assignment.Slot.Value == "right-hand").Item;
        f.Equipment.Unequip(held);
        f.Equipment.Equip(held, [new SlotId("left-hand")]);
        WorldRpg.Kit.Inventory.UniqueInventoryItem helm = f.Equipment.Materialize(new DurableIdentityReference(DurableIdentityKind.Item, 701), new InventoryItemId("template-107-iron"));
        f.Equipment.Equip(helm, [new SlotId("head")]);
        PaperDollPresentation equipped = presentation.Read(f.Player, f.Progression).PaperDoll!;
        // The record decides the image; whether iron changes this helm's colours is the publication's to say.
        Assert.Equal([f.Definitions.CharacterPresentation.PaperDollItems[(armor, 27)].Dyes["iron"], f.Definitions.CharacterPresentation.PaperDollItems[(armor, 3)].Dyes["iron"], "paper-doll.234.12.iron"],
            equipped.Layers.Where(layer => layer.Kind == "item").Select(layer => layer.MediaId));
        PaperDollLayerPresentation helmLayer = equipped.Layers.Single(layer => layer.Slot == "head");
        Assert.Equal(f.Definitions.CharacterPresentation.PaperDollItems[(armor, 27)].MaskMediaId, helmLayer.Mask);
        Assert.NotNull(helmLayer.Mask);
        Assert.Contains(helmLayer.Mask!, equipped.MediaIds);

        // With nothing worn the censor draws both halves of the clothed body.
        foreach (WorldRpg.Kit.Inventory.UniqueInventoryItem worn in f.Equipment.Read().Assignments.Select(assignment => assignment.Item).Distinct().ToArray())
            f.Equipment.Unequip(worn);
        PaperDollPresentation bare = presentation.Read(f.Player, f.Progression).PaperDoll!;
        Assert.Equal([(null, null), (0, 40), (40, clothed.Placement.Height), (null, null)],
            bare.Layers.Select(layer => (layer.ClipTop, layer.ClipBottom)));
    }

    [Fact]
    public void Paper_doll_draws_refreshed_art_at_its_own_doll_rect_with_classic_clips_and_masks_and_clothing_and_armour_in_their_dye_or_metal()
    {
        using Fixture f = new();
        DaggerfallCharacterIdentity identity = new DaggerfallCharacterState(f.Definitions, f.Player.Stats, f.PlayerDefinition).Identity with { Gender = DaggerfallCharacterGender.Male };
        DaggerfallRaceLayers race = f.Definitions.CharacterPresentation.RequireRace(identity.RaceId);
        DaggerfallCharacterLayerDefinition nude = race.Body(identity.Gender, clothed: false);
        DaggerfallCharacterLayerDefinition clothed = race.Body(identity.Gender, clothed: true);
        DaggerfallItemDefinition Template(Func<DaggerfallItemTemplateDefinition, bool> match) => f.Definitions.TemplateItems.Values.First(item => item.Template is { } template && match(template));
        DaggerfallItemDefinition helmet = Template(template => template.Index == 107);
        DaggerfallItemDefinition shirt = Template(template => template.Groups.Contains("MensClothing") && template.Name.Contains("Shirt", StringComparison.Ordinal));
        DaggerfallItemDefinition cuirass = Template(template => template.Index == 102);
        DaggerfallWornItem Worn(string slot, DaggerfallItemDefinition item, string material, string? dye = null, int variant = 0) =>
            new($"{slot}.{item.Id.Value}", slot, item,
                new DaggerfallItemInstanceMetadata(item.Id.Value, material, variant, 1, 1, true, false, null, null, null, DaggerfallItemOwner.Player, Dye: dye), () => $"{slot} item");
        DaggerfallWornItem[] worn = [Worn("head", helmet, "steel"), Worn("chest-clothes", shirt, "cloth", dye: "red")];
        PaperDollPresentation classic = DaggerfallPaperDollPolicy.Compose(f.Definitions, identity, null, worn);
        PaperDollLayerPresentation classicHelm = classic.Layers.Single(layer => layer.Slot == "head");
        PaperDollLayerPresentation classicShirt = classic.Layers.Single(layer => layer.Slot == "chest-clothes");
        Assert.NotNull(classicHelm.Mask);
        PaperDollLayerPresentation Cuirass(string material, DaggerfallArtReplacements? set) =>
            DaggerfallPaperDollPolicy.Compose(f.Definitions, identity, null, [Worn("chest-armor", cuirass, material, variant: 1)], set).Layers.Single(layer => layer.Slot == "chest-armor");
        PaperDollLayerPresentation classicCuirass = Cuirass("steel", null);

        // Every classic image the doll draws has a replacement with a rect of its own, as promotion writes them.
        System.Text.Json.Nodes.JsonObject Entry(string replaces, double x, double y, double width, double height) => new()
        {
            ["replaces"] = replaces, ["mediaId"] = $"refreshed.{replaces}", ["path"] = $"worldrpg/packs/daggerfall.art-replacements/media/paper-doll/{replaces}.webp", ["byteLength"] = 1,
            ["doll"] = new System.Text.Json.Nodes.JsonObject { ["x"] = x, ["y"] = y, ["width"] = width, ["height"] = height },
        };
        // The shirt's refreshed record is one neutral image for every dye, standing in for the record as published undyed.
        string neutralShirt = classicShirt.MediaId[..classicShirt.MediaId.LastIndexOf('.')];
        System.Text.Json.Nodes.JsonObject shirtEntry = Entry(neutralShirt, 25, 40, 60, 50);
        shirtEntry["dyeMask"] = new System.Text.Json.Nodes.JsonObject { ["mediaId"] = $"refreshed.{neutralShirt}.dye", ["path"] = "worldrpg/packs/daggerfall.art-replacements/media/shirt-dye.webp", ["byteLength"] = 1 };
        // The steel plate cuirass's refreshed record likewise stands in for every metal, its bare steel masked for the tint.
        System.Text.Json.Nodes.JsonObject cuirassEntry = Entry(classicCuirass.MediaId, 30, 35, 50, 60);
        cuirassEntry["dyeMask"] = new System.Text.Json.Nodes.JsonObject { ["mediaId"] = $"refreshed.{classicCuirass.MediaId}.dye", ["path"] = "worldrpg/packs/daggerfall.art-replacements/media/cuirass-dye.webp", ["byteLength"] = 1 };
        System.Text.Json.Nodes.JsonObject payload = new()
        {
            ["artReplacements"] = new System.Text.Json.Nodes.JsonObject
            {
                ["images"] = new System.Text.Json.Nodes.JsonArray(Entry(nude.MediaId, 20.5, 30.25, 70, 150), Entry(clothed.MediaId, 20.5, 30.25, 70, 150),
                    Entry(classicHelm.MediaId, 40, 0, 30, 32), shirtEntry, cuirassEntry),
                ["dyeTints"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["red"] = new System.Text.Json.Nodes.JsonObject { ["multiply"] = "#fe4f1a", ["brightness"] = 1.0 },
                    ["dwarven"] = new System.Text.Json.Nodes.JsonObject { ["multiply"] = "#ffd27a", ["brightness"] = 1.1 },
                },
            },
        };
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()), "test.art");
        PaperDollPresentation refreshed = DaggerfallPaperDollPolicy.Compose(f.Definitions, identity, null, worn, art);

        // The body is drawn at the replacement's rect, smoothed.
        PaperDollLayerPresentation body = refreshed.Layers.First(layer => layer.Kind == "body");
        Assert.Equal(new PaperDollLayerPresentation("body", $"refreshed.{nude.MediaId}", 20.5, 30.25, 70, 150, Smooth: true), body);
        // The shirt covers the upper body, so the clothed body's lower half is drawn from the waist row of the
        // classic image, which in the replacement's rect is moved by the two rects' offset, down to its own bottom.
        PaperDollLayerPresentation lower = refreshed.Layers.Single(layer => layer.MediaId == $"refreshed.{clothed.MediaId}");
        Assert.Equal((clothed.Placement!.Y + 40 - 30.25, 150d), (lower.ClipTop!.Value, lower.ClipBottom!.Value));
        // The helm keeps its classic mask, drawn where the classic helm was.
        PaperDollLayerPresentation helm = refreshed.Layers.Single(layer => layer.Slot == "head");
        Assert.Equal((helm.MediaId, helm.X, helm.Y, helm.Mask), ($"refreshed.{classicHelm.MediaId}", 40d, 0d, classicHelm.Mask));
        Assert.Equal(new DaggerfallDollRect(classicHelm.X, classicHelm.Y, classicHelm.Width, classicHelm.Height), helm.MaskRect);
        // Refreshed clothing draws its neutral image tinted in the worn dye through its dye mask.
        PaperDollLayerPresentation shirtLayer = refreshed.Layers.Single(layer => layer.Slot == "chest-clothes");
        Assert.Equal(($"refreshed.{neutralShirt}", 25d, true, new PaperDollTint("#fe4f1a", 1, $"refreshed.{neutralShirt}.dye")),
            (shirtLayer.MediaId, shirtLayer.X, shirtLayer.Smooth, shirtLayer.Tint));
        Assert.Contains($"refreshed.{neutralShirt}.dye", refreshed.MediaIds);
        // A dye the set states no tint for keeps the classic image of that dye.
        PaperDollPresentation purple = DaggerfallPaperDollPolicy.Compose(f.Definitions, identity, null, [Worn("chest-clothes", shirt, "cloth", dye: "purple")], art);
        Assert.Equal(DaggerfallPaperDollPolicy.Compose(f.Definitions, identity, null, [Worn("chest-clothes", shirt, "cloth", dye: "purple")]).Layers.Single(layer => layer.Slot == "chest-clothes"),
            purple.Layers.Single(layer => layer.Slot == "chest-clothes"));
        // Steel, which the classic leaves as it is, draws the refreshed cuirass untinted. Another metal draws the
        // refreshed steel tinted in that metal, in place of the classic palette swap; a metal the set states no tint
        // for keeps its classic image.
        Assert.Equal(($"refreshed.{classicCuirass.MediaId}", 30d, (PaperDollTint?)null), (Cuirass("steel", art).MediaId, Cuirass("steel", art).X, Cuirass("steel", art).Tint));
        Assert.NotEqual(classicCuirass.MediaId, Cuirass("dwarven", null).MediaId);
        PaperDollLayerPresentation dwarven = Cuirass("dwarven", art);
        Assert.Equal(($"refreshed.{classicCuirass.MediaId}", 30d, true, new PaperDollTint("#ffd27a", 1.1, $"refreshed.{classicCuirass.MediaId}.dye")),
            (dwarven.MediaId, dwarven.X, dwarven.Smooth, dwarven.Tint));
        Assert.Equal(Cuirass("ebony", null), Cuirass("ebony", art));
    }

    [Fact]
    public void Refreshed_weapon_icon_draws_the_whole_weapon_while_the_doll_keeps_the_layer_with_the_hand_gap()
    {
        using Fixture f = new();
        DaggerfallCharacterIdentity identity = new DaggerfallCharacterState(f.Definitions, f.Player.Stats, f.PlayerDefinition).Identity;
        DaggerfallItemDefinition longsword = f.Definitions.TemplateItems.Values.First(item => item.Template?.Index == 120);
        DaggerfallItemInstanceMetadata Made(string material) =>
            new(longsword.Id.Value, material, 0, 1, 1, true, false, null, null, null, DaggerfallItemOwner.Player);
        System.Text.Json.Nodes.JsonObject Image(string mediaId, double width) => new()
        {
            ["mediaId"] = mediaId, ["path"] = $"worldrpg/packs/daggerfall.art-replacements/media/worn/{mediaId}.webp", ["byteLength"] = 1,
            ["doll"] = new System.Text.Json.Nodes.JsonObject { ["x"] = 10, ["y"] = 20, ["width"] = width, ["height"] = 40 },
            ["dyeMask"] = new System.Text.Json.Nodes.JsonObject { ["mediaId"] = $"{mediaId}.dye", ["path"] = $"worldrpg/packs/daggerfall.art-replacements/media/worn/{mediaId}-dye.webp", ["byteLength"] = 1 },
        };
        // The longsword's left-hand record, as promotion writes it: the layer with its hand gap, and the whole weapon as its icon.
        System.Text.Json.Nodes.JsonObject layer = Image("refreshed.paper-doll.234.12", 30);
        layer["replaces"] = "paper-doll.234.12";
        layer["icon"] = Image("refreshed.paper-doll.234.12.icon", 34);
        System.Text.Json.Nodes.JsonObject payload = new()
        {
            ["artReplacements"] = new System.Text.Json.Nodes.JsonObject
            {
                ["images"] = new System.Text.Json.Nodes.JsonArray(layer),
                ["dyeTints"] = new System.Text.Json.Nodes.JsonObject { ["dwarven"] = new System.Text.Json.Nodes.JsonObject { ["multiply"] = "#ffd27a", ["brightness"] = 1.1 } },
            },
        };
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()), "test.art");

        // The doll draws the layer, which leaves the gap its hand shows through.
        DaggerfallWornItem held = new("left-hand.sword", "left-hand", longsword, Made("steel"), () => "sword");
        Assert.Equal("refreshed.paper-doll.234.12", DaggerfallPaperDollPolicy.Compose(f.Definitions, identity, null, [held], art).Layers.Single(item => item.Slot == "left-hand").MediaId);
        // The inventory, a shop and the ground draw the whole weapon: in steel as it is, in another metal tinted through its own mask.
        Assert.Equal(("refreshed.paper-doll.234.12.icon", (PaperDollTint?)null), DaggerfallPaperDollPolicy.InventoryImage(f.Definitions, longsword, Made("steel"), identity, art));
        Assert.Equal(("refreshed.paper-doll.234.12.icon", new PaperDollTint("#ffd27a", 1.1, "refreshed.paper-doll.234.12.icon.dye")),
            DaggerfallPaperDollPolicy.InventoryImage(f.Definitions, longsword, Made("dwarven"), identity, art));
        Assert.Same(art.For("paper-doll.234.12")!.Icon, art.ByMediaId("refreshed.paper-doll.234.12.icon"));
        // Classic art keeps its one image.
        Assert.Equal(("paper-doll.234.12.dwarven", (PaperDollTint?)null), DaggerfallPaperDollPolicy.InventoryImage(f.Definitions, longsword, Made("dwarven"), identity, null));
    }

    [Fact]
    public void Paper_doll_draws_an_artifact_from_its_own_texture_for_the_gender_it_was_made_for()
    {
        using Fixture f = new();
        DaggerfallCharacterIdentity identity = new DaggerfallCharacterState(f.Definitions, f.Player.Stats, f.PlayerDefinition).Identity;
        DaggerfallItemDefinition Template(int index) => f.Definitions.TemplateItems.Values.First(item => item.Template?.Index == index);
        DaggerfallWornItem Worn(string slot, int template, string? enchantment, string? gender = null) =>
            new($"{slot}.{template}", slot, Template(template),
                new DaggerfallItemInstanceMetadata(Template(template).Id.Value, "ebony", 0, 1, 1, true, false, null, null, enchantment, DaggerfallItemOwner.Player, Gender: gender),
                () => $"{slot} item");
        string Drawn(PaperDollPresentation doll, string slot) => doll.Layers.Single(layer => layer.Slot == slot).MediaId;
        string Artifact(int archive, int record) => f.Definitions.CharacterPresentation.PaperDollItems[(archive, record)].Dyes["unchanged"];

        // Lord's Mail (a cuirass) and the Ebony Blade (a katana) are the 16th and 23rd artifact templates, whose
        // records the donor's table gives as 9 and 5; a one-handed weapon in the right hand takes the next record.
        Assert.Equal("Lord's Mail", f.Definitions.Magic.MagicItems["magic-item.0051"].Name);
        Assert.Equal("Ebony Blade", f.Definitions.Magic.MagicItems["magic-item.0058"].Name);
        Assert.True(Template(121).Template!.IsOneHanded);
        PaperDollPresentation doll = DaggerfallPaperDollPolicy.Compose(f.Definitions, identity with { Gender = DaggerfallCharacterGender.Male }, null,
            [Worn("chest-armor", 102, "magic-item.0051"), Worn("right-hand", 121, "magic-item.0058")]);
        Assert.Equal(Artifact(432, 9), Drawn(doll, "chest-armor"));
        Assert.Equal(Artifact(432, 6), Drawn(doll, "right-hand"));
        Assert.Empty(doll.Unplaced);

        // In the left hand the blade is its own record; mail made for a woman draws from the female archive.
        doll = DaggerfallPaperDollPolicy.Compose(f.Definitions, identity with { Gender = DaggerfallCharacterGender.Male }, null,
            [Worn("chest-armor", 102, "magic-item.0051", gender: "female"), Worn("left-hand", 121, "magic-item.0058")]);
        Assert.Equal(Artifact(433, 9), Drawn(doll, "chest-armor"));
        Assert.Equal(Artifact(432, 5), Drawn(doll, "left-hand"));

        // A regular magic item keeps its base template's texture in its material's dye.
        string regular = f.Definitions.Magic.MagicItems.Values.First(magic => magic.Type == 0).Key;
        doll = DaggerfallPaperDollPolicy.Compose(f.Definitions, identity with { Gender = DaggerfallCharacterGender.Male }, null,
            [Worn("left-hand", 121, regular)]);
        Assert.Equal(f.Definitions.CharacterPresentation.PaperDollItems[(Template(121).Template!.PlayerTextureArchive, Template(121).Template!.PlayerTextureRecord)].Dyes["ebony"],
            Drawn(doll, "left-hand"));
    }

    [Fact]
    public void Paper_doll_draws_a_vampire_head_and_background_and_only_the_beast_background_while_transformed_in_refreshed_art_too()
    {
        using Fixture f = new();
        DaggerfallCharacterState character = new(f.Definitions, f.Player.Stats, f.PlayerDefinition);
        DaggerfallCharacterIdentity identity = character.Identity;
        string gender = identity.Gender == DaggerfallCharacterGender.Female ? "female" : "male";
        DaggerfallWornItem[] worn = [];

        DaggerfallRacialFormMedia vampire = f.Definitions.CharacterPresentation.RacialForms[$"vampire.{identity.RaceId}.{gender}"];
        PaperDollPresentation vampireDoll = DaggerfallPaperDollPolicy.Compose(f.Definitions, identity,
            new("vampirism", new DaggerfallRacialOverrideState(DaggerfallRacialKind.Vampire, false, 0, 0, Vampire: new(150, 0))), worn);
        Assert.Equal(vampire.BodyMediaId, vampireDoll.Background.MediaId);
        PaperDollLayerPresentation head = vampireDoll.Layers.Single(layer => layer.Kind == "head");
        Assert.Equal((vampire.HeadMediaId, vampire.HeadPlacement!.X, vampire.HeadPlacement.Y), (head.MediaId, head.X, head.Y));

        PaperDollPresentation beast = DaggerfallPaperDollPolicy.Compose(f.Definitions, identity,
            new("lycanthropy", new DaggerfallRacialOverrideState(DaggerfallRacialKind.Werewolf, true, 0, 0)), worn);
        Assert.Equal(f.Definitions.CharacterPresentation.RacialForms["werewolf"].BodyMediaId, beast.Background.MediaId);
        Assert.Equal((-8, -7), (beast.Background.X, beast.Background.Y));
        Assert.Empty(beast.Layers);

        // With refreshed art, the vampire head and the beast form's whole picture draw their replacements at their own rects.
        string beastBody = f.Definitions.CharacterPresentation.RacialForms["werewolf"].BodyMediaId;
        System.Text.Json.Nodes.JsonObject Entry(string replaces, double x, double y, double width, double height) => new()
        {
            ["replaces"] = replaces, ["mediaId"] = $"refreshed.{replaces}", ["path"] = $"worldrpg/packs/daggerfall.art-replacements/media/paper-doll/{replaces}.webp", ["byteLength"] = 1,
            ["doll"] = new System.Text.Json.Nodes.JsonObject { ["x"] = x, ["y"] = y, ["width"] = width, ["height"] = height },
        };
        System.Text.Json.Nodes.JsonObject payload = new()
        {
            ["artReplacements"] = new System.Text.Json.Nodes.JsonObject
            {
                ["images"] = new System.Text.Json.Nodes.JsonArray(Entry(vampire.HeadMediaId, 41.5, 1, 31, 33), Entry(beastBody, -8.5, -7.25, 126, 199)),
            },
        };
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()), "test.art");
        PaperDollLayerPresentation refreshedHead = DaggerfallPaperDollPolicy.Compose(f.Definitions, identity,
            new("vampirism", new DaggerfallRacialOverrideState(DaggerfallRacialKind.Vampire, false, 0, 0, Vampire: new(150, 0))), worn, art).Layers.Single(layer => layer.Kind == "head");
        Assert.Equal(new PaperDollLayerPresentation("head", $"refreshed.{vampire.HeadMediaId}", 41.5, 1, 31, 33, Smooth: true), refreshedHead);
        PaperDollPresentation refreshedBeast = DaggerfallPaperDollPolicy.Compose(f.Definitions, identity,
            new("lycanthropy", new DaggerfallRacialOverrideState(DaggerfallRacialKind.Werewolf, true, 0, 0)), worn, art);
        Assert.Equal(new PaperDollLayerPresentation("background", $"refreshed.{beastBody}", -8.5, -7.25, 126, 199, Smooth: true), refreshedBeast.Background);
    }

    private static (long Current, long Maximum) Resource(CharacterSheetPresentation sheet, string id)
    {
        CharacterResourcePresentation resource = sheet.Resources.Single(value => value.Id == id);
        return (resource.Current, resource.Maximum);
    }

    private static (long Value, long Permanent) Value(IEnumerable<CharacterStatPresentation> values, string id)
    {
        CharacterStatPresentation value = values.Single(value => value.Id == id);
        return (value.Value, value.Permanent);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MechanicsEquipmentCoordinator Equipment;
        internal readonly MechanicsInventoryCoordinator Inventory;
        internal readonly ActorsState Actors;
        internal readonly DaggerfallDefinitions Definitions;
        internal readonly DaggerfallActorDefinition PlayerDefinition;
        internal readonly PlayerActorState Player;
        internal readonly ProgressionState Progression = new();
        internal readonly DaggerfallCharacterPresentation Presentation;

        internal Fixture()
        {
            Definitions = TestPayload.Definitions;
            PlayerDefinition = Definitions.RequireActor(new DaggerfallActorId("player"));
            var items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values).ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            var slots = Definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
            Actors = new ActorsState();
            Player = Actors.CreatePlayer(1, new EntityTypeId(PlayerDefinition.Id.Value),
                new DaggerfallMechanicsState().CreateStats(PlayerDefinition, DaggerfallPlayerVitals.Initial(PlayerDefinition.Stats, Definitions.Catalogs.RequireCareer("class00"))), "health", DaggerActorFactory.PlayerCapabilities);
            EntityId owner = Player.Actor.Entity;
            InventoryStore world = new();
            world.RegisterInventory(new InventoryState(owner));
            world.RegisterEquipment(new EquipmentState(owner));
            InventoryComponent inventory = new(world, owner);
            EquipmentComponent equipmentComponent = new(world, owner);
            Player.Actor.Add(inventory);
            Player.Actor.Add(equipmentComponent);
            Inventory = new MechanicsInventoryCoordinator(inventory, Actors.Entities, items);
            Equipment = new MechanicsEquipmentCoordinator(inventory, equipmentComponent, Actors.Entities, items, slots);
            foreach (DaggerfallLoadoutEntry entry in PlayerDefinition.Loadout.Where(entry => entry.UniqueEntityId is not null))
            {
                WorldRpg.Kit.Inventory.UniqueInventoryItem item = Equipment.Materialize(
                    new DurableIdentityReference(DurableIdentityKind.Item, entry.UniqueEntityId!.Value),
                    new InventoryItemId(entry.ItemId.Value));
                if (entry.EquipSlot is DaggerfallEquipmentSlotId slot)
                    Equipment.Equip(item, [new SlotId(slot.Value)]);
            }
            Presentation = new DaggerfallCharacterPresentation(Definitions, PlayerDefinition, Equipment);
        }

        public void Dispose()
        {
            Actors.Dispose();
        }
    }

}
