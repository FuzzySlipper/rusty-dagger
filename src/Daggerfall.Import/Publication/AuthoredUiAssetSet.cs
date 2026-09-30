using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalization;

namespace Daggerfall.Import.Publication;

/// <summary>
/// Reads the tracked authored UI manifest (<c>data/ui-authored-assets.json</c>) into the classic media
/// profile. The caller supplies the manifest bytes and reads each named original, so this owner receives
/// only portable labels and copied bytes and never discovers a directory.
/// </summary>
public static class AuthoredUiAssetSet
{
    /// <summary>The label the manifest is published under.</summary>
    public const string ManifestLabel = "ui-authored-assets.json";

    private static readonly JsonSerializerOptions Options = new(PublishedJson.SectionRead);

    /// <summary>
    /// Builds the classic media profile from the manifest, reading each asset's original through
    /// <paramref name="readOriginal"/> by its portable file leaf.
    /// </summary>
    public static Arena2ClassicMediaProfile Read(byte[] manifestBytes, Func<string, byte[]> readOriginal)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);
        ArgumentNullException.ThrowIfNull(readOriginal);
        AssetSet manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AssetSet>(manifestBytes, Options)
                ?? throw new FormatException("The authored UI manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new FormatException($"The authored UI manifest is not readable: {exception.Message}", exception);
        }

        if (manifest.Assets is null || manifest.Assets.Count == 0)
        {
            throw new FormatException("The authored UI manifest names no asset.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> outputFiles = new(StringComparer.Ordinal);
        HashSet<string> sourceFiles = new(StringComparer.Ordinal);
        long totalBytes = manifestBytes.LongLength;
        List<ClassicAuthoredUiAsset> assets = new(manifest.Assets.Count);
        foreach (Asset asset in manifest.Assets.OrderBy(asset => asset.Id, StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(asset);
            RequirePortableLeaf(asset.File, nameof(asset.File));
            RequirePortableLeaf(asset.SourceFile, nameof(asset.SourceFile));
            if (string.IsNullOrWhiteSpace(asset.Id) || asset.Id.Any(char.IsControl)
                || string.IsNullOrWhiteSpace(asset.Generator) || string.IsNullOrWhiteSpace(asset.Prompt)
                || asset.Generator.Any(char.IsControl) || asset.Prompt.Any(char.IsControl)
                || !ids.Add(asset.Id) || !outputFiles.Add(asset.File) || !sourceFiles.Add(asset.SourceFile))
            {
                throw new FormatException("Authored UI asset IDs, output files, and source files must be unique plain values.");
            }

            byte[] bytes = readOriginal(asset.SourceFile);
            totalBytes = checked(totalBytes + bytes.LongLength);
            if (totalBytes > Arena2SiteSources.MaximumTotalSourceBytes)
            {
                throw new InvalidOperationException("The authored UI input exceeds the total source byte quota.");
            }

            assets.Add(new ClassicAuthoredUiAsset(
                asset.Id,
                $"media/ui/authored/{asset.File}",
                $"ui-original/{asset.SourceFile}",
                bytes,
                asset.Generator,
                asset.Prompt));
        }

        return new Arena2ClassicMediaProfile(
            AuthoredUiManifest: new ClassicAuthoredUiManifestInput(ManifestLabel, manifestBytes),
            AuthoredUiAssets: assets);
    }

    private static void RequirePortableLeaf(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !StringComparer.Ordinal.Equals(value, Path.GetFileName(value))
            || value is "." or ".."
            || value.Contains('/') || value.Contains('\\'))
        {
            throw new FormatException($"{name} must be one portable file leaf.");
        }
    }

    private sealed record AssetSet(IReadOnlyList<Asset> Assets);

    private sealed record Asset(string Id, string File, string SourceFile, string Generator, string Prompt);
}
