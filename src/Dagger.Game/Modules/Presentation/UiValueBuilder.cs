using System.Text;
using Rusty.Engine;

namespace RustyDagger.Game.Modules.Presentation;

/// <summary>Builds Dagger's safe structured UI value without retaining borrowed storage.</summary>
internal sealed class UiValueBuilder
{
    private const uint NullKind = 0;
    private const uint NumberKind = 2;
    private const uint StringKind = 3;
    private const uint ObjectKind = 5;

    private readonly List<StructuredValueNode> _nodes = [];
    private readonly List<uint> _edges = [];
    private readonly List<byte> _utf8 = [];

    public uint Null() => Add(NullKind);

    public uint Number(double value) => Add(NumberKind, numberValue: value);

    public uint String(string value)
    {
        (uint offset, uint length) = Bytes(value);
        return Add(StringKind, textOffset: offset, textLength: length);
    }

    public uint Object(params (string Key, uint Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        uint firstEdge = checked((uint)_edges.Count);
        foreach ((string key, uint value) in fields)
        {
            if (value >= (uint)_nodes.Count) throw new ArgumentOutOfRangeException(nameof(fields));
            (uint offset, uint length) = Bytes(key);
            StructuredValueNode node = _nodes[checked((int)value)];
            uint keyedValue = checked((uint)_nodes.Count);
            _nodes.Add(node with { KeyOffset = offset, KeyLen = length });
            _edges.Add(keyedValue);
        }
        return Add(ObjectKind, firstEdge: firstEdge, childCount: checked((uint)fields.Length));
    }

    public UiValue Build(uint root)
    {
        if (root >= (uint)_nodes.Count) throw new ArgumentOutOfRangeException(nameof(root));
        return new UiValue(_nodes.ToArray(), _edges.ToArray(), root, _utf8.ToArray());
    }

    private uint Add(uint kind, uint boolValue = 0, double numberValue = 0, uint textOffset = 0, uint textLength = 0, uint firstEdge = 0, uint childCount = 0)
    {
        uint index = checked((uint)_nodes.Count);
        _nodes.Add(new StructuredValueNode(kind, boolValue, numberValue, KeyOffset: 0, KeyLen: 0, textOffset, textLength, firstEdge, childCount));
        return index;
    }

    private (uint Offset, uint Length) Bytes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        uint offset = checked((uint)_utf8.Count);
        _utf8.AddRange(bytes);
        return (offset, checked((uint)bytes.Length));
    }
}
