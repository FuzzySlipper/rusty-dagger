using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal readonly record struct DaggerfallAmbientContext(DaggerfallWorldProfileKey Profile,
    DaggerfallWeatherKind Weather, bool Night, bool Sheltered, bool Castle, bool SpecialArea);

/// <summary>One session's weather emitter and ambient audio, stepped only by admitted product updates.</summary>
internal sealed class DaggerfallAmbientPresentation : IDisposable
{
    private static long s_nextEmitter;
    private readonly IPresentationService _presentation;
    private readonly IAudioService _audio;
    private readonly IRandomService _random;
    private readonly DaggerfallAmbientTuning _tuning;
    private readonly DaggerfallPresentationAudioTuning _audioTuning;
    private readonly Func<DaggerfallWorldProfileKey, string, AudioClip?> _openClip;
    private readonly Func<bool, RenderResourceReference> _precipitationSprite;
    private readonly Dictionary<(DaggerfallWorldProfileKey, string), AudioClip> _clips = [];
    private readonly HashSet<AudioSignalHandle> _oneShots = [];
    private readonly ulong _emitterId = checked((ulong)Interlocked.Increment(ref s_nextEmitter));
    private PresentationEmitter? _emitter;
    private AudioVoice? _loop;
    private string? _loopClip;
    private DaggerfallAmbientContext? _context;
    private bool _playing;
    private double _wait;
    private int _flashSteps;
    private double _flashWait;
    private double _thunderWait;
    private string? _thunderClip;
    private bool _flashOn;
    private ulong _draw;
    private ulong _signal;
    private bool _disposed;

    internal DaggerfallAmbientPresentation(IPresentationService presentation, IAudioService audio,
        IRandomService random, DaggerfallAmbientTuning tuning,
        Func<DaggerfallWorldProfileKey, string, AudioClip?> openClip,
        Func<bool, RenderResourceReference> precipitationSprite, DaggerfallPresentationAudioTuning? audioTuning = null)
    {
        _presentation = presentation; _audio = audio; _random = random;
        _tuning = tuning.Validate(); _openClip = openClip; _precipitationSprite = precipitationSprite;
        _audioTuning = (audioTuning ?? DaggerfallTuning.Defaults.PresentationAudio).Validate();
    }

    internal float FlashIntensity => _flashOn ? _tuning.FlashIntensity : 0;

    internal void Update(DaggerfallAmbientContext context, Vector3 player, double admittedSeconds, bool playing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(admittedSeconds) || admittedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(admittedSeconds));
        RetireRealizedOneShots();
        bool changed = _context != context;
        if (changed)
        {
            RetireLoop(); RetireEmitter();
            _flashOn = false; _flashSteps = 0; _thunderClip = null; _thunderWait = 0;
            _wait = NextWait();
            _context = context;
        }
        bool exterior = context.Profile.Kind == DaggerfallWorldProfileKind.Exterior;
        bool rain = context.Weather is DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder;
        string? loop = exterior ? rain ? "ambient.rain"
            : context.Night && context.Weather is DaggerfallWeatherKind.Sunny or DaggerfallWeatherKind.Cloudy
                ? "ambient.crickets" : null : null;
        if (_loopClip != loop)
        {
            RetireLoop();
            if (loop is not null && Clip(context.Profile, loop) is { } clip)
            {
                _loop = _audio.CreateVoice(Descriptor(clip, looping: true));
                _loopClip = loop;
                // New voices start running; immediately pause a loop created by a held-context change.
                if (!playing) _audio.ControlVoice(new(_loop, AudioVoiceControl.Pause));
            }
        }
        if (!changed && playing != _playing && _loop is { } voice)
            _audio.ControlVoice(new(voice, playing ? AudioVoiceControl.Resume : AudioVoiceControl.Pause));
        _playing = playing;

        bool snow = context.Weather == DaggerfallWeatherKind.Snow;
        bool precipitating = playing && exterior && !context.Sheltered && (rain || snow);
        if (precipitating)
        {
            PresentationParticleDescriptor descriptor = Precipitation(player, snow);
            if (_emitter is null) _emitter = _presentation.CreateEmitter(descriptor);
            else _presentation.UpdateEmitter(_emitter, descriptor);
        }
        else RetireEmitter();
        if (!playing || admittedSeconds == 0) return;

