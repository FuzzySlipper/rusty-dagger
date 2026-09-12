using System.Text;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using Xunit.Sdk;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

// THROWAWAY boundary probes for the error-and-boundary review lane. Deleted after the run.
public sealed class TempEbProbeTests
{
    private static XunitException Probe(string outcome) => new("PROBE " + outcome);

    private static string RepoRoot()
    {
        for (DirectoryInfo? c = new(AppContext.BaseDirectory); c is not null; c = c.Parent)
            if (File.Exists(Path.Combine(c.FullName, "AGENTS.md"))) return c.FullName;
        throw new InvalidOperationException("no root");
    }

    private static string PackPath() => Path.Combine(RepoRoot(), "content/worldrpg/payloads/daggerfall.base.json");

    private static string ReadMutate(Action<JsonObject> change)
    {
        JsonObject pack = JsonNode.Parse(File.ReadAllText(PackPath()))!.AsObject();
        change(pack);
        try { DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(pack.ToJsonString())); return "ACCEPTED"; }
        catch (DaggerfallContentException ex) { return $"REFUSED diag={ex.Diagnostics.Count} msg={ex.Message}"; }
        catch (Exception ex) { return $"ESCAPED {ex.GetType().FullName}: {ex.Message}"; }
    }

    private static string ReadRawText(Func<string, string> change)
    {
        string text = change(File.ReadAllText(PackPath()));
        try { DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(text)); return "ACCEPTED"; }
        catch (DaggerfallContentException ex) { return $"REFUSED diag={ex.Diagnostics.Count} msg={ex.Message}"; }
        catch (Exception ex) { return $"ESCAPED {ex.GetType().FullName}: {ex.Message}"; }
    }

    [Fact]
    public void P_Runtime_ShapeMutations()
    {
        List<string> o = [];
        o.Add("missing-catalogs: " + ReadMutate(p => p.Remove("catalogs")));
        o.Add("catalogs-array: " + ReadMutate(p => p["catalogs"] = new JsonArray()));
        o.Add("catalogs-null: " + ReadMutate(p => p["catalogs"] = JsonValue.Create((string?)null)));
        o.Add("catalogs-string: " + ReadMutate(p => p["catalogs"] = "nope"));
        o.Add("schema-0: " + ReadMutate(p => p["catalogs"]!["schemaVersion"] = 0));
        o.Add("schema-2: " + ReadMutate(p => p["catalogs"]!["schemaVersion"] = 2));
        o.Add("schema-str: " + ReadMutate(p => p["catalogs"]!["schemaVersion"] = "1"));
        o.Add("schema-missing: " + ReadMutate(p => (p["catalogs"]!.AsObject()).Remove("schemaVersion")));
        o.Add("attrs-object: " + ReadMutate(p => p["catalogs"]!["attributes"] = new JsonObject()));
        o.Add("attrs-missing: " + ReadMutate(p => (p["catalogs"]!.AsObject()).Remove("attributes")));
        o.Add("hp-string: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["hitPointsPerLevel"] = "18"));
        o.Add("hp-negative: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["hitPointsPerLevel"] = -5));
        o.Add("mult-string: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["advancementMultiplier"] = "fast"));
        o.Add("mult-zero: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["advancementMultiplier"] = 0));
        o.Add("mult-negative: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["advancementMultiplier"] = -1.5));
        o.Add("neg-index: " + ReadMutate(p => p["catalogs"]!["attributes"]!.AsArray()[0]!["index"] = -1));
        o.Add("dup-index: " + ReadMutate(p => p["catalogs"]!["attributes"]!.AsArray()[1]!["index"] = 0));
        o.Add("gap-index: " + ReadMutate(p => p["catalogs"]!["attributes"]!.AsArray()[7]!["index"] = 99));
        o.Add("index-string: " + ReadMutate(p => p["catalogs"]!["attributes"]!.AsArray()[0]!["index"] = "zero"));
        o.Add("extra-prop-career: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["mysteryField"] = 42));
        o.Add("cite-norecord: " + ReadMutate(p => (p["catalogs"]!["races"]!.AsArray()[0]!["source"]!.AsObject()).Remove("recordId")));
        o.Add("cite-nopath: " + ReadMutate(p => (p["catalogs"]!["races"]!.AsArray()[0]!["source"]!.AsObject()).Remove("path")));
        o.Add("cite-empty-path: " + ReadMutate(p => p["catalogs"]!["races"]!.AsArray()[0]!["source"]!["path"] = ""));
        o.Add("cite-CNT999: " + ReadMutate(p => p["catalogs"]!["races"]!.AsArray()[0]!["source"]!["recordId"] = "CNT-999"));
        throw Probe(string.Join("\n", o));
    }

    [Fact]
    public void P_Runtime_SemanticGaps()
    {
        List<string> o = [];
        o.Add("dup-career-id: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[1]!["id"] = "class00"));
        o.Add("dup-race-id: " + ReadMutate(p => p["catalogs"]!["races"]!.AsArray()[1]!["id"] = "breton"));
        o.Add("dup-race-donor: " + ReadMutate(p => p["catalogs"]!["races"]!.AsArray()[1]!["donorRaceId"] = 1));
        o.Add("donor-0: " + ReadMutate(p => p["catalogs"]!["races"]!.AsArray()[0]!["donorRaceId"] = 0));
        o.Add("donor-neg: " + ReadMutate(p => p["catalogs"]!["races"]!.AsArray()[0]!["donorRaceId"] = -3));
        o.Add("pending-0: " + ReadMutate(p => p["catalogs"]!["pending"]!.AsArray()[0]!["ownerTask"] = 0));
        o.Add("pending-neg: " + ReadMutate(p => p["catalogs"]!["pending"]!.AsArray()[0]!["ownerTask"] = -1));
        o.Add("pending-dup: " + ReadMutate(p => p["catalogs"]!["pending"]!.AsArray()[1]!["id"] = "factions"));
        o.Add("pending-empty-reason: " + ReadMutate(p => p["catalogs"]!["pending"]!.AsArray()[0]!["reason"] = ""));
        o.Add("career-empty-skills: " + ReadMutate(p => { var c = p["catalogs"]!["careers"]!.AsArray()[0]!; c["primarySkills"] = new JsonArray(); c["majorSkills"] = new JsonArray(); c["minorSkills"] = new JsonArray(); }));
        o.Add("career-12-primary: " + ReadMutate(p => {
            var skills = p["catalogs"]!["skills"]!.AsArray().Take(12).Select(s => JsonValue.Create(s!["id"]!.GetValue<string>())!);
            var arr = new JsonArray(); foreach (var s in skills) arr.Add(s);
            p["catalogs"]!["careers"]!.AsArray()[0]!["primarySkills"] = arr;
        }));
        o.Add("career-attrs-empty: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["attributes"] = new JsonArray()));
        o.Add("career-name-empty: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["name"] = ""));
        o.Add("collisions-empty: " + ReadMutate(p => p["catalogs"]!["careerNameCollisions"] = new JsonArray()));
        o.Add("skillref-tamper: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["skillReferences"]!.AsArray()[0] = "tampered"));
        o.Add("resistances-drop-magic: " + ReadMutate(p => {
            var arr = p["catalogs"]!["resistances"]!.AsArray();
            for (int i = arr.Count - 1; i >= 0; i--) if (arr[i]!["id"]!.GetValue<string>() == "magic") arr.RemoveAt(i);
        }));
        throw Probe(string.Join("\n", o));
    }

    [Fact]
    public void P_Runtime_DuplicateJsonProperty()
    {
        // JsonObject cannot hold duplicates, so mutate the raw text: repeat "id" in career[0].
        string outcome = ReadRawText(text =>
        {
            int at = text.IndexOf("\"id\": \"class00\"", StringComparison.Ordinal);
            return text.Insert(at, "\"id\": \"duplicate-id\", ");
        });
        throw Probe("dup-json-prop-career-id: " + outcome);
    }

    [Fact]
    public void P_Runtime_PrivateersHoldAsBase()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "content/worldrpg/payloads/daggerfall.privateers-hold.json"));
        string outcome;
        try { DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(text)); outcome = "ACCEPTED"; }
        catch (DaggerfallContentException ex) { outcome = $"REFUSED diag={ex.Diagnostics.Count} msg={ex.Message}"; }
        catch (Exception ex) { outcome = $"ESCAPED {ex.GetType().FullName}: {ex.Message}"; }
        throw Probe("privateers-as-base: " + outcome);
    }

    [Fact]
    public void P_Runtime_DiagnosticCap()
    {
        // Every career loses its name (19 diagnostics) plus more: blank all primary skills.
        JsonObject pack = JsonNode.Parse(File.ReadAllText(PackPath()))!.AsObject();
        foreach (var c in pack["catalogs"]!["careers"]!.AsArray()) c!["name"] = "";
        string outcome;
        try { DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(pack.ToJsonString())); outcome = "ACCEPTED"; }
        catch (DaggerfallContentException ex)
        {
            bool truncated = ex.Message.Contains("truncated", StringComparison.Ordinal);
            outcome = $"REFUSED diag={ex.Diagnostics.Count} truncated={truncated} first={ex.Diagnostics[0]} last={ex.Diagnostics[^1]}";
        }
        catch (Exception ex) { outcome = $"ESCAPED {ex.GetType().FullName}: {ex.Message}"; }
        throw Probe("cap-19-bad-names: " + outcome);
    }

    [Fact]
    public void P_Fingerprint_Sensitivity()
    {
        var defs = DaggerfallBaseContent.Read(File.ReadAllBytes(PackPath()));
        string baseline = DaggerfallBaseContent.Fingerprint(defs);
        string fixture = File.ReadAllText(Path.Combine(RepoRoot(), "tests/WorldRpg.Rulesets.Daggerfall.Tests/Fixtures/daggerfall.base.semantic.sha256")).Trim();
        JsonObject pack = JsonNode.Parse(File.ReadAllText(PackPath()))!.AsObject();
        pack["catalogs"]!["careers"]!.AsArray()[0]!["hitPointsPerLevel"] = 7;
        var mutated = DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(pack.ToJsonString()));
        string changed = DaggerfallBaseContent.Fingerprint(mutated);
        // skillReferences is redundant published data the reader ignores: tamper it only.
        JsonObject pack2 = JsonNode.Parse(File.ReadAllText(PackPath()))!.AsObject();
        pack2["catalogs"]!["careers"]!.AsArray()[0]!["skillReferences"]!.AsArray()[0] = "tampered";
        string fp2;
        try { fp2 = DaggerfallBaseContent.Fingerprint(DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(pack2.ToJsonString()))); }
        catch (Exception ex) { fp2 = $"READ-FAILED {ex.GetType().Name}"; }
        throw Probe($"baseline==fixture={string.Equals(baseline, fixture, StringComparison.Ordinal)} hp6v7-differ={!string.Equals(baseline, changed, StringComparison.Ordinal)} skillref-only-differ={!string.Equals(baseline, fp2, StringComparison.Ordinal)}");
    }
}
