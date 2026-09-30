using Daggerfall.Import.Publication;
using WorldRpg.SpriteAuthoring;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Writes the neutral inspection document an authoring tool reads instead of the publication's sidecars. It
/// is derived output, so it may never land inside the publication it describes: a file there would disagree
/// with the publication manifest.
/// </summary>
internal static class SpriteInspectionCommand
{
    public static ToolCommand Command { get; } = new("sprite-inspection", [SpriteOverlays.Publication, CommandOption.Required("--output", "FILE")], Run);

    private static int Run(CommandArguments args)
    {
        string publicationDirectory = SpriteOverlays.PublicationDirectory(args);
        if (string.IsNullOrWhiteSpace(args["--output"])) throw new ArgumentException("--output must name a file.");
        string output = Path.GetFullPath(args["--output"]);
        if (output.StartsWith(Path.TrimEndingDirectorySeparator(publicationDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("--output must name a file outside the generated publication directory.");
        }

        SpriteInspectionDocument document = SpritePublicationReader.Read(publicationDirectory).ToInspectionDocument();
        PayloadFiles.WriteFile(output, SpriteInspectionDocumentSerializer.Serialize(document));
        Console.WriteLine("sprite inspection document written");
        return 0;
    }
}
