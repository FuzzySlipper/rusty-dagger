using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Ruleset selection and Engine-backed materialization of normalized classic templates.</summary>
internal sealed class DaggerfallItemFactory(DaggerfallDefinitions definitions, IRandomService random)
{
    private const string Scope = "daggerfall.item-factory.v1";
    private static readonly string[] ClothingDyes = ["blue", "grey", "red", "dark-brown", "purple", "light-brown", "white", "aquamarine", "yellow", "green"];
    private readonly DaggerfallDefinitions _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly IRandomService _random = random ?? throw new ArgumentNullException(nameof(random));

    internal DaggerfallCreatedItem Create(DaggerfallItemCreateRequest request)
    {
        request.Validate();
        if (request.Category == "Magic") return CreateMagic(request);
        if (request.MagicItemKey is not null)
            throw new ArgumentException("A magic template can only be created through the Magic category.", nameof(request));
        DaggerfallItemTemplateDefinition[] eligible = _definitions.ItemTemplateCatalog.Templates.Values
            .Where(template => template.Groups.Contains(request.Category, StringComparer.Ordinal))
            .OrderBy(template => template.Index).ToArray();
        if (eligible.Length == 0) throw new ArgumentException($"No retained template belongs to category '{request.Category}'.", nameof(request));
        DaggerfallItemTemplateDefinition template = request.TemplateIndex is int index
            ? eligible.SingleOrDefault(value => value.Index == index) ?? throw new ArgumentException($"Template {index} is not in category '{request.Category}'.", nameof(request))
            : eligible[Draw(request.Key + ".template", 0, eligible.Length - 1)];
        int variant = request.Variant ?? (template.Variants > 0 ? Draw(request.Key + ".variant", 0, template.Variants - 1) : 0);
        if (variant < 0 || variant >= Math.Max(1, template.Variants)) throw new ArgumentOutOfRangeException(nameof(request), "The selected variant is outside the template's retained range.");
        string material = SelectMaterial(template, request);
        (string? race, string? gender, string? dye) = SelectAppearance(template, request, material);
        int? bookId = SelectBook(template, request);
        int? potionRecipeKey = SelectPotionRecipe(template, request);
        ulong? creditValue = SelectCreditValue(template, request);
        int condition = StartingCondition(template, material);
        string? enchantment = null;
        ulong quantity = SelectQuantity(template, request, material);
        InventoryItemId item = new(MaterializedItemId(template, material));
        return new DaggerfallCreatedItem(template.Index, item, template.Stackable,
            quantity, new DaggerfallItemInstanceMetadata(item.Value, material, variant, condition, condition,
                Identified: enchantment is null, Stolen: request.Stolen, request.QuestId, request.QuestSymbol, enchantment, request.Owner,
                race, gender, dye, bookId, potionRecipeKey, creditValue).Validate());
    }

    private DaggerfallCreatedItem CreateMagic(DaggerfallItemCreateRequest request)
    {
        if (request.TemplateIndex is not null || request.Material is not null || request.Variant is not null || request.BookId is not null || request.PotionRecipeKey is not null || request.CreditValue is not null)
            throw new ArgumentException("Magic template creation chooses its own base template, material, variant, and book identity.", nameof(request));
        DaggerfallMagicItemDefinition[] regular = _definitions.Magic.MagicItems.Values
            .Where(item => item.Type == 0).OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        DaggerfallMagicItemDefinition magic = request.MagicItemKey is { } key
            ? _definitions.Magic.MagicItems.TryGetValue(key, out DaggerfallMagicItemDefinition? selected)
                ? selected
                : throw new ArgumentException($"Magic template '{key}' is not published.", nameof(request))
            : regular.Length != 0
                ? regular[Draw(request.Key + ".magic-template", 0, regular.Length - 1)]
                : throw new InvalidOperationException("The selected content publishes no regular magic templates.");
        (string category, int? template, string? material) = MagicBase(magic, request);
        bool appearance = category is "Armor" or "MensClothing" or "WomensClothing";
        DaggerfallCreatedItem baseItem = Create(request with
        {
            Category = category,
            Key = request.Key + ".magic-base",
            TemplateIndex = template,
            Material = material,
            MagicItemKey = null,
            Race = appearance ? request.Race : null,
            Gender = appearance ? request.Gender : null,
            Dye = appearance ? request.Dye : null,
        });
        if (baseItem.TemplateIndex == 131)
            throw new InvalidOperationException("ItemBuilder never permits an enchanted arrow.");
        string itemId = DaggerfallMagicItemIds.For(baseItem.Item.Value, magic.Key);
        _ = _definitions.RequireItem(new DaggerfallItemId(itemId));
        DaggerfallItemInstanceMetadata metadata = baseItem.Metadata with
        {
            ItemId = itemId,
            CurrentCondition = magic.Uses,
            MaximumCondition = magic.Uses,
            Identified = true,
            Enchantment = magic.Key,
        };
        return new(baseItem.TemplateIndex, new InventoryItemId(itemId), Stackable: false, Quantity: 1, metadata.Validate());
    }

