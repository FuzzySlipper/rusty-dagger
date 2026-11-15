using System.Numerics;
using System.Text;
using System.Text.Json;
using Rusty.Engine.Debugging;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How the developer commands and the playtest readouts answer: a refusal, or JSON written in one pass.</summary>
internal static class DaggerfallDeveloperReplies
{
    internal static DebugCommandResult Refused(string reason) => DebugCommandResult.Failure(DebugCommandStatus.Failed, reason);

    internal static DebugCommandResult Json(Action<Utf8JsonWriter> write)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream)) write(writer);
        return DebugCommandResult.Success(Encoding.UTF8.GetString(stream.ToArray()));
    }

    internal static void Vector(Utf8JsonWriter w, string name, Vector3 value)
    {
        w.WriteStartObject(name); w.WriteNumber("x", value.X); w.WriteNumber("y", value.Y); w.WriteNumber("z", value.Z); w.WriteEndObject();
    }
}
