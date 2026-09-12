using System.Text;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using Xunit.Sdk;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

// THROWAWAY boundary probes, part 2. Deleted after the run.
public sealed class TempEbProbe2Tests
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

    [Fact]
    public void P2_FlagBytes()
    {
        List<string> o = [];
        o.Add("resFlags-missing: " + ReadMutate(p => (p["catalogs"]!["careers"]!.AsArray()[12]!.AsObject()).Remove("resistanceFlags")));
        o.Add("resFlags-string: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[12]!["resistanceFlags"] = "34"));
        o.Add("resFlags-256: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[12]!["resistanceFlags"] = 256));
        o.Add("resFlags-neg: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[12]!["resistanceFlags"] = -1));
        o.Add("resFlags-float: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[12]!["resistanceFlags"] = 34.5));
        o.Add("lowTol-8: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["lowToleranceFlags"] = 8));
        o.Add("critWeak-255: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["criticalWeaknessFlags"] = 255));
        o.Add("critWeak-256: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["criticalWeaknessFlags"] = 256));
        o.Add("consistent-lie: " + ReadMutate(p => { var c = p["catalogs"]!["careers"]!.AsArray()[12]!; c["resistanceFlags"] = 2; c["resistanceElements"] = new JsonArray("magic"); }));
        o.Add("flagBytes-removed: " + ReadMutate(p => (p["catalogs"]!["careers"]!.AsArray()[0]!.AsObject()).Remove("flagBytes")));
        o.Add("flagBytes-garbage: " + ReadMutate(p => p["catalogs"]!["careers"]!.AsArray()[0]!["flagBytes"] = "nonsense"));
        throw Probe(string.Join("\n", o));
    }

    [Fact]
    public void P2_FingerprintFlagBlindness()
    {
        var baseline = DaggerfallBaseContent.Fingerprint(DaggerfallBaseContent.Read(File.ReadAllBytes(PackPath())));
        JsonObject p1 = JsonNode.Parse(File.ReadAllText(PackPath()))!.AsObject();
        p1["catalogs"]!["careers"]!.AsArray()[0]!["lowToleranceFlags"] = 8;
        string fpLow = DaggerfallBaseContent.Fingerprint(DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(p1.ToJsonString())));
        JsonObject p2 = JsonNode.Parse(File.ReadAllText(PackPath()))!.AsObject();
        p2["catalogs"]!["careers"]!.AsArray()[0]!["criticalWeaknessFlags"] = 255;
        string fpCrit = DaggerfallBaseContent.Fingerprint(DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(p2.ToJsonString())));
        JsonObject p3 = JsonNode.Parse(File.ReadAllText(PackPath()))!.AsObject();
        p3["catalogs"]!["careers"]!.AsArray()[12]!["resistanceFlags"] = 2;
        p3["catalogs"]!["careers"]!.AsArray()[12]!["resistanceElements"] = new JsonArray("magic");
        string fpConsistentLie;
        try { fpConsistentLie = DaggerfallBaseContent.Fingerprint(DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(p3.ToJsonString()))); }
        catch (Exception ex) { fpConsistentLie = "READ-FAILED " + ex.GetType().Name; }
        throw Probe($"lowTol-change-visible={!string.Equals(baseline, fpLow, StringComparison.Ordinal)} critWeak-change-visible={!string.Equals(baseline, fpCrit, StringComparison.Ordinal)} consistent-lie-fp={!string.Equals(baseline, fpConsistentLie, StringComparison.Ordinal)} consistent-lie-read={(fpConsistentLie.StartsWith("READ-FAILED") ? fpConsistentLie : "accepted")}");
    }
}
