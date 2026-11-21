using System.Net;
using System.Text;
using System.Text.Json;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Source.Tool.ReferenceExports;

internal sealed record NpcBrief(string Brief, string[] Costume, string[] Colors, string[] Props, string[] Pose);

internal static class NpcReferencePages
{
    private static readonly Dictionary<string, (string Source, int Record, int Frame)> PreferredDepictions = new()
    {
        ["npc-010"] = ("arena2/TEXTURE.175", 1, 0),
    };

    public static void Write(ReferenceExport export, NpcReferenceWorld world, IReadOnlyDictionary<int, ClassicFaction> factions,
        IReadOnlyList<NpcEntry> entries)
    {
        NpcEntry[] cards = [.. entries.OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .Select(entry => PreferDepiction(export, entry))];
        using Stream briefsResource = typeof(NpcReferencePages).Assembly.GetManifestResourceStream("NpcReferenceBriefs.json")!;
        var briefs = JsonSerializer.Deserialize<Dictionary<string, NpcBrief>>(briefsResource, ReferenceExport.Json)!;
        var chartBriefs = cards.Select(entry => (entry.Id, Brief: MergeBriefs(entry, briefs)))
            .Where(entry => entry.Brief is not null).ToDictionary(entry => entry.Id, entry => entry.Brief!);
        var aliases = cards.SelectMany(entry => entry.Aliases.Select(alias => (alias, entry.Id)))
            .GroupBy(item => item.alias).ToDictionary(group => group.Key, group => group.Select(item => item.Id).Distinct().ToArray());
        foreach (NpcEntry entry in cards)
        {
            NpcBrief? brief = chartBriefs.GetValueOrDefault(entry.Id);
            if (brief is null) export.Warn($"{entry.Id} has no catalog brief.");
            WriteDetail(export, world, factions, entry, brief, briefs);
        }

        export.WriteJson("briefs.json", chartBriefs);
        export.WriteJson("manifest.json", new
        {
            entries = cards.Select(entry => new { entry.Id, entry.Name, entry.Kind, entry.Aliases, image = entry.Thumbnail?.File }),
            aliases = aliases.Where(pair => pair.Value.Length == 1).ToDictionary(pair => pair.Key, pair => pair.Value[0]),
            sharedAliases = aliases.Where(pair => pair.Value.Length > 1).ToDictionary(pair => pair.Key, pair => pair.Value),
            briefs = "briefs.json",
        });
        using Stream script = typeof(NpcReferencePages).Assembly.GetManifestResourceStream("NpcReferenceChart.js")!;
        using (Stream file = File.Create(Path.Combine(export.Root, "filter.js"))) script.CopyTo(file);
        File.WriteAllText(Path.Combine(export.Root, "style.css"), Style);
        StringBuilder page = Page("Daggerfall NPC Reference", "");
        page.Append("""
          <header><h1>Daggerfall NPC Reference</h1>
          <div class="tools"><label>Find an NPC<input id="search" type="search" placeholder="ID, name, or costume detail" autocomplete="off"></label>
          """);
        WriteFilter(page, "kind", "Type", "All NPCs", cards.Select(entry => entry.Kind));
        WriteFilter(page, "costume", "Costume", "Any costume", chartBriefs.Values.SelectMany(brief => brief.Costume));
        WriteFilter(page, "color", "Color", "Any color", chartBriefs.Values.SelectMany(brief => brief.Colors));
        WriteFilter(page, "prop", "Prop", "Any prop", chartBriefs.Values.SelectMany(brief => brief.Props));
        WriteFilter(page, "pose", "Pose", "Any pose", chartBriefs.Values.SelectMany(brief => brief.Pose));
        page.Append($"""
          </div>
          <div class="results-bar"><output id="count" aria-live="polite">{Count(cards.Length, "reference")}</output>
          <div id="similar" hidden><span id="similar-label"></span> <button type="button" id="clear-similar">Clear similarity</button></div>
          </div></header><main>
          <section id="comparison" aria-labelledby="comparison-heading" hidden>
          <div class="comparison-bar"><h2 id="comparison-heading">Compare costumes</h2>
          <output id="comparison-count" aria-live="polite"></output><button type="button" id="clear-comparison">Clear comparison</button></div>
          <ul id="comparison-list"></ul>
          <div id="merge-tools" class="comparison-bar" hidden>
          <label>Keep NPC <select id="merge-keeper"></select></label>
          <button type="button" id="mark-merge">Mark merge</button>
          <output id="merge-status" aria-live="polite"></output></div></section>
          <ul id="chart" class="chart">
          """);
        foreach (NpcEntry entry in cards)
        {
            NpcBrief? brief = chartBriefs.GetValueOrDefault(entry.Id);
            string costume = string.Join('|', brief?.Costume ?? []);
            string color = string.Join('|', brief?.Colors ?? []);
            string prop = string.Join('|', brief?.Props ?? []);
            string pose = string.Join('|', brief?.Pose ?? []);
            string haystack = string.Join(' ', new[] { entry.Id, entry.Name, brief?.Brief ?? "", costume, color, prop }
                .Concat(entry.Aliases).Concat((entry.Merged ?? []).Select(member => member.Name)));
            page.Append($"""
              <li data-id="{H(entry.Id)}" data-name="{H(entry.Name)}" data-kind="{H(entry.Kind)}"
                  data-search="{H(haystack.ToLowerInvariant())}" data-brief="{H(brief?.Brief ?? "")}"
                  data-costume="{H(costume)}" data-color="{H(color)}" data-prop="{H(prop)}" data-pose="{H(pose)}">
              <a class="card" href="{H(entry.Id)}/index.html">
              {Picture(entry.Thumbnail, "", "")}
              <span class="card-name">{H(entry.Name)}</span><code>{H(entry.Id)}</code>
              </a><button class="compare-toggle" type="button" aria-label="Compare {H(entry.Id)}" aria-pressed="false">Compare</button></li>
              """);
        }
        page.Append("</ul><p id=\"empty\" hidden>No matching NPCs.</p><footer><a href=\"briefs.json\">Briefs JSON</a></footer></main><script src=\"filter.js\"></script></body></html>");
        File.WriteAllText(Path.Combine(export.Root, "index.html"), page.ToString());
        Console.WriteLine($"Daggerfall NPC reference: {cards.Length} cards, {chartBriefs.Count} briefs → {export.Root}");
    }

