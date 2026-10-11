using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Resolves an item's present Daggerfall value from its durable instance meaning. Engine inventory
/// remains the authority for quantity and containment; this service only interprets a selected
/// book identity against the admitted normalized catalog.
/// </summary>
internal sealed class DaggerfallItemValuation(DaggerfallDefinitions definitions)
{
    // EnemyBasics soul values, consumed by ItemBuilder.CreateRandomlyFilledSoulTrap.
    private static readonly int[] SoulValues = [0, 1000, 1000, 0, 0, 0, 0, 1000, 3000, 1000, 10000, 0, 1000, 3000, 1000, 0, 3000, 0, 30000, 10000, 0, 3000, 3000, 30000, 1000, 50000, 50000, 10000, 70000, 150000, 100000, 800000, 100000, 250000, 0, 30000, 30000, 30000, 30000, 0, 500000, 10000, 10000];
    private readonly DaggerfallDefinitions _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    internal int CurrentValue(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!StringComparer.Ordinal.Equals(definition.Id.Value, metadata.ItemId))
            throw new InvalidOperationException($"Item metadata '{metadata.ItemId}' does not belong to definition '{definition.Id.Value}'.");

        if (metadata.ItemId == "template-274")
            return 5000 + (metadata.CapturedSoulMobileId is int soul ? SoulValues[soul] : 0);
        if (metadata.PotionRecipeKey is int recipe) return _definitions.Magic.PotionRecipes[recipe].Price;
        if (metadata.MadeEnchantment is { } made) return made.Value;
        if (metadata.Enchantment is { } enchantment)
        {
            if (!_definitions.Magic.MagicItems.TryGetValue(enchantment, out DaggerfallMagicItemDefinition? magic))
                throw new InvalidOperationException($"Item '{definition.Id.Value}' names unpublished magic metadata '{enchantment}'.");
            return magic.Value;
        }

        bool isBook = definition.Template?.Groups.Contains("Books", StringComparer.Ordinal) == true;
        if (!isBook)
        {
            if (metadata.BookId is not null)
                throw new InvalidOperationException($"Item '{definition.Id.Value}' is not a book but carries book identity {metadata.BookId}.");
            return definition.Value;
        }

        if (metadata.BookId is not int bookId)
            throw new InvalidOperationException($"Book item '{definition.Id.Value}' has no selected book identity.");
        if (!_definitions.Books.Books.TryGetValue(bookId, out DaggerfallBookDefinition? book))
            throw new InvalidOperationException($"Book item '{definition.Id.Value}' names unpublished book {bookId}.");
        if (book.Disposition != DaggerfallBookDisposition.Read)
            throw new InvalidOperationException($"Book item '{definition.Id.Value}' names unreadable book {bookId} ({book.Disposition}).");

        return checked((int)book.RuntimePrice);
    }
}