        AdvanceLightning(context, player, admittedSeconds);
        bool hasOneShots = exterior
            ? context.Weather == DaggerfallWeatherKind.Thunder || !context.Night && context.Weather is DaggerfallWeatherKind.Sunny or DaggerfallWeatherKind.Cloudy
            : context.Profile.Kind == DaggerfallWorldProfileKind.Dungeon && !context.Castle;
        if (!hasOneShots) return;
        _wait -= admittedSeconds;
        if (_wait > 0) return;
        _wait = NextWait();
        if (exterior && context.Weather == DaggerfallWeatherKind.Thunder)
        {
            if (_thunderClip is not null) return;
            int type = Draw("storm-type", 0, 2);
            _thunderClip = new[] {"ambient.thunder-short", "ambient.thunder", "ambient.lightning-roll"}[type];
            _flashSteps = Draw("storm-flashes", type == 0 ? 4 : type == 1 ? 5 : 20, type == 0 ? 7 : type == 1 ? 9 : 29) * 2;
            _flashWait = 0;
            _thunderWait = type == 2 ? _tuning.RollingThunderDelay : 0;
        }
        else if (exterior)
            Emit(context.Profile, Draw("bird", 1, 2) == 1 ? "ambient.bird1" : "ambient.bird2", player);
        else
            Emit(context.Profile, $"dungeon.ambient.{Draw("dungeon", 1, 14):00}", player);
    }

    private void AdvanceLightning(DaggerfallAmbientContext context, Vector3 player, double seconds)
    {
        if (_thunderClip is null) return;
        _flashWait -= seconds;
        // This bounded sequence is presentation within the Engine-admitted interval, not a timer or
        // world clock. Catch-up visits its finite remaining flashes and retains the final base light.
        while (_flashSteps > 0 && _flashWait <= 0)
        {
            _flashOn = _flashSteps % 2 == 0 && Draw("flash-skip", 0, 999) < _tuning.FlashSkipChance * 1000;
            _flashSteps--; _flashWait += _tuning.FlashSeconds;
        }
        if (_flashSteps > 0) return;
        _flashOn = false;
        _thunderWait -= Math.Max(0, -_flashWait);
        _flashWait = 0;
        if (_thunderWait > 0) return;
        Emit(context.Profile, _thunderClip, player);
        _thunderClip = null;
    }

    private PresentationParticleDescriptor Precipitation(Vector3 player, bool snow)
    {
        float speed = snow ? _tuning.SnowSpeed : _tuning.RainSpeed;
        float size = snow ? _tuning.SnowSize : _tuning.RainSize;
        float life = snow ? _tuning.SnowLifetime : _tuning.RainLifetime;
        float spread = snow ? _tuning.SnowSpread : _tuning.RainSpread;
        return new PresentationParticleDescriptor(_emitterId, $"daggerfall.weather.{_emitterId}",
            new(PresentationAnchorKind.World, player + Vector3.UnitY * _tuning.PrecipitationHeight, 0, Vector3.Zero),
            PresentationParticleVisual.Billboard, _precipitationSprite(snow), 1,
            snow ? _tuning.SnowRate : _tuning.RainRate, 0, life, life,
            new(-spread, -speed, -spread), new(spread, -speed, spread), Vector3.Zero,
            new PresentationParticleScalarKey[] {new(0, size), new(1, size)},
            new PresentationParticleColorKey[] {new(0, new Color(1,1,1,.7f)), new(1, new Color(1,1,1,0))},
            0, _emitterId, _tuning.ParticleBudget, true, false, default,
            ReadOnlyMemory<PresentationParticleCollisionVolume>.Empty, PresentationParticleSizeMode.World);
    }

    private AudioClip? Clip(DaggerfallWorldProfileKey profile, string id)
    {
        if (_clips.TryGetValue((profile, id), out var existing)) return existing;
        AudioClip? clip = _openClip(profile, id);
        if (clip is not null) _clips.Add((profile, id), clip);
        return clip;
    }

    private AudioSourceDescriptor Descriptor(AudioClip clip, bool looping) => new(clip, AudioBus.Ambient,
        _audioTuning.Volume, _audioTuning.Pitch, looping, 0, _audioTuning.MaxDistance, AudioRolloff.Linear, 0, AudioEmitterKind.Global2d, Vector3.Zero, 0, Vector3.Zero);

    private void Emit(DaggerfallWorldProfileKey profile, string id, Vector3 player)
    {
        if (Clip(profile, id) is not { } clip) return;
        float angle = Draw("sound-angle", 0, 359) * MathF.PI / 180;
        bool thunder = id.StartsWith("ambient.thunder", StringComparison.Ordinal) || id == "ambient.lightning-roll";
        float distance = thunder ? 10000 : Draw("sound-distance", 10, 20);
        Vector3 position = player + new Vector3(MathF.Sin(angle), thunder ? .34f : 0, MathF.Cos(angle)) * distance;
        var descriptor = Descriptor(clip, false) with {SpatialBlend = 1, MaxDistance = thunder ? 24000 : 104,
            EmitterKind = AudioEmitterKind.World3d, Position = position};
        _oneShots.Add(_audio.Emit(new($"daggerfall.ambient.{_emitterId}.{++_signal}.{id}", descriptor)));
    }

    private int Draw(string purpose, int minimum, int maximum) => checked((int)_random.DrawKeyed(new(0,
        "daggerfall.ambient", $"{_emitterId}:{purpose}:{++_draw}", minimum, maximum)).Value);
    private int NextWait() => Draw("wait", _tuning.MinimumWaitSeconds, _tuning.MaximumWaitSeconds - 1);
    private void RetireLoop() {var loop = _loop; _loop = null; _loopClip = null; loop?.Dispose();}
    private void RetireEmitter() {var emitter = _emitter; _emitter = null; emitter?.Dispose();}
    private void RetireRealizedOneShots()
    {
        if (_oneShots.Count == 0) return;
        AudioRealizationResult realization = _audio.ReadRealization();
        foreach (AudioRealizationFact fact in realization.Facts.Span)
            if (fact.SignalHandle != 0
                && (fact.Kind is AudioRealizationFactKind.NaturalCompletionOneShot or AudioRealizationFactKind.Diagnostic))
                _oneShots.Remove(new AudioSignalHandle(fact.SignalHandle));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception> failures = [];
        try {RetireLoop();} catch (Exception error) {failures.Add(error);}
        try {RetireEmitter();} catch (Exception error) {failures.Add(error);}
        foreach (AudioSignalHandle signal in _oneShots)
            try {_audio.RetireOneShot(signal);} catch (Exception error) {failures.Add(error);}
        _oneShots.Clear();
        foreach (AudioClip clip in _clips.Values)
            try {clip.Dispose();} catch (Exception error) {failures.Add(error);}
        _clips.Clear();
        if (failures.Count != 0) throw new AggregateException(failures);
    }
}
