using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Interaction;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>What a session offers the host's debug catalogue, reached in a test as the host reaches it.</summary>
internal static class SessionCommands
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The one module of a type the session offers: a Developer owner, or an Engine module over one.</summary>
    internal static T Commands<T>(this DaggerfallSession session) where T : IDebugCommandModule => session.DebugCommands.OfType<T>().Single();

    /// <summary><c>playtest.action</c>'s answer, read back as the record it is written from.</summary>
    internal static PlaytestAction PlaytestAction(this DaggerfallSession session, string id) =>
        JsonSerializer.Deserialize<PlaytestAction>(session.Commands<PlaytestDebugModule>().Action(id).Message, Web)!;

    /// <summary>The activation's interaction inspection along the player's look now, as <c>interaction.inspect</c> reads it.</summary>
    internal static WorldInteraction InteractionInspection(this DaggerfallSession session)
    {
        LookReceipt look = Look.Integrate(new(new(session.State.PlayerControl.YawRadians, session.State.PlayerControl.PitchRadians), Vector2.Zero,
            new LookConfig(1f, 1f, -MathF.PI / 2f, MathF.PI / 2f, MathF.Tau, false, false, true)));
        return session.Activation.Module!.CreateInspection(session.State.Actors.Player.Actor.Entity, session.State.PlayerControl, look);
    }
}
