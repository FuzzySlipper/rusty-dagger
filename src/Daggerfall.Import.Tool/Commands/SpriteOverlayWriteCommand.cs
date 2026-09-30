using Daggerfall.Import.Publication;
using WorldRpg.SpriteAuthoring;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>Writes an authored sprite overlay into an authoring root after validating it against the site closure.</summary>
internal static class SpriteOverlayWriteCommand
{
    public static ToolCommand Command { get; } = new("sprite-overlay-write",
        [SpriteOverlays.Publication, SpriteOverlays.Authoring, SpriteOverlays.Overlay, CommandOption.Required("--input", "FILE")], Run);

    private static int Run(CommandArguments args)
    {
        string publicationDirectory = SpriteOverlays.PublicationDirectory(args);
        string authoring = SpriteOverlays.AuthoringDirectory(args);
        string overlayPath = SpriteOverlays.OverlayPath(args);
        SpritePublicationSnapshot publication = SpritePublicationReader.Read(publicationDirectory);
        SpriteAuthoredOverlayDocument overlay = SpriteAuthoredOverlayStore.Read(SpriteOverlays.ReadBytes(args["--input"]));
        SpriteAuthoredOverlayStore.Write(publicationDirectory, authoring, overlayPath, overlay, publication.Catalog, publication.AuthoringBasisDigest);
        Console.WriteLine("sprite overlay written; a later import write consumes it through --sprite-authoring and --sprite-overlay.");
        return 0;
    }
}
