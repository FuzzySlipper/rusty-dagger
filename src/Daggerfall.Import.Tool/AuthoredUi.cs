using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool;

/// <summary>Reads the tracked authored UI input through its two explicit paths.</summary>
internal static class AuthoredUi
{
    public static readonly CommandOption Manifest = CommandOption.Required("--ui-authored-assets", "FILE");
    public static readonly CommandOption Originals = CommandOption.Required("--ui-original", "DIR");

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