    private (string Category, int? Template, string? Material) MagicBase(DaggerfallMagicItemDefinition magic, DaggerfallItemCreateRequest request)
    {
        if (magic.Type == 0)
        {
            string[] choices = magic.Group switch
            {
                0 => ["Armor", "Weapons", "MensClothing", "ReligiousItems", "WomensClothing", "Gems", "Jewellery"],
                1 => ["Armor", "Weapons", "MensClothing", "WomensClothing", "Jewellery"],
                2 => ["Weapons"],
                _ => throw new InvalidOperationException($"Regular magic template '{magic.Key}' has unknown base-group selector {magic.Group}."),
            };
            string selected = choices[Draw(request.Key + ".magic-group", 0, choices.Length - 1)];
            // Both source clothing groups dispatch to CreateRandomClothing(player gender).
            if (selected is "MensClothing" or "WomensClothing")
                selected = request.Gender switch
                {
                    "male" => "MensClothing", "female" => "WomensClothing",
                    _ => throw new ArgumentException("Magic clothing requires an explicit player gender.", nameof(request)),
                };
            // ItemBuilder retries arrows before enchanting; select uniformly from the same
            // eligible weapon set so every keyed draw produces an enchantable base.
            int? selectedTemplate = null;
            if (selected == "Weapons")
            {
                int[] weapons = _definitions.ItemTemplateCatalog.Templates.Values
                    .Where(item => item.Groups.Contains("Weapons", StringComparer.Ordinal) && item.Index != 131)
                    .OrderBy(item => item.Index).Select(item => item.Index).ToArray();
                selectedTemplate = weapons[Draw(request.Key + ".magic-weapon", 0, weapons.Length - 1)];
            }
            return (selected, selectedTemplate, null);
        }
        string category = magic.Group switch
        {
            2 => "Armor", 3 => "Weapons", 6 => "MensClothing", 7 => "Books", 10 => "ReligiousItems",
            12 => "WomensClothing", 14 => "Gems", 15 => "PlantIngredients1", 25 => "Jewellery",
            _ => throw new InvalidOperationException($"Magic template '{magic.Key}' names unsupported donor group {magic.Group}."),
        };
        DaggerfallItemTemplateDefinition[] candidates = _definitions.ItemTemplateCatalog.Templates.Values
            .Where(item => item.Groups.Contains(category, StringComparer.Ordinal)).OrderBy(item => item.Index).ToArray();
        if (magic.GroupIndex < 0 || magic.GroupIndex >= candidates.Length)
            throw new InvalidOperationException($"Magic template '{magic.Key}' names missing {category} index {magic.GroupIndex}.");
        return (category, candidates[magic.GroupIndex].Index, MagicMaterial(magic, category));
    }

    private static string? MagicMaterial(DaggerfallMagicItemDefinition magic, string category) => category switch
    {
        "Weapons" when magic.Material is >= 0 and < 10 => DaggerfallItemMaterialPolicy.WeaponMaterials[magic.Material],
        "Armor" when magic.Material == 0 => "leather",
        "Armor" when magic.Material == 0x100 => "chain",
        "Armor" when magic.Material is >= 0x200 and <= 0x209 => DaggerfallItemMaterialPolicy.WeaponMaterials[magic.Material - 0x200],
        "Armor" => throw new InvalidOperationException($"Magic armor '{magic.Key}' has unknown material {magic.Material}."),
        _ => null,
    };

    internal void Materialize(DaggerfallCreatedItem created, MechanicsInventoryCoordinator inventory, DaggerfallItemInstances instances,
        InventoryStackId? stack = null, DurableIdentityReference? unique = null)
    {
        ArgumentNullException.ThrowIfNull(created); ArgumentNullException.ThrowIfNull(inventory); ArgumentNullException.ThrowIfNull(instances);
        if (created.Stackable)
        {
            InventoryStackId id = stack ?? throw new ArgumentException("A stackable created item requires an explicit stack id.", nameof(stack));
            inventory.Grant(new InventoryGrant(created.Item, id, created.Quantity));
            instances.RegisterStack(created.Metadata.Owner, id, created.Metadata);
        }
        else
        {
            DurableIdentityReference id = unique ?? throw new ArgumentException("A unique created item requires a durable identity.", nameof(unique));
            inventory.GrantAtomic([new InventoryAtomicGrant(created.Item, UniqueItem: id)]);
            instances.RegisterUnique(id.Value, created.Metadata);
        }
    }

