using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;
using Xunit.Sdk;

namespace Daggerfall.Import.Tests;

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

    private static byte[] Base() => File.ReadAllBytes(Path.Combine(RepoRoot(), "local/arena2", "CLASS00.CFG"));
    private static IReadOnlyList<SourceInventoryRow> ReadInventory() =>
        SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepoRoot(), "docs/coverage/content-source-manifest.csv")));
    private static List<(string FileName, byte[] Bytes)> ReadCareers() =>
        [.. Directory.EnumerateFiles(Path.Combine(RepoRoot(), "local/arena2"), "CLASS*.CFG").Order(StringComparer.Ordinal).Select(p => (Path.GetFileName(p), File.ReadAllBytes(p)))];
    private static string PackPath() => Path.Combine(RepoRoot(), "content/worldrpg/payloads/daggerfall.base.json");
    private static System.Text.Json.Nodes.JsonNode Pack() => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(PackPath()))!;
    private static List<string> VocabAttrs() => [.. Pack()!["vocabulary"]!["attributes"]!.AsArray().Select(v => v!.GetValue<string>())];
    private static List<string> VocabSkills() => [.. Pack()!["vocabulary"]!["skills"]!.AsArray().Select(v => v!.GetValue<string>())];
    private static List<string> EnemyIds() => [.. Pack()!["actors"]!.AsArray().Select(v => v!.AsObject()["id"]!.GetValue<string>()).Where(id => id != "player")];
    private static List<string> ItemIds() => [.. Pack()!["items"]!.AsArray().Select(v => v!.AsObject()["id"]!.GetValue<string>())];
    private static IReadOnlySet<string> InvIds(IReadOnlyList<SourceInventoryRow> inv) => inv.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
    private static DaggerfallCatalogs BuildBase() => DaggerfallCatalogBuilder.Build(ReadInventory(), VocabAttrs(), VocabSkills(), ReadCareers(), EnemyIds(), ItemIds());

    [Fact]
    public void P_Decoder_Lengths()
    {
        List<string> outcomes = [];
        foreach (int len in new[] { 0, 1, 2, 73, 74, 75, 76, 148 })
        {
            byte[] bytes = len == 74 ? Base() : new byte[len];
            try { ClassCfgDecoder.Decode(bytes, "SRC"); outcomes.Add($"len={len}: ACCEPTED"); }
            catch (Arena2FormatException ex) { outcomes.Add($"len={len}: REJECT offset={ex.Offset} msg={ex.Message}"); }
        }
        throw Probe(string.Join(" | ", outcomes));
    }

    [Fact]
    public void P_Decoder_SkillSlots_34_35_36()
    {
        int[] slots = [16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27];
        List<string> outcomes = [];
        foreach (int slot in slots)
            foreach (byte val in new byte[] { 34, 35, 36 })
            {
                byte[] b = Base(); b[slot] = val;
                try { ClassCfgDecoder.Decode(b, "SRC"); outcomes.Add($"slot={slot} val={val}: ACCEPTED"); }
                catch (Arena2FormatException ex) { outcomes.Add($"slot={slot} val={val}: REJECT offset={ex.Offset}"); }
            }
        throw Probe(string.Join(" | ", outcomes));
    }

    [Fact]
    public void P_Decoder_Names()
    {
        List<string> outcomes = [];
        void Try(string label, Action<byte[]> mutate)
        {
            byte[] b = Base(); mutate(b);
            try { var r = ClassCfgDecoder.Decode(b, "SRC"); outcomes.Add($"{label}: ACCEPTED name='{r.Name}'"); }
            catch (Arena2FormatException ex) { outcomes.Add($"{label}: REJECT offset={ex.Offset} msg={ex.Message}"); }
        }
        Try("all-NUL", b => { for (int i = 28; i < 44; i++) b[i] = 0; });
        Try("16-A-no-NUL", b => { for (int i = 28; i < 44; i++) b[i] = (byte)'A'; });
        Try("15-A-plus-NUL", b => { for (int i = 28; i < 43; i++) b[i] = (byte)'A'; b[43] = 0; });
        Try("high-0x80-first", b => { b[28] = 0x80; b[29] = 0; });
        Try("high-0xFF-first", b => { b[28] = 0xFF; b[29] = 0; });
        Try("del-0x7F", b => { b[28] = 0x7F; b[29] = 0; });
        Try("ctrl-0x1F", b => { b[28] = 0x1F; b[29] = 0; });
        Try("space-0x20", b => { b[28] = 0x20; b[29] = 0; });
        Try("tilde-0x7E", b => { b[28] = 0x7E; b[29] = 0; });
        Try("garbage-after-NUL", b => { b[28] = (byte)'A'; b[29] = 0; b[30] = 0xFF; b[31] = 0x01; });
        Try("empty-then-text", b => { b[28] = 0; b[29] = (byte)'B'; });
        throw Probe(string.Join(" | ", outcomes));
    }

    [Fact]
    public void P_Decoder_HpAndMultiplierEdges()
    {
        List<string> outcomes = [];
        void TryHp(string label, ushort hp)
        {
            byte[] b = Base(); b[52] = (byte)(hp & 0xFF); b[53] = (byte)(hp >> 8);
            try { var r = ClassCfgDecoder.Decode(b, "SRC"); outcomes.Add($"{label}: ACCEPTED hp={r.HitPointsPerLevel}"); }
            catch (Arena2FormatException ex) { outcomes.Add($"{label}: REJECT {ex.Message}"); }
        }
        void TryMult(string label, uint raw)
        {
            byte[] b = Base(); b[54] = (byte)(raw & 0xFF); b[55] = (byte)((raw >> 8) & 0xFF); b[56] = (byte)((raw >> 16) & 0xFF); b[57] = (byte)(raw >> 24);
            try { var r = ClassCfgDecoder.Decode(b, "SRC"); outcomes.Add($"{label}: ACCEPTED mult={r.AdvancementMultiplier:R}"); }
            catch (Arena2FormatException ex) { outcomes.Add($"{label}: REJECT {ex.Message}"); }
        }
        TryHp("hp-0", 0); TryHp("hp-1", 1); TryHp("hp-FFFF", 0xFFFF);
        TryMult("mult-0", 0u); TryMult("mult-1", 1u); TryMult("mult-FFFFFFFF", 0xFFFFFFFFu); TryMult("mult-00010000", 0x00010000u);
        throw Probe(string.Join(" | ", outcomes));
    }

    [Fact]
    public void P_Builder_MissingFamilies()
    {
        List<string> outcomes = [];
        foreach (string fam in new[] { "CNT-009", "CNT-010", "CNT-007", "CNT-011" })
        {
            var inv = ReadInventory().Where(r => r.Id != fam).ToList();
            try { DaggerfallCatalogBuilder.Build(inv, VocabAttrs(), VocabSkills(), ReadCareers(), EnemyIds(), ItemIds()); outcomes.Add($"{fam}: ACCEPTED"); }
            catch (Exception ex) { outcomes.Add($"{fam}: {ex.GetType().Name}: {ex.Message}"); }
        }
        throw Probe(string.Join(" | ", outcomes));
    }

    [Fact]
    public void P_Builder_DuplicateInventoryId()
    {
        var inv = ReadInventory().ToList();
        var fam = inv.First(r => r.Id == "CNT-010" && r.RowType == "family");
        inv.Add(fam with { Notes = "duplicate probe" });
        try { var c = DaggerfallCatalogBuilder.Build(inv, VocabAttrs(), VocabSkills(), ReadCareers(), EnemyIds(), ItemIds()); throw Probe($"duplicate-family-id: ACCEPTED careers={c.Careers.Count}"); }
        catch (Exception ex) { throw Probe($"duplicate-family-id: {ex.GetType().Name}: {ex.Message}"); }
    }

    [Fact]
    public void P_Builder_TwoFileRowsSamePath()
    {
        var inv = ReadInventory().ToList();
        var row = inv.First(r => r.Id == "CNT-010.file.CLASS00.CFG");
        inv.Add(row with { Id = "CNT-010.file.CLASS00.DUP" });
        try { var c = DaggerfallCatalogBuilder.Build(inv, VocabAttrs(), VocabSkills(), ReadCareers(), EnemyIds(), ItemIds()); throw Probe($"two-rows-same-path: ACCEPTED cited={c.Careers.Single(x => x.Id == "class00").Source.RecordId}"); }
        catch (Exception ex) { throw Probe($"two-rows-same-path: {ex.GetType().Name}: {ex.Message}"); }
    }

    [Fact]
    public void P_Builder_EmptyLists()
    {
        List<string> outcomes = [];
        try { var c = DaggerfallCatalogBuilder.Build(ReadInventory(), VocabAttrs(), VocabSkills(), [], EnemyIds(), ItemIds()); outcomes.Add($"empty-careers: ACCEPTED careers={c.Careers.Count} collisions=[{string.Join(",", c.CareerNameCollisions)}]"); }
        catch (Exception ex) { outcomes.Add($"empty-careers: {ex.GetType().Name}: {ex.Message}"); }
        try { var c = DaggerfallCatalogBuilder.Build(ReadInventory(), VocabAttrs(), VocabSkills(), ReadCareers(), [], ItemIds()); outcomes.Add($"empty-enemies: ACCEPTED enemies={c.Enemies.Count}"); }
        catch (Exception ex) { outcomes.Add($"empty-enemies: {ex.GetType().Name}: {ex.Message}"); }
        try { var c = DaggerfallCatalogBuilder.Build(ReadInventory(), VocabAttrs(), VocabSkills(), ReadCareers(), EnemyIds(), []); outcomes.Add($"empty-items: ACCEPTED items={c.ItemTemplates.Count}"); }
        catch (Exception ex) { outcomes.Add($"empty-items: {ex.GetType().Name}: {ex.Message}"); }
        // career file present on disk but absent from inventory
        try
        {
            var careers = ReadCareers(); careers.Add(("CLASS99.CFG", Base()));
            DaggerfallCatalogBuilder.Build(ReadInventory(), VocabAttrs(), VocabSkills(), careers, EnemyIds(), ItemIds());
            outcomes.Add("unknown-career-file: ACCEPTED");
        }
        catch (Exception ex) { outcomes.Add($"unknown-career-file: {ex.GetType().Name}: {ex.Message}"); }
        throw Probe(string.Join(" | ", outcomes));
    }

    [Fact]
    public void P_Builder_VocabLengths()
    {
        List<string> outcomes = [];
        foreach (int n in new[] { 0, 7, 8, 34, 36 })
        {
            var skills = VocabSkills().Take(n).ToList();
            if (n == 36) { skills = VocabSkills(); skills.Add("extra-skill"); }
            try { DaggerfallCatalogBuilder.Build(ReadInventory(), VocabAttrs(), skills, ReadCareers(), EnemyIds(), ItemIds()); outcomes.Add($"skills-{n}: ACCEPTED"); }
            catch (Exception ex) { outcomes.Add($"skills-{n}: {ex.GetType().Name}: {ex.Message}"); }
        }
        foreach (int n in new[] { 0, 7, 9, 10 })
        {
            var attrs = VocabAttrs().Take(n).ToList();
            if (n > 9) { }
            if (n == 10) { attrs = VocabAttrs(); attrs.Add("extra-attr"); }
            try { var c = DaggerfallCatalogBuilder.Build(ReadInventory(), attrs, VocabSkills(), ReadCareers(), EnemyIds(), ItemIds()); outcomes.Add($"attrs-{n}: ACCEPTED catalogAttrs={c.Attributes.Count}"); }
            catch (Exception ex) { outcomes.Add($"attrs-{n}: {ex.GetType().Name}: {ex.Message}"); }
        }
        throw Probe(string.Join(" | ", outcomes));
    }

    [Fact]
    public void P_Validate_Bypasses()
    {
        List<string> outcomes = [];
        var inv = InvIds(ReadInventory());
        // superset inventory set
        try { BuildBase().Validate(new HashSet<string>(inv, StringComparer.Ordinal) { "CNT-FAKE" }); outcomes.Add("superset-inv: ACCEPTED"); }
        catch (Exception ex) { outcomes.Add($"superset-inv: {ex.GetType().Name}: {ex.Message}"); }
        // empty skill lists
        try
        {
            var c = BuildBase(); var k = c.Careers[0];
            var altered = c with { Careers = [k with { PrimarySkills = [], MajorSkills = [], MinorSkills = [] }, .. c.Careers.Skip(1)] };
            altered.Validate(inv); outcomes.Add("empty-skills: ACCEPTED");
        }
        catch (Exception ex) { outcomes.Add($"empty-skills: {ex.GetType().Name}: {ex.Message}"); }
        // 12 primary skills, distinct valid keys
        try
        {
            var c = BuildBase();
            var twelve = c.Skills.Take(12).Select(s => s.Id).ToList();
            var k = c.Careers[0];
            var altered = c with { Careers = [k with { PrimarySkills = twelve, MajorSkills = [], MinorSkills = [] }, .. c.Careers.Skip(1)] };
            altered.Validate(inv); outcomes.Add("twelve-primary: ACCEPTED");
        }
        catch (Exception ex) { outcomes.Add($"twelve-primary: {ex.GetType().Name}: {ex.Message}"); }
        // 12 identical skills
        try
        {
            var c = BuildBase(); var k = c.Careers[0]; var s = c.Skills[0].Id;
            var altered = c with { Careers = [k with { PrimarySkills = [s, s, s], MajorSkills = [s], MinorSkills = [s] }, .. c.Careers.Skip(1)] };
            altered.Validate(inv); outcomes.Add("identical-skills: ACCEPTED");
        }
        catch (Exception ex) { outcomes.Add($"identical-skills: {ex.GetType().Name}: {ex.Message}"); }
        // permuted contiguous indices
        try
        {
            var c = BuildBase();
            var altered = c with { Skills = [.. c.Skills.Reverse().Select((s, i) => s with { Index = i })] };
            altered.Validate(inv); outcomes.Add("permuted-indices: ACCEPTED");
        }
        catch (Exception ex) { outcomes.Add($"permuted-indices: {ex.GetType().Name}: {ex.Message}"); }
        // duplicate donor race
        try
        {
            var c = BuildBase();
            var altered = c with { Races = [.. c.Races.Select((r, i) => i == 1 ? r with { DonorRaceId = c.Races[0].DonorRaceId } : r)] };
            altered.Validate(inv); outcomes.Add("dup-donor: ACCEPTED");
        }
        catch (Exception ex) { outcomes.Add($"dup-donor: {ex.GetType().Name}: {ex.Message}"); }
        // pending task 0
        try
        {
            var c = BuildBase();
            var altered = c with { Pending = [c.Pending[0] with { OwnerTask = 0 }, .. c.Pending.Skip(1)] };
            altered.Validate(inv); outcomes.Add("pending-0: ACCEPTED");
        }
        catch (Exception ex) { outcomes.Add($"pending-0: {ex.GetType().Name}: {ex.Message}"); }
        // fake citation id smuggled via the caller-supplied set
        try
        {
            var c = BuildBase();
            var altered = c with { Races = [c.Races[0] with { Source = new DaggerfallCatalogSource("CNT-FAKE", "local/arena2/nowhere") }, .. c.Races.Skip(1)] };
            altered.Validate(new HashSet<string>(inv, StringComparer.Ordinal) { "CNT-FAKE" }); outcomes.Add("fake-cite-in-set: ACCEPTED");
        }
        catch (Exception ex) { outcomes.Add($"fake-cite-in-set: {ex.GetType().Name}: {ex.Message}"); }
        // duplicate career ids
        try
        {
            var c = BuildBase();
            var altered = c with { Careers = [c.Careers[0] with { Id = c.Careers[1].Id }, .. c.Careers.Skip(1)] };
            altered.Validate(inv); outcomes.Add("dup-career-id: ACCEPTED");
        }
        catch (Exception ex) { outcomes.Add($"dup-career-id: {ex.GetType().Name}: {ex.Message}"); }
        // career attribute counts 7 / 9
        foreach (int n in new[] { 7, 9 })
        {
            try
            {
                var c = BuildBase(); var k = c.Careers[0];
                var attrs = n == 7 ? k.Attributes.Take(7).ToList() : [.. k.Attributes, "reflexes"];
                var altered = c with { Careers = [k with { Attributes = attrs }, .. c.Careers.Skip(1)] };
                altered.Validate(inv); outcomes.Add($"career-attrs-{n}: ACCEPTED");
            }
            catch (Exception ex) { outcomes.Add($"career-attrs-{n}: {ex.GetType().Name}: {ex.Message}"); }
        }
        // resistances 4 / 6
        foreach (int n in new[] { 4, 6 })
        {
            try
            {
                var c = BuildBase();
                var res = n == 4 ? c.Resistances.Take(4).ToList() : [.. c.Resistances, new DaggerfallIndexedKey("extra", 5, c.Resistances[0].Source)];
                var altered = c with { Resistances = res };
                altered.Validate(inv); outcomes.Add($"resistances-{n}: ACCEPTED");
            }
            catch (Exception ex) { outcomes.Add($"resistances-{n}: {ex.GetType().Name}: {ex.Message}"); }
        }
        throw Probe(string.Join(" | ", outcomes));
    }

    [Fact]
    public void P_Serializer_Idempotence()
    {
        var inv = InvIds(ReadInventory());
        byte[] a = DaggerfallCatalogSerializer.Serialize(BuildBase(), inv);
        byte[] b = DaggerfallCatalogSerializer.Serialize(BuildBase(), inv);
        throw Probe($"idempotent={a.SequenceEqual(b)} len={a.Length} sha={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(a))}");
    }
}
