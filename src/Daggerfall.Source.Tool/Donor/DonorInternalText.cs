using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Source.Tool.Donor;

/// <summary>
/// Daggerfall Unity's localized <c>Internal_Strings.csv</c> as the base pack's <c>internalText</c> section: a text
/// family of its own, in the text contract's shape, which the ruleset reads beside the imported <c>text</c>.
/// </summary>
internal static class DonorInternalText
{
    public static DaggerfallText Build(byte[] bytes, string label, string language)
    {
        InternalStringsCatalog catalog = InternalStringsReader.Read(bytes, label);
        DaggerfallTextRecord[] records = [.. catalog.Records.Select(entry => FamilyTextRecords.Plain(
            DaggerfallTextKind.Internal, entry.Key, label, entry.Index, checked((int)entry.Offset), entry.ByteLength, entry.Value))];
        DaggerfallText published = new(
            [new DaggerfallTextSource(DaggerfallTextKind.Internal, label, language, bytes.LongLength, catalog.HeaderLength, catalog.Records.Count)],
            records,
            [.. DaggerfallTextBuilder.MacroIndex(records)]);
        published.Validate();
        return published;
    }
}

/// <summary>Daggerfall Unity's <c>Internal_Flats.csv</c> as the base pack's <c>npcCaptions</c> section.</summary>
internal static class DonorNpcCaptions
{
    public static NpcCaptionSection Build(byte[] bytes, string label)
    {
        InternalStringsCatalog catalog = InternalStringsReader.Read(bytes, label);
        NpcCaption[] captions = [.. catalog.Records.Select(row =>
        {
            if (!int.TryParse(row.Key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id))
                throw new InvalidOperationException($"NPC caption source '{label}' has nonnumeric flat identity '{row.Key}'.");
            return new NpcCaption(id >> 7, id & 127, row.Value);
        })];
        if (captions.Any(caption => string.IsNullOrWhiteSpace(caption.Caption))
            || captions.Select(caption => (caption.Archive, caption.Record)).Distinct().Count() != captions.Length)
            throw new InvalidOperationException($"NPC caption source '{label}' carries an empty caption or one flat address twice.");
        return new(PublishedSource.Of(label, bytes), captions);
    }
}

/// <summary>One localized flat caption, with its address decoded offline.</summary>
internal sealed record NpcCaption(int Archive, int Record, string Caption);

/// <summary>The flat captions by billboard address, citing the donor table they were taken from.</summary>
internal sealed record NpcCaptionSection(PublishedSource Source, IReadOnlyList<NpcCaption> Captions);
