using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal static partial class DaggerfallSiteContent
{
    private static IReadOnlyList<DaggerfallCityGateDefinition> ReadCityGates(ProductContent content,
        ReadOnlyMemory<byte> normalized, DaggerfallWorldMedia product, DaggerfallClosureArtifacts artifacts,
        ref IReadOnlyList<NormalizedMaterial> materials, DaggerfallContentDiagnostics diagnostics)
    {
        using JsonDocument document = JsonDocument.Parse(normalized);
        if (!document.RootElement.GetProperty("world").TryGetProperty("gates", out var gates) || gates.GetArrayLength() == 0) return [];
        var meshes = DaggerfallLocationAssembly.ReadMeshIndex(content, product);
        List<NormalizedMaterial> bindings = [.. materials];
        List<DaggerfallCityGateDefinition> result = [];
        foreach (JsonElement gate in gates.EnumerateArray())
        {
            string id = gate.GetProperty("id").GetString()!;
            DaggerfallCityGateState State(string property)
            {
                var state = DaggerfallWorldBlocks.ReadGateState(gate.GetProperty(property), id, diagnostics);
                var mesh = meshes[state.MeshArtifactId];
                List<DaggerfallMeshMaterialBinding> slots = [];
                foreach (var slot in mesh.MaterialsBySlot.OrderBy(pair => pair.Key))
                {
                    var material = bindings.FirstOrDefault(value => value.MaterialResourceId == slot.Value);
                    if (material is null)
                    {
                        if (!product.TryGetResource(slot.Value, out var resource))
                        { diagnostics.Add($"City gate '{id}' material '{slot.Value}' is not published."); continue; }
                        string path = artifacts.Resolve(DaggerfallBaseContent.Text(resource, "relativePath", diagnostics));
                        material = new NormalizedMaterial((uint)bindings.Count, path, RequireArtifact(artifacts, path, diagnostics), slot.Value);
                        bindings.Add(material);
                    }
                    slots.Add(new(slot.Key, material.Slot));
                }
                return new(new(mesh.Path, mesh.Sha256, slots), state.LocalBounds.Minimum, state.LocalBounds.Maximum,
                    state.CollisionVertices, state.CollisionTriangles);
            }
            result.Add(new DaggerfallCityGateDefinition(id, DaggerfallSiteGeometry.Pose(
                ObjectVector3(gate.GetProperty("position"), id, diagnostics),
                ObjectVector3(gate.GetProperty("rotationDegrees"), id, diagnostics)), State("open"), State("closed")).Validate());
        }
        materials = bindings;
        return result;
    }
}
