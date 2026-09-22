using System.Text.Json;
using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// Block placements: every RDB object and RMB half record publishes its position and references,
/// model ids resolve to geometry, and malformed halves are explicit.
/// </summary>
public sealed class BlockPlacementTests
{
    [Fact]
    public void Refuses_halves_whose_records_run_past_their_bytes()
    {
        byte[] tiny = new byte[16];
        Assert.Throws<Arena2FormatException>(() => RmbPlacementReader.Read(tiny, 0, new RmbBlockSummary("X", 0, 0, 1, 0, [], 0), "source"));
    }

    [Fact]
    public void Placements_repeat_counts_and_resolve_models()
    {
        string root = RepositoryRoot();
        JsonDocument payload = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.blocks.json")));
        JsonDocument pack = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));

        HashSet<int> models = [.. pack.RootElement.GetProperty("geometry").GetProperty("records").EnumerateArray().Select(record => record.GetProperty("recordId").GetInt32())];
        int rdbModels = 0, rdbFlats = 0, rdbLights = 0, rdbDoors = 0;
        int rmbModels = 0, rmbFlats = 0, rmbSections = 0, rmbPeople = 0, rmbDoors = 0;
        foreach (JsonElement record in payload.RootElement.GetProperty("records").EnumerateArray())
        {
            if (record.TryGetProperty("objects", out JsonElement objects) && objects.ValueKind == JsonValueKind.Object)
            {
                Assert.Equal(objects.GetProperty("models").GetInt32(), objects.GetProperty("modelPlacements").EnumerateArray().Count());
                Assert.Equal(objects.GetProperty("flats").GetInt32(), objects.GetProperty("flatPlacements").EnumerateArray().Count());
                Assert.Equal(objects.GetProperty("lights").GetInt32(), objects.GetProperty("lightPlacements").EnumerateArray().Count());
                foreach (JsonElement model in objects.GetProperty("modelPlacements").EnumerateArray())
                {
                    Assert.Contains(int.Parse(model.GetProperty("modelId").GetString()!, System.Globalization.CultureInfo.InvariantCulture), models);
                    rdbModels++;
                }

                rdbFlats += objects.GetProperty("flatPlacements").EnumerateArray().Count();
                rdbLights += objects.GetProperty("lightPlacements").EnumerateArray().Count();
                rdbDoors += objects.GetProperty("doorPlacements").EnumerateArray().Count();
            }

            if (record.TryGetProperty("rmbPlacements", out JsonElement placements) && placements.ValueKind == JsonValueKind.Object)
            {
                JsonElement header = record.GetProperty("rmb");
                Assert.Equal(header.GetProperty("buildings").EnumerateArray().Count(), placements.GetProperty("buildings").EnumerateArray().Count());
                foreach (var (building, placed) in header.GetProperty("buildings").EnumerateArray().Zip(placements.GetProperty("buildings").EnumerateArray()))
                {
                    CheckHalf(building.GetProperty("exterior"), placed.GetProperty("exterior"));
                    CheckHalf(building.GetProperty("interior"), placed.GetProperty("interior"));
                }
            }
        }

        // Corpus scale: every RDB object and a six-figure RMB placement census.
        Assert.True(rdbModels > 20000);
        Assert.True(rdbFlats > 10000);
        Assert.True(rdbLights > 4000);
        Assert.True(rdbDoors > 2000);

        int Count(string path)
        {
            int total = 0;
            foreach (JsonElement record in payload.RootElement.GetProperty("records").EnumerateArray())
            {
                if (record.TryGetProperty("rmbPlacements", out JsonElement p) && p.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonElement building in p.GetProperty("buildings").EnumerateArray())
                    {
                        foreach (string half in new[] { "exterior", "interior" })
                        {
                            total += building.GetProperty(half).GetProperty(path).EnumerateArray().Count();
                        }
                    }

                    if (path == "models") total += p.GetProperty("miscModels").EnumerateArray().Count();
                    if (path == "flats") total += p.GetProperty("miscFlats").EnumerateArray().Count();
                }
            }

            return total;
        }

        rmbModels = Count("models");
        rmbFlats = Count("flats");
        rmbSections = Count("sections");
        rmbPeople = Count("people");
        rmbDoors = Count("doors");
        Assert.True(rmbModels > 200000);
        Assert.True(rmbFlats > 100000);
        Assert.True(rmbSections > 100000);
        Assert.True(rmbPeople > 10000);
        Assert.True(rmbDoors > 20000);

        void CheckHalf(JsonElement counts, JsonElement placed)
        {
            Assert.Equal(counts.GetProperty("objects").GetInt32(), placed.GetProperty("models").EnumerateArray().Count());
            Assert.Equal(counts.GetProperty("flats").GetInt32(), placed.GetProperty("flats").EnumerateArray().Count());
            Assert.Equal(counts.GetProperty("sections").GetInt32(), placed.GetProperty("sections").EnumerateArray().Count());
            Assert.Equal(counts.GetProperty("people").GetInt32(), placed.GetProperty("people").EnumerateArray().Count());
            Assert.Equal(counts.GetProperty("doors").GetInt32(), placed.GetProperty("doors").EnumerateArray().Count());
            foreach (JsonElement model in placed.GetProperty("models").EnumerateArray())
            {
                Assert.Contains(int.Parse(model.GetProperty("modelId").GetString()!, System.Globalization.CultureInfo.InvariantCulture), models);
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
