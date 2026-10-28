using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Kit;

/// <summary>
/// One content container installed beside the product and opened by its host for the product's life: its file
/// name, the open collection, and the description it carries of itself.
/// </summary>
/// <remarks>
/// <para>
/// A container is content that is installed and replaced on its own, such as data a player converts from files
/// the product does not ship. Its description (<see cref="DescriptionPath"/>) names the revision of the tool that
/// produced it and the content packs it carries; a pack it describes resolves exactly as a product content pack
/// does, its payload read from the container.
/// </para>
/// <para>
/// The Kit knows only that a pack may live in an installed container. Which containers a product opens, which it
/// requires and whether one is current are its ruleset's and host's: see <see cref="IInstalledContentRuleset"/>.
/// The host owns the collection and disposes it; this wrapper never does.
/// </para>
/// </remarks>
public sealed class InstalledContent
{
    /// <summary>The container-relative path of a container's description of itself.</summary>
    public const string DescriptionPath = "worldrpg/data-container.json";

    /// <summary>The <c>kind</c> a container description declares.</summary>
    public const string DescriptionKind = "worldrpg.data-container";

    private HashSet<string>? _paths;
    private InstalledContentDescription? _description;

    public InstalledContent(string name, ProductContentBundle container)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Container = container ?? throw new ArgumentNullException(nameof(container));
    }

    /// <summary>The container's file name, as the host opened it.</summary>
    public string Name { get; }

    /// <summary>The open collection; its files are read when first used.</summary>
    public ProductContentBundle Container { get; }

    /// <summary>Whether the container holds a file at this container-relative path.</summary>
    public bool Contains(string path) => (_paths ??= [.. Container.Entries.ToArray().Select(entry => entry.Path)]).Contains(path);

    /// <summary>
    /// The container's description, or null when it carries none. A description that is not one is refused,
    /// since the container would otherwise be used without the facts that say what it is.
    /// </summary>
    /// <exception cref="InvalidDataException">The description is malformed.</exception>
    public InstalledContentDescription? Description => _description ??= Contains(DescriptionPath) ? InstalledContentDescription.Read(Name, Container.ReadBytes(DescriptionPath)) : null;
}

/// <summary>What a container says of itself: the revision of the tool that produced it and the packs it carries.</summary>
/// <param name="Revision">The producing tool's revision, which a ruleset compares with the one it was built with.</param>
/// <param name="ContentPacks">Each content pack descriptor the container carries, as its JSON.</param>
public sealed record InstalledContentDescription(string Revision, IReadOnlyList<JsonElement> ContentPacks)
{
    internal static InstalledContentDescription Read(string container, ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != InstalledContent.DescriptionKind)
                throw new InvalidDataException($"its kind is not '{InstalledContent.DescriptionKind}'");
            if (!root.TryGetProperty("importerRevision", out JsonElement revision) || revision.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(revision.GetString()))
                throw new InvalidDataException("it names no 'importerRevision'");
            if (!root.TryGetProperty("contentPacks", out JsonElement packs) || packs.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("'contentPacks' is not an array");
            return new(revision.GetString()!, [.. packs.EnumerateArray().Select(pack => pack.Clone())]);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The description of '{container}' is not valid JSON: {exception.Message}", exception);
        }
        catch (InvalidDataException exception) when (exception.InnerException is null)
        {
            throw new InvalidDataException($"The description of '{container}' is malformed: {exception.Message}.");
        }
    }
}

/// <summary>A container a ruleset reads, which the host opens beside the product before composing.</summary>
/// <param name="Name">The container's file name, resolved beside the product (in a release) or in the project's installed-content directory (under the development loop).</param>
/// <param name="Title">What the player knows the container as, for the messages that name it.</param>
/// <param name="Required">Whether the ruleset's games compose without it; an optional container that is absent is left out.</param>
/// <param name="DevelopmentStep">What a developer does to produce it, shown when it is absent, unreadable or stale under the development loop.</param>
/// <param name="ReleaseStep">What a player does to produce it, shown in a release.</param>
/// <param name="Import">How the entry screen produces it from a folder the player chooses; null when it cannot.</param>
public sealed record InstalledContentRequirement(string Name, string Title, bool Required, string DevelopmentStep, string ReleaseStep,
    InstalledContentImport? Import = null);

/// <summary>
/// How a container is produced from the player's own files on the entry screen: the product's declared tool that
/// writes it, what the folder picker asks for, and the tool's arguments for the chosen folder and the directory
/// the host opens installed containers from.
/// </summary>
/// <param name="Tool">The declared tool's project name (<c>RustyEngineProductTool</c>).</param>
/// <param name="FolderPrompt">The folder picker's title.</param>
/// <param name="Arguments">The tool's arguments for the chosen folder and the installed-content directory.</param>
public sealed record InstalledContentImport(string Tool, string FolderPrompt, Func<string, string, IReadOnlyList<string>> Arguments);

/// <summary>
/// Optional ruleset seam for content installed beside the product: the containers the ruleset's games read, whether
/// an opened one is current for this build, and the session the product runs when the content cannot be used.
/// </summary>
public interface IInstalledContentRuleset
{
    /// <summary>The containers the host opens for this ruleset, before any of its games is composed.</summary>
    IReadOnlyList<InstalledContentRequirement> InstalledContent { get; }

    /// <summary>
    /// Why an opened container is not the one this build reads, such as one produced by another revision of its
    /// tool; null when it is current.
    /// </summary>
    string? CheckInstalledContent(InstalledContent content);

    /// <summary>
    /// The session the product runs when no game can be played: it shows the entry screen with the reason and
    /// the next step, and offers nothing to play.
    /// </summary>
    IGameSession CreateUnavailableSession(GameUnavailableContext context);
}

/// <summary>
/// What an unavailable session shows: the problems that stop play, each with the step that fixes it, and the
/// import that produces the required container from the entry screen, when the ruleset declares one.
/// </summary>
public sealed record GameUnavailableContext(IEngineContext Engine, IReadOnlyList<string> Problems, InstalledContentRequirement? Importable = null);

/// <summary>
/// An unavailable session that can produce its missing container: once its import has installed it, the host
/// opens the installed containers again and composes the game.
/// </summary>
public interface IInstalledContentImportSession : IGameSession
{
    /// <summary>Whether an import has installed the container since the last take, so the host should open the game.</summary>
    bool TakeReopenRequest();
}