    private string SelectMaterial(DaggerfallItemTemplateDefinition template, DaggerfallItemCreateRequest request)
    {
        bool weapon = template.Groups.Contains("Weapons", StringComparer.Ordinal);
        bool armor = template.Groups.Contains("Armor", StringComparer.Ordinal);
        if (template.Index == 131)
        {
            if (request.Material is not null)
                throw new ArgumentException("Arrows never carry a weapon material.", nameof(request));
            return "none";
        }
        if (request.Material is { } selected)
        {
            if (weapon && DaggerfallItemMaterialPolicy.IsWeaponMaterial(selected)) return selected;
            if (armor && DaggerfallItemMaterialPolicy.IsArmorMaterial(selected)) return selected;
            throw new ArgumentException($"Material '{selected}' is not valid for template {template.Index}.", nameof(request));
        }
        if (template.Groups.Contains("Weapons", StringComparer.Ordinal))
            return DaggerfallItemMaterialPolicy.RandomMaterial(request.Level, Draw(request.Key + ".material", 0, 255));
        if (template.Groups.Contains("Armor", StringComparer.Ordinal))
            return DaggerfallItemMaterialPolicy.RandomArmorMaterial(request.Level, Draw(request.Key + ".armor", 1, 100), Draw(request.Key + ".plate", 0, 255));
        return "none";
    }

    private (string? Race, string? Gender, string? Dye) SelectAppearance(DaggerfallItemTemplateDefinition template, DaggerfallItemCreateRequest request, string material)
    {
        bool mens = template.Groups.Contains("MensClothing", StringComparer.Ordinal);
        bool womens = template.Groups.Contains("WomensClothing", StringComparer.Ordinal);
        bool armor = template.Groups.Contains("Armor", StringComparer.Ordinal);
        if (!mens && !womens && !armor)
        {
            if (request.Race is not null || request.Gender is not null || request.Dye is not null)
                throw new ArgumentException($"Template {template.Index} has no race, gender, or dye appearance.", nameof(request));
            return (null, null, null);
        }
        string race = request.Race ?? throw new ArgumentException($"Template {template.Index} requires an explicit published race.", nameof(request));
        if (!_definitions.CharacterPresentation.Races.ContainsKey(race))
            throw new ArgumentException($"Race '{race}' is not published by the selected character presentation.", nameof(request));
        string gender = mens ? "male" : womens ? "female" : request.Gender ?? throw new ArgumentException($"Armor template {template.Index} requires an explicit gender.", nameof(request));
        if (gender is not ("male" or "female")) throw new ArgumentException("Item gender must be male or female.", nameof(request));
        if ((mens && request.Gender is not null && request.Gender != "male") || (womens && request.Gender is not null && request.Gender != "female"))
            throw new ArgumentException($"Template {template.Index} belongs to the {(mens ? "men's" : "women's")} clothing catalog.", nameof(request));
        string dye = request.Dye ?? (mens || womens
            ? request.TemplateIndex is null ? ClothingDyes[Draw(request.Key + ".dye", 0, ClothingDyes.Length - 1)] : "blue"
            : material);
        if (mens || womens)
        {
            if (!ClothingDyes.Contains(dye, StringComparer.Ordinal))
                throw new ArgumentException($"Dye '{dye}' is not a retained clothing dye.", nameof(request));
        }
        else if (request.Dye is not null && request.Dye != material)
            throw new ArgumentException("Armor dye is determined by its selected material.", nameof(request));
        return (race, gender, dye);
    }

    private int? SelectBook(DaggerfallItemTemplateDefinition template, DaggerfallItemCreateRequest request)
    {
        if (!template.Groups.Contains("Books", StringComparer.Ordinal))
        {
            if (request.BookId is not null) throw new ArgumentException($"Template {template.Index} is not a book.", nameof(request));
            return null;
        }
        DaggerfallBookDefinition[] readable = _definitions.Books.Books.Values
            .Where(book => book.Disposition == DaggerfallBookDisposition.Read).OrderBy(book => book.BookId).ToArray();
        if (request.BookId is int selected)
            return readable.Any(book => book.BookId == selected)
                ? selected
                : throw new ArgumentException($"Book {selected} is not a published readable book.", nameof(request));
        if (readable.Length == 0) throw new InvalidOperationException("The selected content publishes no readable books.");
        return readable[Draw(request.Key + ".book", 0, readable.Length - 1)].BookId;
    }