    private static NpcEntry PreferDepiction(ReferenceExport export, NpcEntry entry)
    {
        if (!PreferredDepictions.TryGetValue(entry.Id, out var preferred)) return entry;
        ReferenceImage? image = entry.Art.SelectMany(group => group.Frames)
            .FirstOrDefault(frame => (frame.Source, frame.Record, frame.Frame) == preferred);
        if (image is null)
        {
            export.Warn($"{entry.Id} is missing its preferred depiction from {preferred.Source}, record {preferred.Record}, frame {preferred.Frame}.");
            return entry;
        }
        return entry with
        {
            PreferredImage = image,
            Art = [.. entry.Art.OrderByDescending(group => group.Frames.Contains(image))],
        };
    }

    private static void WriteFilter(StringBuilder page, string id, string label, string empty, IEnumerable<string> values)
    {
        page.Append($"<label>{H(label)}<select id=\"{H(id)}\"><option value=\"\">{H(empty)}</option>");
        foreach (string value in values.Distinct().Order(StringComparer.OrdinalIgnoreCase))
            page.Append($"<option value=\"{H(value)}\">{H(value)}</option>");
        page.Append("</select></label>");
    }

    private static NpcBrief? MergeBriefs(NpcEntry entry, IReadOnlyDictionary<string, NpcBrief> briefs)
    {
        NpcBrief[] members = [.. (entry.Merged ?? []).Prepend(entry).Select(member => briefs.GetValueOrDefault(member.Id)).OfType<NpcBrief>()];
        return members.FirstOrDefault() is { } keeper ? keeper with
        {
            Costume = [.. members.SelectMany(brief => brief.Costume).Distinct()],
            Colors = [.. members.SelectMany(brief => brief.Colors).Distinct()],
            Props = [.. members.SelectMany(brief => brief.Props).Distinct()],
            Pose = [.. members.SelectMany(brief => brief.Pose).Distinct()],
        } : null;
    }

