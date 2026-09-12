namespace Daggerfall.Import.Arena2;

/// <summary>Whether a published consumer binds one supplied media file.</summary>
/// <remarks>
/// This answers the binding question alone. Whether a file reads is a separate fact, carried by
/// <see cref="Arena2CanvasKind"/>, so a file can be bound and unread, or unbound and read; a
/// single enum cannot say both without making one of them unrepresentable.
/// </remarks>
public enum MediaBinding
{
    /// <summary>A published consumer binds the file.</summary>
    Admitted,

    /// <summary>The file is supplied and no published consumer binds it yet.</summary>
    RequiredPending,
}
