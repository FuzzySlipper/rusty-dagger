using System.Net;
using System.Text;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Source.Tool.ReferenceExports;

internal static class NpcReferencePages
{
    public static void Write(ReferenceExport export, NpcReferenceWorld world, IReadOnlyDictionary<int, ClassicFaction> factions,
        IReadOnlyList<NpcEntry> entries)
    {
        NpcEntry[] cards = [.. entries.OrderBy(entry => entry.Id, StringComparer.Ordinal)];
        var aliases = entries.SelectMany(entry => entry.Aliases.Select(alias => (alias, entry.Id)))
            .GroupBy(item => item.alias).ToDictionary(group => group.Key, group => group.Select(item => item.Id).Distinct().ToArray());
        foreach (NpcEntry entry in entries) WriteDetail(export, world, factions, entry);

        export.WriteJson("manifest.json", new
        {
            entries = cards.Select(entry => new { entry.Id, entry.Name, entry.Kind, entry.Aliases, image = entry.Thumbnail?.File }),
            aliases = aliases.Where(pair => pair.Value.Length == 1).ToDictionary(pair => pair.Key, pair => pair.Value[0]),
            sharedAliases = aliases.Where(pair => pair.Value.Length > 1).ToDictionary(pair => pair.Key, pair => pair.Value),
        });
        File.WriteAllText(Path.Combine(export.Root, "style.css"), Style);
        File.WriteAllText(Path.Combine(export.Root, "filter.js"), Filter);
        StringBuilder page = Page("Daggerfall NPC Reference", "");
        page.Append("""
          <header><h1>Daggerfall NPC Reference</h1>
          <div class="tools"><label>Find an NPC<input id="search" type="search" placeholder="Name or reference ID" autocomplete="off"></label>
          <label>Type<select id="kind"><option value="">All NPCs</option>
          """);
        foreach (string kind in cards.Select(entry => entry.Kind).Distinct())
            page.Append($"<option>{H(kind)}</option>");
        page.Append($"</select></label><output id=\"count\" aria-live=\"polite\">{Count(cards.Length, "reference")}</output></div></header><main>");
        page.Append("<ul id=\"chart\" class=\"chart\">");
        foreach (NpcEntry entry in cards)
        {
            string haystack = string.Join(' ', new[] { entry.Id, entry.Name }.Concat(entry.Aliases));
            page.Append($"""
              <li data-id="{H(entry.Id)}" data-search="{H(haystack.ToLowerInvariant())}" data-kind="{H(entry.Kind)}">
              <a class="card" href="{H(entry.Id)}/index.html">
              {Picture(entry.Thumbnail, "", "")}
              <span class="card-name">{H(entry.Name)}</span><code>{H(entry.Id)}</code>
              </a></li>
              """);
        }
        page.Append("</ul><p id=\"empty\" hidden>No matching NPCs.</p></main><script src=\"filter.js\"></script></body></html>");
        File.WriteAllText(Path.Combine(export.Root, "index.html"), page.ToString());
        Console.WriteLine($"Daggerfall NPC reference: {cards.Length} cards, {entries.Count} detail pages → {export.Root}");
    }