    private static void WriteDetail(ReferenceExport export, NpcReferenceWorld world, IReadOnlyDictionary<int, ClassicFaction> factions,
        NpcEntry entry, NpcBrief? brief, IReadOnlyDictionary<string, NpcBrief> briefs)
    {
        string directory = Path.Combine(export.Root, entry.Id);
        Directory.CreateDirectory(directory);
        StringBuilder page = Page($"{entry.Name} · {entry.Id}", "../");
        page.Append($"""
          <header><nav><a href="../index.html">← All NPCs</a></nav>
          <h1>{H(entry.Name)}</h1><p class="identity"><code>{H(entry.Id)}</code> · {H(entry.Kind)}</p>
          </header><main>
          """);
        if (brief is not null)
        {
            page.Append($"<section class=\"brief\"><h2>Costume brief</h2><p>{H(brief.Brief)}</p><dl>");
            WriteBriefTags(page, "Costume", brief.Costume);
            WriteBriefTags(page, "Colors", brief.Colors);
            WriteBriefTags(page, "Props", brief.Props);
            WriteBriefTags(page, "Pose", brief.Pose);
            page.Append("</dl></section>");
        }
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

        if (entry.Merged is { Count: > 0 } merged)
        {
            page.Append("<section><h2>Merged references</h2>");
            foreach (NpcEntry member in merged)
            {
                page.Append($"<details><summary>{H(member.Id)} · {H(member.Name)}</summary>");
                if (briefs.GetValueOrDefault(member.Id) is { } original) page.Append($"<p>{H(original.Brief)}</p>");
                page.Append("<dl>");
                foreach ((string label, string value) in member.Facts)
                    WriteBriefTags(page, label, [value]);
                page.Append("</dl></details>");
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
            entry.Id, entry.Name, entry.Kind, entry.Facts, entry.Aliases, entry.Art, entry.Placements, brief,
            image = entry.Thumbnail?.File,
            merged = (entry.Merged ?? []).Select(member => new { member.Id, member.Name, member.Kind, member.Facts, brief = briefs.GetValueOrDefault(member.Id) }),
            artRoot = "../art/",
        });
    }

    private static void WriteBriefTags(StringBuilder page, string label, string[] values)
    {
        if (values.Length > 0) page.Append($"<div><dt>{H(label)}</dt><dd>{H(string.Join(", ", values))}</dd></div>");
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
      a:focus-visible, button:focus-visible, input:focus-visible, select:focus-visible, summary:focus-visible { outline: 2px solid var(--gold); outline-offset: 3px; }
      .tools { display: flex; flex-wrap: wrap; align-items: end; gap: 12px; }
      .tools label { display: grid; gap: 4px; color: var(--dim); font-size: .85rem; }
      input, select { font: inherit; font-size: 1rem; min-height: 42px; width: 100%; padding: 6px 10px; border: 1px solid var(--border); border-radius: 5px; background: var(--panel); color: var(--ink); }
      input { width: min(310px, 82vw); }
      .results-bar, .comparison-bar:not([hidden]) { display: flex; align-items: center; flex-wrap: wrap; gap: 8px 16px; }
      .results-bar { margin-top: 10px; }
      button { font: inherit; cursor: pointer; color: var(--ink); background: var(--panel); border: 1px solid var(--border); border-radius: 4px; padding: 4px 9px; }
      button:hover, button[aria-pressed="true"] { color: var(--gold); border-color: var(--gold); }
      button:disabled { cursor: default; opacity: .55; }
      #comparison { position: sticky; top: 8px; z-index: 1; margin-bottom: 18px; padding: 10px; border: 1px solid var(--border); border-radius: 6px; background: #24211e; box-shadow: 0 5px 18px #150f0b; }
      .comparison-bar { margin-bottom: 8px; }
      .comparison-bar h2 { font-size: 1.2rem; margin: 0; }
      #clear-comparison { margin-left: auto; }
      #merge-tools:not([hidden]) { margin-top: 8px; margin-bottom: 0; }
      #merge-tools select { width: auto; min-height: 32px; margin-left: 4px; }
      #comparison-list { display: flex; overflow-x: auto; list-style: none; gap: 10px; margin: 0; padding: 0 0 2px; }
      #comparison-list li { flex: 1 0 min(200px, calc(50% - 6px)); max-width: 360px; min-width: 0; padding: 8px; border: 1px solid var(--border); border-radius: 4px; }
      #comparison-list .picture { height: clamp(88px, 16vh, 155px); margin-bottom: 5px; }
      #comparison-list p { margin: 6px 0; font-size: .85rem; }
      .comparison-actions { display: flex; gap: 6px; flex-wrap: wrap; }
      .brief { max-width: 880px; margin-bottom: 22px; }
      .brief p { color: var(--ink); margin: 8px 0 12px; }
      .chart, .frames, .links { list-style: none; padding: 0; margin: 0; display: grid; gap: 10px; }
      .chart { grid-template-columns: repeat(auto-fill, minmax(126px, 1fr)); }
      .chart > li:not([hidden]) { display: flex; flex-direction: column; }
      .card { display: flex; flex: 1; flex-direction: column; gap: 4px; text-decoration: none; padding: 6px; background: var(--panel); border: 1px solid var(--border); border-radius: 6px 6px 0 0; }
      .compare-toggle { border-top: 0; border-radius: 0 0 6px 6px; font-size: .78rem; }
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

}