    private static int? SelectPotionRecipe(DaggerfallItemTemplateDefinition template, DaggerfallItemCreateRequest request)
    {
        if (template.Index is not (83 or 278))
        {
            if (request.PotionRecipeKey is not null)
                throw new ArgumentException($"Template {template.Index} is not a potion or potion recipe.", nameof(request));
            return null;
        }
        if (request.PotionRecipeKey is not int recipe || !DaggerfallLootPolicy.IsClassicPotionRecipeKey(recipe))
            throw new ArgumentException($"Template {template.Index} requires one retained classic potion recipe identity.", nameof(request));
        return recipe;
    }

    private static ulong? SelectCreditValue(DaggerfallItemTemplateDefinition template, DaggerfallItemCreateRequest request)
    {
        if (template.Index != 275)
        {
            if (request.CreditValue is not null)
                throw new ArgumentException($"Template {template.Index} is not a letter of credit.", nameof(request));
            return null;
        }
        return request.CreditValue is > 0 ? request.CreditValue
            : throw new ArgumentException("A letter of credit requires a positive redeemable amount.", nameof(request));
    }

    private ulong SelectQuantity(DaggerfallItemTemplateDefinition template, DaggerfallItemCreateRequest request, string material)
    {
        if (!template.Stackable)
        {
            if (request.Quantity is not null and not 1) throw new ArgumentException($"Unique template {template.Index} must have quantity one.", nameof(request));
            return 1;
        }
        ulong quantity = request.Quantity ?? (template.Index == 131 ? checked((ulong)Draw(request.Key + ".quantity", 1, 20)) : 1);
        if (quantity == 0 || quantity > _definitions.RequireItem(new DaggerfallItemId(MaterializedItemId(template, material))).MaximumQuantity)
            throw new ArgumentOutOfRangeException(nameof(request), $"Template {template.Index} quantity is outside its Engine-backed range.");
        return quantity;
    }

    /// <summary>
    /// The condition units one instance of this template enters play with: ItemBuilder initializes an
    /// ordinary template from its hit points, then its material routine scales weapon and plate
    /// condition, and the donor's arrow template carries no budget at all. A definition that authors no
    /// weapon or armor material keeps the unscaled hit points the routines answer for its group. This is
    /// the one owner of that rule; the authored-loadout path asks it rather than restating it.
    /// </summary>
    internal static int StartingCondition(DaggerfallItemTemplateDefinition template, string? material)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Index == 131) return 0;
        return material is null or "none" ? template.HitPoints : DaggerfallItemMaterialPolicy.Apply(template, material).MaximumCondition;
    }

    private static string MaterializedItemId(DaggerfallItemTemplateDefinition template, string material) =>
        material == "none" ? $"template-{template.Index}" : $"template-{template.Index}-{material}";

    private int Draw(string key, int minimum, int maximum) => checked((int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, Scope, key, minimum, maximum)).Value);
}

internal sealed record DaggerfallItemCreateRequest(string Category, string Key, DaggerfallItemOwner Owner, ulong? Quantity = null,
    int? TemplateIndex = null, string? Material = null, int? Variant = null, int Level = 1, bool Stolen = false,
    string? QuestId = null, string? QuestSymbol = null, string? Race = null, string? Gender = null, string? Dye = null,
    int? BookId = null, string? MagicItemKey = null, int? PotionRecipeKey = null, ulong? CreditValue = null)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Category); ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        if (Quantity == 0 || Level < 0 || BookId < 0 || PotionRecipeKey <= 0 || CreditValue == 0) throw new ArgumentOutOfRangeException(nameof(Quantity));
        if ((QuestId is null) != (QuestSymbol is null)) throw new ArgumentException("Quest items require both quest and symbol.");
        if (Material is { Length: 0 } || Race is { Length: 0 } || Gender is { Length: 0 } || Dye is { Length: 0 } || MagicItemKey is { Length: 0 })
            throw new ArgumentException("Item creation values cannot be empty strings.");
        Owner.Validate();
    }
}

internal sealed record DaggerfallCreatedItem(int TemplateIndex, InventoryItemId Item, bool Stackable, ulong Quantity, DaggerfallItemInstanceMetadata Metadata);
