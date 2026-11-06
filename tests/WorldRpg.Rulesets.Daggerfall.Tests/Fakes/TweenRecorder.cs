using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Records the tweens a presentation starts and the controls it sends them.</summary>
internal class TweenRecorder : DispatchProxy
{
    internal ITweenService Service { get; private set; } = null!;
    internal List<TweenStartRequest> Started { get; } = [];
    internal List<TweenControlRequest> Controls { get; } = [];
    internal static TweenRecorder Create()
    {
        var service = Create<ITweenService, TweenRecorder>();
        var recorder = (TweenRecorder)(object)service;
        recorder.Service = service; return recorder;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method?.Name)
        {
            case nameof(ITweenService.Start):
                Started.Add((TweenStartRequest)args![0]!);
                return Activator.CreateInstance(method.ReturnType);
            case nameof(ITweenService.Control):
                Controls.Add((TweenControlRequest)args![0]!);
                return Activator.CreateInstance(method.ReturnType);
            default: throw new NotSupportedException(method?.Name);
        }
    }
}
