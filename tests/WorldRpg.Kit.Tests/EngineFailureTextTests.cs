using Rusty.Engine;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>
/// The failure text is what makes a refused Engine call diagnosable: the exception's own message names
/// only the service, operation and status, so the reasons the call carried have to be stated beside it.
/// </summary>
public sealed class EngineFailureTextTests
{
    [Fact]
    public void A_refused_engine_call_states_the_reasons_it_carried()
    {
        EngineCallException failure = new(
            "Audio",
            "CreateVoice",
            0,
            new EngineDiagnostic[]
            {
                new("CSHARP_AUDIO_CLIP_HANDLE", "audio clip handle is not admitted", "csharp-engine-services"),
            });

        string text = EngineFailureText.Describe(failure);

        Assert.Contains("Audio.CreateVoice returned status 0", text, StringComparison.Ordinal);
        Assert.Contains("CSHARP_AUDIO_CLIP_HANDLE", text, StringComparison.Ordinal);
        Assert.Contains("audio clip handle is not admitted", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_with_no_reasons_still_names_itself()
    {
        string text = EngineFailureText.Describe(new EngineCallException("Graphics", "DestroyAppearance", 0));

        Assert.Contains("Graphics.DestroyAppearance returned status 0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_states_every_failure_it_collected()
    {
        AggregateException failure = new(
            new EngineCallException("Audio", "CreateVoice", 0),
            new InvalidOperationException("retired SpriteAtlas was not released"));

        string text = EngineFailureText.Describe(failure);

        Assert.Contains("Audio.CreateVoice returned status 0", text, StringComparison.Ordinal);
        Assert.Contains("retired SpriteAtlas was not released", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_cause_is_stated_too()
    {
        InvalidOperationException failure = new("the score could not start", new EngineCallException("Audio", "CreateVoice", 7));

        string text = EngineFailureText.Describe(failure);

        Assert.Contains("the score could not start", text, StringComparison.Ordinal);
        Assert.Contains("Audio.CreateVoice returned status 7", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_without_reasons_is_rendered_as_the_message_alone()
    {
        string text = EngineFailureText.Describe(new EngineCallException("Graphics", "DestroySpriteAtlas", 0));

        Assert.Equal("EngineCallException: Rusty Engine Graphics.DestroySpriteAtlas returned status 0.", text);
    }
}
