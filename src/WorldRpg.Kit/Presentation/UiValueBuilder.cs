using System.Text;
using Rusty.Engine;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// Builds a safe structured UI value without retaining borrowed storage. A builder keeps its storage when cleared, so
/// one rebuilt every update grows once and then allocates nothing for the value itself.
/// </summary>
public sealed class UiValueBuilder
{
    private const StructuredValueKind NullKind = StructuredValueKind.Null;
    private const StructuredValueKind NumberKind = StructuredValueKind.Number;
    private const StructuredValueKind StringKind = StructuredValueKind.String;
    private const StructuredValueKind ArrayKind = StructuredValueKind.Array;
    private const StructuredValueKind ObjectKind = StructuredValueKind.Object;

    private StructuredValueNode[] _nodes = new StructuredValueNode[64];
    private uint[] _edges = new uint[64];
    private byte[] _utf8 = new byte[1024];
    private int _nodeCount, _edgeCount, _utf8Count;

    /// <summary>The nodes written since the builder was made or last cleared.</summary>
    public ReadOnlySpan<StructuredValueNode> Nodes => _nodes.AsSpan(0, _nodeCount);

    /// <summary>The edges written since the builder was made or last cleared.</summary>
    public ReadOnlySpan<uint> Edges => _edges.AsSpan(0, _edgeCount);

    /// <summary>The UTF-8 text written since the builder was made or last cleared.</summary>
    public ReadOnlySpan<byte> Utf8 => _utf8.AsSpan(0, _utf8Count);

    /// <summary>Forgets every value written, keeping the storage for the next.</summary>
    public void Clear() => _nodeCount = _edgeCount = _utf8Count = 0;

    public uint Null() => Add(NullKind);

    public uint Boolean(bool value) => Add(StructuredValueKind.Bool, boolValue: value ? 1u : 0u);

    public uint Number(double value) => Add(NumberKind, numberValue: value);

    public uint String(string value)
    {
        (uint offset, uint length) = Bytes(value);
        return Add(StringKind, textOffset: offset, textLength: length);
    }

    public uint Object(params (string Key, uint Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        uint firstEdge = checked((uint)_edgeCount);
        foreach ((string key, uint value) in fields)
        {
            if (value >= (uint)_nodeCount) throw new ArgumentOutOfRangeException(nameof(fields));
            (uint offset, uint length) = Bytes(key);
            StructuredValueNode node = _nodes[checked((int)value)];
            uint keyedValue = checked((uint)_nodeCount);
            AddNode(node with { KeyOffset = offset, KeyLen = length });
            AddEdge(keyedValue);
        }
        return Add(ObjectKind, firstEdge: firstEdge, childCount: checked((uint)fields.Length));
    }

    public uint Array(params uint[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        uint firstEdge = checked((uint)_edgeCount);
        foreach (uint value in values)
        {
            if (value >= (uint)_nodeCount) throw new ArgumentOutOfRangeException(nameof(values));
            AddEdge(value);
        }
        return Add(ArrayKind, firstEdge: firstEdge, childCount: checked((uint)values.Length));
    }

    /// <summary>A value of its own, copied out of the builder, rooted at <paramref name="root"/>.</summary>
    public UiValue Build(uint root)
    {
        if (root >= (uint)_nodeCount) throw new ArgumentOutOfRangeException(nameof(root));
        return new UiValue(Nodes.ToArray(), Edges.ToArray(), root, Utf8.ToArray());
    }

    private uint Add(StructuredValueKind kind, uint boolValue = 0, double numberValue = 0, uint textOffset = 0, uint textLength = 0, uint firstEdge = 0, uint childCount = 0)
    {
        uint index = checked((uint)_nodeCount);
        AddNode(new StructuredValueNode(kind, boolValue, numberValue, KeyOffset: 0, KeyLen: 0, textOffset, textLength, firstEdge, childCount));
        return index;
    }

    private void AddNode(StructuredValueNode node)
    {
        Reserve(ref _nodes, _nodeCount + 1);
        _nodes[_nodeCount++] = node;
    }

    private void AddEdge(uint edge)
    {
        Reserve(ref _edges, _edgeCount + 1);
        _edges[_edgeCount++] = edge;
    }

    private (uint Offset, uint Length) Bytes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Reserve(ref _utf8, _utf8Count + Encoding.UTF8.GetByteCount(value));
        int written = Encoding.UTF8.GetBytes(value, _utf8.AsSpan(_utf8Count));
        uint offset = checked((uint)_utf8Count);
        _utf8Count += written;
        return (offset, checked((uint)written));
    }

    private static void Reserve<T>(ref T[] storage, int needed)
    {
        if (needed > storage.Length) System.Array.Resize(ref storage, Math.Max(needed, storage.Length * 2));
    }
}