    private static void WriteDetail(ReferenceExport export, NpcReferenceWorld world, IReadOnlyDictionary<int, ClassicFaction> factions, NpcEntry entry)
    {
        string directory = Path.Combine(export.Root, entry.Id);
        Directory.CreateDirectory(directory);
        StringBuilder page = Page($"{entry.Name} · {entry.Id}", "../");
        page.Append($"""
          <header><nav><a href="../index.html">← All NPCs</a></nav>
          <h1>{H(entry.Name)}</h1><p class="identity"><code>{H(entry.Id)}</code> · {H(entry.Kind)}</p>
          </header><main>
          """);
        if (entry.Facts.Count > 0)
        {
            page.Append("<dl>");
            foreach ((string label, string value) in entry.Facts)
                page.Append($"<div><dt>{H(label)}</dt><dd>{H(value)}</dd></div>");
            page.Append("</dl>");
        }
        if (entry.Links.Count > 0)
        {
            page.Append("<ul class=\"links\" aria-label=\"Related references\">");
            foreach (NpcLink link in entry.Links)
                page.Append($"<li><a href=\"{H(link.Href)}\">{H(link.Label)}</a></li>");
            page.Append("</ul>");
        }

        if (entry.Placements.Count > 0)
        {
            page.Append("<section><h2>World placements</h2><p>Locations use one or more blocks containing this reference. A block can be reused in multiple locations.</p>");
            foreach (var group in entry.Placements.GroupBy(use => (use.Setting, use.Pose))
                .OrderBy(group => group.Key.Setting).ThenBy(group => group.Key.Pose, StringComparer.Ordinal))
            {
                string[] blocks = [.. group.Select(use => use.Block).Distinct().Order(StringComparer.Ordinal)];
                var locations = world.Locations(blocks).GroupBy(location => world.Region(location.Region)).ToArray();
                int locationCount = locations.Sum(region => region.Count());
                string setting = group.Key.Setting == NpcSetting.Dungeon ? "Dungeon or palace interior" : group.Key.Setting.ToString();
                if (group.Key.Pose is { } pose) setting += $" · {pose}";
                string title = $"{setting} · {Count(locationCount, "location")}";
                page.Append($"<details{(locationCount <= 12 ? " open" : "")}><summary>{H(title)}</summary>");
                foreach (var region in locations)
                {
                    page.Append($"<div class=\"region\"><strong>{H(region.Key)}</strong><ul>");
                    foreach (NpcLocation location in region)
                        page.Append($"<li title=\"MAPS.BSA region {location.Region}, location {location.Index}\">{H(location.Name)}</li>");
                    page.Append("</ul></div>");
                }
                page.Append($"<details class=\"blocks\"><summary>{Count(blocks.Length, "source block")} · {Count(group.Count(), "placement")} in block templates</summary><ul>");
                foreach (var block in group.GroupBy(use => use.Block).OrderBy(block => block.Key, StringComparer.Ordinal))
                {
                    string context = string.Join(", ", block.GroupBy(use => use.Faction).OrderBy(byFaction => byFaction.Key)
                        .Select(byFaction => $"{byFaction.Count()} × {FactionLabel(factions, byFaction.Key)}"));
                    page.Append($"<li><code>{H(block.Key)}</code> · {H(context)}</li>");
                }
                page.Append("</ul></details></details>");
            }
            page.Append("</section>");
        }

        page.Append("<section><h2>Original art</h2>");
        if (entry.Aliases.Length > 0)
            page.Append($"<p>Source references · {H(string.Join(", ", entry.Aliases))}</p>");
        foreach (NpcArt group in entry.Art.Where(art => !art.Identical && art.Frames.Count > 0).OrderBy(art => art.Frames[0].Role == "portrait" ? 0 : 1))
        {
            page.Append($"<h3>{H(group.Label)}{(group.Frames.Count > 1 ? $" <small>· {group.Frames.Count} images</small>" : "")}</h3><ul class=\"frames\">");
            foreach (ReferenceImage frame in group.Frames)
            {
                string file = $"../art/{frame.File}";
                string source = Path.GetFileName(frame.Source);
                string caption = frame.Role == "portrait" ? $"Record {frame.Record}" : $"Record {frame.Record} · frame {frame.Frame}";
                page.Append($"""
                  <li><figure><a href="{H(file)}">{Picture(frame, "../", $"{source}, {caption}")}</a>
                  <figcaption>{H(source)}<br>{H(caption)}<br>{frame.Width} × {frame.Height}</figcaption></figure></li>
                  """);
            }
            page.Append("</ul>");
        }
        page.Append("</section><footer><a href=\"manifest.json\">Source manifest</a><a href=\"../index.html\">↑ All NPCs</a></footer></main></body></html>");
        File.WriteAllText(Path.Combine(directory, "index.html"), page.ToString());
        export.WriteJson($"{entry.Id}/manifest.json", new
        {
            entry.Id, entry.Name, entry.Kind, entry.Facts, entry.Aliases, entry.Art, entry.Placements,
            artRoot = "../art/",
        });
    }

    private static string FactionLabel(IReadOnlyDictionary<int, ClassicFaction> factions, int id) =>
        id == 0 ? "no faction" : factions.TryGetValue(id, out ClassicFaction? faction) ? $"{faction.Name} (#{id})" : $"faction #{id}";

    private static string Count(int count, string singular) => $"{count} {singular}{(count == 1 ? "" : "s")}";

    private static string Picture(ReferenceImage? image, string prefix, string label) => image is { } frame
        ? $"<span class=\"picture\"><img src=\"{H(prefix)}art/{H(frame.File)}\" alt=\"{H(label)}\" loading=\"lazy\" decoding=\"async\"></span>"
        : "<span class=\"picture\">No image</span>";

    private static string H(string value) => WebUtility.HtmlEncode(value);

    private static StringBuilder Page(string title, string prefix) => new($"""
      <!doctype html>
      <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
      <title>{H(title)}</title><link rel="stylesheet" href="{H(prefix)}style.css"></head><body>
      """);

