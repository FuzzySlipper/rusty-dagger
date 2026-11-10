using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// One layer of a figure's stack: the body (the clip the game chooses), a standing clip that always plays, or the
/// companion named from the body clip by <c>{clip}</c>.
/// </summary>
public sealed record PartsRigLayer(string Name, uint Track, bool Body, string? Clip, string? Companion, bool Loop, float Alpha)
{
    /// <summary>This companion's clip for <paramref name="body"/>.</summary>
    public string CompanionOf(string body) => Companion!.Replace("{clip}", body, StringComparison.Ordinal);
}

/// <summary>
/// How a figure's clips play together (<c>parts-rig-layers/1</c>): each layer on its own track, a higher track replacing
/// only what its clip keys. Exactly one layer is the body, on track 0. A standing layer loops its clip for as long as
/// the figure shows; a companion plays the clip named from the body clip while the rig holds one.
/// </summary>
public sealed class PartsRigLayerStack
{
    public const string Spec = "parts-rig-layers/1";

    private PartsRigLayerStack(IReadOnlyList<PartsRigLayer> layers) => Layers = layers;

    public IReadOnlyList<PartsRigLayer> Layers { get; }

    /// <summary>Reads a stack document.</summary>
    /// <exception cref="InvalidOperationException">It is another spec, or its layers are not one body on track 0 and distinct tracks each with one clip or companion.</exception>
    public static PartsRigLayerStack Read(JsonElement stack)
    {
        try
        {
            if (stack.GetProperty("spec").GetString() != Spec)
                throw new InvalidOperationException($"A layer stack is {stack.GetProperty("spec")}, not {Spec}.");
            List<PartsRigLayer> layers = [];
            foreach (JsonElement layer in stack.GetProperty("layers").EnumerateArray())
            {
                string name = layer.GetProperty("name").GetString()!;
                bool body = layer.TryGetProperty("body", out JsonElement flag) && flag.GetBoolean();
                string? clip = layer.TryGetProperty("clip", out JsonElement c) ? c.GetString() : null;
                string? companion = layer.TryGetProperty("companion", out JsonElement m) ? m.GetString() : null;
                float alpha = layer.TryGetProperty("alpha", out JsonElement a) ? a.GetSingle() : 1F;
                if ((body ? 1 : 0) + (clip is null ? 0 : 1) + (companion is null ? 0 : 1) != 1
                    || companion?.Contains("{clip}", StringComparison.Ordinal) == false || alpha is not (>= 0F and <= 1F))
                    throw new InvalidOperationException($"Layer '{name}' is not one of a body, a clip at an alpha from 0 to 1, or a companion naming {{clip}}.");
                layers.Add(new(name, layer.GetProperty("track").GetUInt32(), body, clip, companion,
                    !layer.TryGetProperty("loop", out JsonElement loop) || loop.GetBoolean(), alpha));
            }
            if (layers.Count(layer => layer.Body) != 1 || layers.Single(layer => layer.Body).Track != 0
                || layers.DistinctBy(layer => layer.Track).Count() != layers.Count)
                throw new InvalidOperationException("A layer stack has one body layer on track 0 and its layers on distinct tracks.");
            return new([.. layers.OrderBy(layer => layer.Track)]);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException($"A layer stack is unreadable: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Puts <paramref name="body"/> on object <paramref name="id"/>, in from <paramref name="previous"/> (null when the
    /// object has played nothing, which starts the standing layers too) over <paramref name="crossfadeSeconds"/>: the body
    /// clip on track 0 at its authored speed, and each companion of it the rig holds (<paramref name="clips"/>); a
    /// companion the previous body clip had and this one has not fades out.
    /// </summary>
    public void Play(IPartsRigsService rigs, ulong id, string body, string? previous, IReadOnlySet<string> clips, float crossfadeSeconds)
    {
        float fade = previous is null ? 0F : crossfadeSeconds;
        rigs.Play(new PartsRigPlayRequest(id, body, true, 1F, fade));
        foreach (PartsRigLayer layer in Layers)
        {
            if (layer.Clip is { } clip && previous is null)
                rigs.Play(PartsRigPlayRequest.Layer(id, layer.Track, clip, layer.Alpha, false, 0F, layer.Loop));
            if (layer.Companion is null) continue;
            if (clips.Contains(layer.CompanionOf(body)))
                rigs.Play(PartsRigPlayRequest.Layer(id, layer.Track, layer.CompanionOf(body), layer.Alpha, false, fade, layer.Loop));
            else if (previous is not null && clips.Contains(layer.CompanionOf(previous)))
                rigs.Stop(new PartsRigStopRequest(id, fade, layer.Track));
        }
    }
}
