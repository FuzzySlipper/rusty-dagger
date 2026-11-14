using System.Runtime.CompilerServices;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Text's reading of the definitions: their text set, resolved through Text's presentation owner.</summary>
internal static class DaggerfallDefinitionsText
{
    // One resolver per text set, built on its first read and kept beside the set while the set lives, so definitions
    // that share a text set (a session's copy) share its resolver and the inventory's projection builds none.
    private static readonly ConditionalWeakTable<DaggerfallTextSet, DaggerfallTextResolver> Resolvers = new();

    extension(DaggerfallDefinitions definitions)
    {
        /// <summary>The Daggerfall presentation owner for normalized lookup, layout and global macro expansion.</summary>
        internal DaggerfallTextResolver TextPresentation => Resolvers.GetValue(definitions.Text, static text => new(text));
    }
}
