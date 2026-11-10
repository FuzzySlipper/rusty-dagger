using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer look at the people who walk the world: draws every walking person as the ruleset's parts-rig test figure,
/// seen from a set of directions, to judge the figure in play and what many of them cost. This exists only as a
/// live-debug command; no game surface offers it.
/// </summary>
public interface IDeveloperPeopleSession
{
    /// <summary>Draws every walking person as the test figure in the view set <paramref name="set"/> names, or (<c>off</c>) as their own art again.</summary>
    DebugCommandResult RigPeople(string set);
}

/// <summary>The live-debug command over <see cref="IDeveloperPeopleSession"/>; the delegate follows session replacement.</summary>
public sealed class DeveloperPeopleDebugModule(Func<IDeveloperPeopleSession> current) : IDebugCommandModule
{
    [DebugCommand("people.rig", Description = "Developer: draw every walking person as the parts-rig test figure seen from SET directions (4 or 8), walking while they walk; off draws their own art again.")]
    public DebugCommandResult Rig(string set) => current().RigPeople(set);
}
