using Rusty.Engine;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// Publishes one stream's projection from a builder, and only when it differs from the last one published. The Engine
/// sends no unchanged value on either way; skipping it here also spares the call, the copy and the allocation, so a
/// projection rebuilt every update costs nothing while it stands still. The Engine copies a projection while it is
/// published; the value published is held in this publisher's own storage, which only the next projection it publishes
/// rewrites.
/// </summary>
public sealed class UiProjectionPublisher(IUiService ui, UiStream stream)
{
    private readonly IUiService _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    private readonly UiStream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private StructuredValueNode[] _nodes = [];
    private uint[] _edges = [];
    private byte[] _utf8 = [];
    private int _nodeCount, _edgeCount, _utf8Count;
    private uint _root;
    private bool _published;

    /// <summary>Publishes the builder's value rooted at <paramref name="root"/> unless it is the one last published.</summary>
    /// <returns>Whether it was published.</returns>
    public bool Publish(UiValueBuilder builder, uint root)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (root >= (uint)builder.Nodes.Length) throw new ArgumentOutOfRangeException(nameof(root));
        if (_published && root == _root
            && builder.Nodes.SequenceEqual(_nodes.AsSpan(0, _nodeCount))
            && builder.Edges.SequenceEqual(_edges.AsSpan(0, _edgeCount))
            && builder.Utf8.SequenceEqual(_utf8.AsSpan(0, _utf8Count)))
            return false;

        _nodeCount = Keep(builder.Nodes, ref _nodes);
        _edgeCount = Keep(builder.Edges, ref _edges);
        _utf8Count = Keep(builder.Utf8, ref _utf8);
        _root = root;
        _published = true;
        _ui.PublishProjection(new UiProjection(_stream,
            new UiValue(_nodes.AsMemory(0, _nodeCount), _edges.AsMemory(0, _edgeCount), root, _utf8.AsMemory(0, _utf8Count))));
        return true;
    }

    private static int Keep<T>(ReadOnlySpan<T> source, ref T[] storage)
    {
        if (source.Length > storage.Length) storage = new T[Math.Max(source.Length, storage.Length * 2)];
        source.CopyTo(storage);
        return source.Length;
    }
}
