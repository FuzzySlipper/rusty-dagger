using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Records the parts rigs a session loads, the appearances it makes of them and the clips it plays, and which are released.</summary>
public class PartsRigsRecorder : DispatchProxy
{
    internal IPartsRigsService Service { get; private set; } = null!;
    internal List<string> Loaded { get; } = [];
    internal List<(Appearance Appearance, PartsRigAppearanceRequest Request)> Appearances { get; } = [];
    internal List<PartsRigPlayRequest> Plays { get; } = [];
    internal List<PartsRigStopRequest> Stops { get; } = [];
    internal int ReleasedRigs { get; private set; }
    internal HashSet<Appearance> ReleasedAppearances { get; } = [];
    private ulong next = 1;

    internal static PartsRigsRecorder Create()
    {
        IPartsRigsService service = Create<IPartsRigsService, PartsRigsRecorder>();
        PartsRigsRecorder recorder = (PartsRigsRecorder)(object)service;
        recorder.Service = service;
        return recorder;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        switch (arguments![0])
        {
            case PartsRigLoadRequest load:
                Loaded.Add(load.Path);
                return new PartsRigInfo(new PartsRig(new PartsRigHandle(next++), () => ReleasedRigs++), 1, 1, 3, 1, 1);
            case PartsRigAppearanceRequest request:
                Appearance appearance = null!;
                appearance = new Appearance(new AppearanceHandle(next++), () => ReleasedAppearances.Add(appearance));
                Appearances.Add((appearance, request));
                return appearance;
            case PartsRigPlayRequest play:
                Plays.Add(play);
                return null;
            case PartsRigStopRequest stop:
                Stops.Add(stop);
                return null;
            default:
                throw new NotSupportedException(method?.Name);
        }
    }
}
