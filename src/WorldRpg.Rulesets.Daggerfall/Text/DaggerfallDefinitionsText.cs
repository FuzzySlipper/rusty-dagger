using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Text's reading of the definitions: their text set, resolved through Text's presentation owner.</summary>
internal static class DaggerfallDefinitionsText
{
    extension(DaggerfallDefinitions definitions)
    {
        /// <summary>
        /// The Daggerfall presentation owner for normalized lookup, layout and global macro expansion, over the
        /// definitions' current text. It holds nothing but that text set, so each read answers alike.
        /// </summary>
        internal DaggerfallTextResolver TextPresentation => new(definitions.Text);
    }
}
