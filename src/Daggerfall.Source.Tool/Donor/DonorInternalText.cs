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
