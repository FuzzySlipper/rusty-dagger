using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallAmbientPresentationTests
{
    [Fact]
    public void Rain_shelter_pause_indoor_and_return_own_one_loop_and_retire_emitters()
    {
        var particles = PresentationRecorder.Create(); var audio = AudioRecorder.Create();
        using var rainSprite = new RenderResource(new(11), () => {});
        using var snowSprite = new RenderResource(new(12), () => {});
        using var owner = new DaggerfallAmbientPresentation(particles.Service, audio.Service, RandomMinimum.Create(),
            DaggerfallAmbientTuning.Classic, (_, _) => audio.Service.OpenClip(new("fixture.wav")),
            snow => new(snow ? snowSprite : rainSprite));
        var outside = Context(DaggerfallWorldProfileKind.Exterior, DaggerfallWeatherKind.Rain);
        owner.Update(outside, Vector3.Zero, 1, true);
        owner.Update(outside, Vector3.One, 1, true);
        Assert.Single(audio.Voices); Assert.Single(particles.Created); Assert.Single(particles.Updated);
        Assert.Equal(AudioBus.Ambient, audio.Voices[0].Bus);
        Assert.Equal(PresentationParticleSizeMode.World, particles.Created[0].SizeMode);
        Assert.Equal(11ul, particles.Created[0].Sprite.Value);
        Assert.Equal(Vector3.One + Vector3.UnitY * DaggerfallAmbientTuning.Classic.PrecipitationHeight,
            particles.Updated[0].Anchor.Position);
        owner.Update(outside with {Sheltered = true}, Vector3.One, 1, true);
        Assert.Equal(1, particles.Released);
        owner.Update(outside with {Sheltered = true}, Vector3.One, 0, false);
        Assert.Equal(AudioVoiceControl.Pause, audio.Controls.Last().Control);
        owner.Update(outside with {Sheltered = true}, Vector3.One, 1, true);
        Assert.Equal(AudioVoiceControl.Resume, audio.Controls.Last().Control);
        owner.Update(Context(DaggerfallWorldProfileKind.Interior, DaggerfallWeatherKind.Rain), Vector3.Zero, 1, true);
        Assert.Equal(audio.Voices.Count, audio.ReleasedVoices);
        owner.Update(outside with {Weather = DaggerfallWeatherKind.Snow}, Vector3.Zero, 1, true);
        Assert.Equal(12ul, particles.Created.Last().Sprite.Value);
        owner.Dispose(); Assert.Equal(particles.Created.Count, particles.Released);
    }

    [Fact]
    public void Castle_suppresses_dungeon_one_shots_and_lightning_delay_uses_only_admitted_time()
    {
        var particles = PresentationRecorder.Create(); var audio = AudioRecorder.Create();
        using var owner = new DaggerfallAmbientPresentation(particles.Service, audio.Service, RandomMinimum.Create(),
            DaggerfallAmbientTuning.Classic, (_, _) => audio.Service.OpenClip(new("fixture.wav")), _ => new(9ul));
        var dungeon = Context(DaggerfallWorldProfileKind.Dungeon, DaggerfallWeatherKind.Sunny);
        owner.Update(dungeon with {Castle = true}, Vector3.Zero, 40, true);
        Assert.Empty(audio.Emits);
        owner.Update(dungeon, Vector3.Zero, 5, true);
        Assert.Single(audio.Emits);
        var storm = Context(DaggerfallWorldProfileKind.Exterior, DaggerfallWeatherKind.Thunder);
        owner.Update(storm, Vector3.Zero, 5, true);
        Assert.Single(audio.Emits);
        owner.Update(storm, Vector3.Zero, 100, false);
        Assert.Single(audio.Emits);
        owner.Update(storm, Vector3.Zero, .01, true);
        Assert.Equal(DaggerfallAmbientTuning.Classic.FlashIntensity, owner.FlashIntensity);
        owner.Update(storm, Vector3.Zero, 1, true);
        Assert.Equal(2, audio.Emits.Count); Assert.Equal(0, owner.FlashIntensity);
        owner.Update(dungeon, Vector3.Zero, 0, true);
        Assert.Equal(audio.Voices.Count, audio.ReleasedVoices);
    }

    private static DaggerfallAmbientContext Context(DaggerfallWorldProfileKind kind, DaggerfallWeatherKind weather) =>
        new(new DaggerfallWorldProfileKey(new DaggerfallSiteId(1,2), kind, kind.ToString()).Validate(), weather, false, false, false, false);
}
