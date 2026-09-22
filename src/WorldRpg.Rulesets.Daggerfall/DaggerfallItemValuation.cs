using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Resolves an item's present Daggerfall value from its durable instance meaning. Engine inventory
/// remains the authority for quantity and containment; this service only interprets a selected
/// book identity against the admitted normalized catalog.
/// </summary>
internal sealed class DaggerfallItemValuation(DaggerfallDefinitions definitions)
{
    private readonly DaggerfallDefinitions _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    internal int CurrentValue(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!StringComparer.Ordinal.Equals(definition.Id.Value, metadata.ItemId))
            throw new InvalidOperationException($"Item metadata '{metadata.ItemId}' does not belong to definition '{definition.Id.Value}'.");

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
