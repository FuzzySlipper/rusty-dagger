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
    public void Rain_shelter_pause_indoor_and_return_own_one_loop()
    {
        var audio = AudioRecorder.Create();
        using var owner = new DaggerfallAmbientPresentation(audio.Service, RandomMinimum.Create(),
            DaggerfallAmbientTuning.Classic, (_, _) => audio.Service.OpenClip(new("fixture.wav")));
        var outside = Context(DaggerfallWorldProfileKind.Exterior, DaggerfallWeatherKind.Rain);
        owner.Update(outside, Vector3.Zero, 1, true);
        owner.Update(outside, Vector3.One, 1, true);
        Assert.Single(audio.Voices);
        Assert.Equal(AudioBus.Ambient, audio.Voices[0].Bus);
        owner.Update(outside with {Sheltered = true}, Vector3.One, 0, false);
        Assert.Equal(AudioVoiceControl.Pause, audio.Controls.Last().Control);
        owner.Update(outside with {Sheltered = true}, Vector3.One, 1, true);
        Assert.Equal(AudioVoiceControl.Resume, audio.Controls.Last().Control);
        owner.Update(Context(DaggerfallWorldProfileKind.Interior, DaggerfallWeatherKind.Rain), Vector3.Zero, 1, true);
        Assert.Equal(audio.Voices.Count, audio.ReleasedVoices);
    }

    [Fact]
    public void Snow_gusts_with_the_classics_wind()
    {
        var audio = AudioRecorder.Create(); var opened = new List<string>();
        using var owner = new DaggerfallAmbientPresentation(audio.Service, RandomMinimum.Create(), DaggerfallAmbientTuning.Classic,
            (_, cue) => {opened.Add(cue); return audio.Service.OpenClip(new("fixture.wav"));});
        var snow = Context(DaggerfallWorldProfileKind.Exterior, DaggerfallWeatherKind.Snow);
        owner.Update(snow, Vector3.Zero, 40, true);
        Assert.Single(audio.Emits);
        Assert.Contains(opened, cue => cue.StartsWith("dungeon.ambient.", StringComparison.Ordinal));
    }

    [Fact]
    public void Castle_suppresses_dungeon_one_shots_and_lightning_delay_uses_only_admitted_time()
    {
        var audio = AudioRecorder.Create();
        using var owner = new DaggerfallAmbientPresentation(audio.Service, RandomMinimum.Create(),
            DaggerfallAmbientTuning.Classic, (_, _) => audio.Service.OpenClip(new("fixture.wav")));
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
        owner.Dispose();
        Assert.Equal(audio.EmittedSignals.OrderBy(signal => signal.Value), audio.RetiredSignals.OrderBy(signal => signal.Value));
    }

    [Fact]
    public void Realized_one_shot_is_removed_from_the_disposal_retirement_set()
    {
        var audio = AudioRecorder.Create();
        using var owner = new DaggerfallAmbientPresentation(audio.Service, RandomMinimum.Create(),
            DaggerfallAmbientTuning.Classic, (_, _) => audio.Service.OpenClip(new("fixture.wav")));
        var dungeon = Context(DaggerfallWorldProfileKind.Dungeon, DaggerfallWeatherKind.Sunny);
        owner.Update(dungeon with {Castle = true}, Vector3.Zero, 40, true);
        owner.Update(dungeon, Vector3.Zero, 5, true);
        AudioSignalHandle signal = Assert.Single(audio.EmittedSignals);
        audio.RealizationFacts.Add(new(AudioRealizationFactKind.NaturalCompletionOneShot, 1, 0, signal.Value, 0, AudioDiagnosticCode.None));
        owner.Update(dungeon, Vector3.Zero, 0, true);
        owner.Dispose();
        Assert.Empty(audio.RetiredSignals);
    }

    private static DaggerfallAmbientContext Context(DaggerfallWorldProfileKind kind, DaggerfallWeatherKind weather) =>
        new(new DaggerfallWorldProfileKey(new DaggerfallSiteId(1,2), kind, kind.ToString()).Validate(), weather, false, false, false, false);
}
