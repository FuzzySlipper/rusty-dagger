using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDeathPresentationTests
{
    [Fact]
    public void Death_enters_once_and_publishes_supported_effects_and_choices()
    {
        DaggerfallDeathPresentation presentation = new();

        Assert.False(presentation.View.Active);
        Assert.True(presentation.Enter());
        DaggerfallDeathView view = presentation.View;

        Assert.True(view.Active);
        Assert.Equal("screen.death", view.Screen);
        Assert.True(view.ControlsSuppressed);
        Assert.Equal(DaggerfallDeathCameraEffect.Fall, view.CameraEffect);
        Assert.Equal(DaggerfallDeathFadeEffect.ToBlack, view.FadeEffect);
        Assert.Equal(DaggerfallDeathAudioCue.PlayerDeath, view.AudioCue);
        Assert.Equal(3, view.Choices.Count);
        Assert.True(view.Choices.Single(choice => choice.Id == DaggerfallDeathChoiceId.NewGame).Available);
        Assert.False(view.Choices.Single(choice => choice.Id == DaggerfallDeathChoiceId.LoadGame).Available);
        Assert.True(view.Choices.Single(choice => choice.Id == DaggerfallDeathChoiceId.QuitToTitle).Available);

        ulong revision = view.Revision;
        Assert.False(presentation.Enter());
        Assert.Equal(revision, presentation.View.Revision);
    }

    [Fact]
    public void Load_requires_a_real_slot_and_one_selected_choice_is_latched()
    {
        DaggerfallDeathPresentation presentation = new();
        presentation.Enter();

        Assert.False(presentation.TrySelect(DaggerfallDeathPresentation.LoadGameAction, out _));
        presentation.SetLoadAvailable(true);
        Assert.True(presentation.TrySelect(DaggerfallDeathPresentation.LoadGameAction, out DaggerfallDeathChoiceId selected));
        Assert.Equal(DaggerfallDeathChoiceId.LoadGame, selected);
        Assert.Equal(DaggerfallDeathChoiceId.LoadGame, presentation.View.Selected);
        Assert.False(presentation.TrySelect(DaggerfallDeathPresentation.QuitAction, out _));
        Assert.False(presentation.TrySelect("unknown", out _));
    }

    [Fact]
    public void Clear_removes_transient_death_effects_and_allows_the_next_session_to_enter()
    {
        DaggerfallDeathPresentation presentation = new();
        presentation.Enter();
        presentation.SetLoadAvailable(true);
        Assert.True(presentation.TrySelect(DaggerfallDeathPresentation.NewGameAction, out _));

        presentation.Clear();
        Assert.False(presentation.View.Active);
        Assert.Null(presentation.View.Selected);
        Assert.False(presentation.View.ControlsSuppressed);
        Assert.Equal(DaggerfallDeathCameraEffect.None, presentation.View.CameraEffect);
        Assert.True(presentation.Enter());
        Assert.Null(presentation.View.Selected);
    }

    [Fact]
    public void A_failed_load_can_reopen_the_death_choices()
    {
        DaggerfallDeathPresentation presentation = new();
        presentation.Enter();
        presentation.SetLoadAvailable(true);
        Assert.True(presentation.TrySelect(DaggerfallDeathPresentation.LoadGameAction, out _));

        presentation.ClearSelection();

        Assert.Null(presentation.View.Selected);
        Assert.True(presentation.View.Choices.All(choice => choice.Available));
        Assert.True(presentation.TrySelect(DaggerfallDeathPresentation.QuitAction, out _));
    }
}
