using System.Diagnostics;
using Rusty.Engine;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using Xunit.Abstractions;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The Charing exterior's published spatial closure, admitted by the Engine's own Spatial and Appearance
/// services rather than the suite's fakes: the fakes never parse the artifact bytes, so only this shows
/// the importer still writes what the Engine admits. It reports how long the profile read and each
/// admission take, the costs the exterior's artifact size drives.
/// </summary>
public sealed class ExteriorSpatialAdmissionTests(ITestOutputHelper output)
{
    [Fact]
    public void The_charing_exterior_spatial_closure_is_admitted_by_the_engine_s_spatial_and_appearance_services()
    {
        string root = TestData.RepositoryRoot;
        ProductContent content = FullContent(root);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        // The first read also pays for JIT; the second is the cost the closure's bytes drive.
        _ = ReadProfile(root, content, definitions, "daggerfall.charing-exterior.json");
        Stopwatch read = Stopwatch.StartNew();
        DaggerfallSiteProfile exterior = ReadProfile(root, content, definitions, "daggerfall.charing-exterior.json");
        read.Stop();

        Dictionary<string, ReadOnlyMemory<byte>> files = new(StringComparer.Ordinal)
        {
            [exterior.SpatialArtifact.Path] = File.ReadAllBytes(Path.Combine(root, "content", exterior.SpatialArtifact.Path)),
            [exterior.StaticMesh.Path] = File.ReadAllBytes(Path.Combine(root, "content", exterior.StaticMesh.Path)),
        };
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        host.Call(engine =>
        {
            Stopwatch spatialAdmission = Stopwatch.StartNew();
            using SpatialMovementSystem spatial = new(engine.Spatial, engine.Content, exterior.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
            spatialAdmission.Stop();
            SpatialContentArtifactReadout admitted = engine.Spatial.ReadContentArtifact(new(spatial.Session));
            Assert.Equal(exterior.SpatialArtifact.Sha256, admitted.ContentSha256);
            Assert.True(admitted.CollisionTriangleCount > 0);
            Assert.True(admitted.NavigationCellCount > 0);

            Stopwatch meshAdmission = Stopwatch.StartNew();
            using Appearance mesh = engine.Graphics.CreateStaticMeshFromContent(
                new StaticMeshContentAppearanceRequest(exterior.StaticMesh.Path, exterior.WorldAppearance.Tint));
            meshAdmission.Stop();

            output.WriteLine(
                $"charing exterior: profile read {read.ElapsedMilliseconds} ms; spatial admission {spatialAdmission.ElapsedMilliseconds} ms "
                + $"({files[exterior.SpatialArtifact.Path].Length} bytes, {admitted.CollisionVertexCount} vertices, {admitted.CollisionTriangleCount} triangles, "
                + $"{admitted.NavigationCellCount} navigation cells); static mesh admission {meshAdmission.ElapsedMilliseconds} ms "
                + $"({files[exterior.StaticMesh.Path].Length} bytes)");
        });
    }
}
