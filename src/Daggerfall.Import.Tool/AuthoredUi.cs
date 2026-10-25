using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool;

/// <summary>Reads the tracked authored UI input through its two explicit paths.</summary>
/// <remarks>
/// The originals are the product's own tracked content: the classic media group's inventory lists them at
/// <c>worldrpg/media/ui/authored/</c>, and the product reads their bytes from its content root rather than
/// from the data container, so the import names them and does not write them.
/// </remarks>
internal static class AuthoredUi
{
    /// <summary>The tracked directory the authored UI originals live in, as the product's content root carries them.</summary>
    public const string ProductDirectory = "content/worldrpg/" + AuthoredUiAssetSet.RelativeRoot;

    public static readonly CommandOption Manifest = CommandOption.Staged("--ui-authored-assets", "FILE", "data/ui-authored-assets.json");
    public static readonly CommandOption Originals = CommandOption.Staged("--ui-original", "DIR", ProductDirectory);

    /// <summary>
    /// The classic media profile the authored UI manifest and its originals describe; when neither option is
    /// supplied and they are not required, the profile carries no authored art.
    /// </summary>
    public static Arena2ClassicMediaProfile Profile(CommandArguments args, bool required)
    {
        bool manifest = args.Has(Manifest.Name);
        if (manifest != args.Has(Originals.Name) || (required && !manifest))
        {
            throw args.Invalid($"{Manifest.Name} and {Originals.Name} are supplied together.");
        }

        if (!manifest) return new Arena2ClassicMediaProfile();
        string originalsRoot = Path.GetFullPath(args[Originals.Name]);
        if (!Directory.Exists(originalsRoot)) throw new DirectoryNotFoundException($"The authored UI original directory '{originalsRoot}' was not found.");
        return AuthoredUiAssetSet.Read(
            PayloadFiles.ReadBounded(args[Manifest.Name], "authored UI manifest"),
            leaf => PayloadFiles.ReadBounded(Path.Combine(originalsRoot, leaf), $"authored UI source '{leaf}'"));
    }
}
