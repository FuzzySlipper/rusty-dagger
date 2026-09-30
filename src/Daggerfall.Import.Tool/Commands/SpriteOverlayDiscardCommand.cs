using WorldRpg.SpriteAuthoring;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>Moves an authored sprite overlay to its recovery path.</summary>
internal static class SpriteOverlayDiscardCommand
{
    public static ToolCommand Command { get; } = new("sprite-overlay-discard",
        [SpriteOverlays.Publication, SpriteOverlays.Authoring, SpriteOverlays.Overlay], Run);

    private static int Run(CommandArguments args)
    {
        Console.WriteLine(SpriteAuthoredOverlayStore.Discard(SpriteOverlays.PublicationDirectory(args), SpriteOverlays.AuthoringDirectory(args), SpriteOverlays.OverlayPath(args))
            ? "sprite overlay moved to its .discarded recovery path"
            : "sprite overlay was not present");
        return 0;
    }
}