    private const string Style = """
      :root {
        color-scheme: dark;
        --ink: #f3e9d5;
        --dim: #bcb0a0;
        --gold: #e5b77a;
        --border: #4b423a;
        --panel: #272624;
        background: #1c1b19;
        color: var(--ink);
        font: 15px/1.45 system-ui, sans-serif;
      }
      * { box-sizing: border-box; }
      body { margin: 0 auto; padding: 32px clamp(16px, 4vw, 64px) 48px; max-width: 1920px; }
      header { border-bottom: 1px solid var(--border); padding-bottom: 20px; margin-bottom: 20px; }
      h1, h2 { font-family: Georgia, serif; font-weight: normal; }
      h1 { font-size: clamp(2rem, 4vw, 3.4rem); line-height: 1.05; margin: 0 0 16px; }
      h2 { font-size: 1.8rem; margin-block: 24px 6px; }
      h3 { margin: 16px 0 8px; font-size: 1rem; }
      small, code, output, figcaption, dt, main p { color: var(--dim); }
      a { color: var(--gold); }
      a:focus-visible, input:focus-visible, select:focus-visible, summary:focus-visible { outline: 2px solid var(--gold); outline-offset: 3px; }
      .tools { display: flex; flex-wrap: wrap; align-items: end; gap: 12px; }
      .tools label { display: grid; gap: 4px; color: var(--dim); font-size: .85rem; }
      input, select { font: inherit; font-size: 1rem; min-height: 42px; width: 100%; padding: 6px 10px; border: 1px solid var(--border); border-radius: 5px; background: var(--panel); color: var(--ink); }
      input { width: min(310px, 82vw); }
      output { min-height: 33px; }
      .chart, .frames, .links { list-style: none; padding: 0; margin: 0; display: grid; gap: 10px; }
      .chart { grid-template-columns: repeat(auto-fill, minmax(126px, 1fr)); }
      .card { display: flex; flex-direction: column; height: 100%; gap: 4px; text-decoration: none; padding: 6px; background: var(--panel); border: 1px solid var(--border); border-radius: 6px; }
      .card:hover { border-color: var(--gold); background: #322c26; }
      .card-name { color: var(--ink); font-size: .88rem; line-height: 1.2; overflow-wrap: anywhere; }
      .card code { font-size: .73rem; margin-top: auto; padding-top: 3px; }
      .picture { display: grid; place-items: center; height: 116px; border-radius: 3px; background: radial-gradient(ellipse at 50% 55%, #52493d 0%, #302b25 75%); }
      .picture img { image-rendering: pixelated; display: block; width: 100%; height: 100%; min-width: 0; min-height: 0; object-fit: contain; padding: 5px; }
      nav { margin-bottom: 16px; }
      .identity { color: var(--dim); margin: 0; }
      dl { display: flex; flex-wrap: wrap; gap: 16px 32px; margin: 0 0 16px; }
      dt, figcaption { font-size: .8rem; }
      dd { margin: 2px 0 0; }
      .links { display: flex; flex-wrap: wrap; }
      .links a { display: block; padding: 5px 8px; border: 1px solid var(--border); border-radius: 5px; }
      details { padding: 8px 12px; margin: 6px 0; max-width: 900px; background: var(--panel); border: 1px solid var(--border); border-radius: 5px; }
      summary { cursor: pointer; }
      .region { padding-top: 8px; }
      .region ul { margin: 0; columns: 210px auto; }
      .blocks { margin-top: 10px; }
      .frames { grid-template-columns: repeat(auto-fill, minmax(108px, 136px)); }
      figure { margin: 0; }
      figcaption { margin-top: 4px; }
      footer { border-top: 1px solid var(--border); display: flex; gap: 16px; margin-top: 28px; padding-top: 12px; }
      """;

    private const string Filter = """
      const search = document.querySelector('#search');
      const kind = document.querySelector('#kind');
      const count = document.querySelector('#count');
      const empty = document.querySelector('#empty');
      const cards = [...document.querySelectorAll('#chart > li')];
      const ids = new Set(cards.map(card => card.dataset.id));

      function filter() {
        const query = search.value.toLowerCase().trim();
        const number = /^(?:npc-)?([0-9]{3})$/.exec(query);
        const npcId = number ? `npc-${number[1]}` : query;
        const exactId = ids.has(npcId) ? npcId : null;
        const words = npcId.split(/\s+/);
        let shown = 0;
        for (const card of cards) {
          const matchesKind = !kind.value || kind.value === card.dataset.kind;
          const matchesSearch = exactId
            ? card.dataset.id === exactId
            : words.every(word => card.dataset.search.includes(word));
          card.hidden = !matchesKind || !matchesSearch;
          if (!card.hidden) shown++;
        }
        count.textContent = `${shown} ${shown === 1 ? 'reference' : 'references'}`;
        empty.hidden = shown !== 0;
      }

      search.addEventListener('input', filter);
      kind.addEventListener('change', filter);
      window.addEventListener('pageshow', filter);
      """;
}
