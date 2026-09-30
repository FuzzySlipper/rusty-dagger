using Daggerfall.Import.Publication;
using WorldRpg.SpriteAuthoring;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>Validates an authored sprite overlay against the site closure it was written for.</summary>
internal static class SpriteOverlayValidateCommand
{
    public static ToolCommand Command { get; } = new("sprite-overlay-validate",
        [SpriteOverlays.Publication, SpriteOverlays.Authoring, SpriteOverlays.Overlay], Run);

    private static int Run(CommandArguments args)
    {
        string publicationDirectory = SpriteOverlays.PublicationDirectory(args);
        string authoring = SpriteOverlays.AuthoringDirectory(args);
        string overlayPath = SpriteOverlays.OverlayPath(args);
        SpritePublicationSnapshot publication = SpritePublicationReader.Read(publicationDirectory);
        SpriteAuthoredOverlayStore.ValidateRootSeparation(publicationDirectory, authoring);
        SpriteAuthoredOverlayDocument overlay = SpriteAuthoredOverlayStore.Read(SpriteOverlays.ReadBytes(authoring, overlayPath));
        SpriteAuthoredOverlayStore.Validate(overlay, publication.Catalog, publication.AuthoringBasisDigest);
        Console.WriteLine("sprite overlay is valid");
        return 0;
    }
}
