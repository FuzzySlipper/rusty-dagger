using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Which of the classic UI images an art set draws anew, so the session reads them only for classic art.</summary>
internal static class DaggerfallUiArtChoice
{
    /// <summary>
    /// Whether <paramref name="art"/> draws a classic UI image anew: an image it replaces, a glint it redraws, or a spell
    /// atlas every cell of which has its own image (<see cref="DaggerfallSpellArt"/>), since the page draws a cell from the
    /// atlas only where it has none.
    /// </summary>
    internal static Func<string, bool> DrawnAnew(DaggerfallArtReplacements art)
    {
        DaggerfallSpellArt spells = DaggerfallSpellArt.From(art);
        bool icons = spells.Icons.All(image => image is not null);
        bool selectors = spells.Targets.Concat(spells.Elements).All(image => image is not null);
        return id => art.For(id) is not null || art.Effects.ContainsKey(id)
            || icons && id == DaggerfallSpellConstruction.IconAtlasMedia || selectors && id == DaggerfallSpellConstruction.SelectorAtlasMedia;
    }
}
