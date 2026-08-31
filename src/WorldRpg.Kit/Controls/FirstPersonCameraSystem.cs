using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

/// <summary>Typed product tuning for the Engine-owned first-person camera.</summary>
public sealed record FirstPersonCameraTuning(
    float EyeHeight,
    double FieldOfViewYDegrees,
    double NearPlane,
    double FarPlane)
{
    public FirstPersonCameraTuning Validate()
    {
        if (!float.IsFinite(EyeHeight) || EyeHeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(EyeHeight));
        if (!double.IsFinite(FieldOfViewYDegrees) || FieldOfViewYDegrees <= 0d || FieldOfViewYDegrees >= 180d)
            throw new ArgumentOutOfRangeException(nameof(FieldOfViewYDegrees));
        if (!double.IsFinite(NearPlane) || NearPlane <= 0d)
            throw new ArgumentOutOfRangeException(nameof(NearPlane));
        if (!double.IsFinite(FarPlane) || FarPlane <= NearPlane)
            throw new ArgumentOutOfRangeException(nameof(FarPlane));
        return this;
    }
}

/// <summary>
/// Keeps one Engine-owned camera synchronized with an authoritative player
/// control state. The product supplies pose facts; the Engine owns realization.
/// </summary>
public sealed class FirstPersonCameraSystem : IDisposable
{
    private static readonly CameraViewport FullViewport = new(0d, 0d, 1d, 1d);
    private static readonly double RadiansToDegrees = 180d / Math.PI;

    private readonly ICameraViewService _cameraView;
    private readonly Camera _camera;
    private readonly PlayerControlState _player;
    private readonly FirstPersonCameraTuning _tuning;
    private bool _disposed;

    public FirstPersonCameraSystem(
        ICameraViewService cameraView,
        PlayerControlState player,
        FirstPersonCameraTuning tuning)
    {
        _cameraView = cameraView ?? throw new ArgumentNullException(nameof(cameraView));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();

        Camera? camera = null;
        try
        {
            camera = _cameraView.CreateCamera(Descriptor(_player));
            _cameraView.SetActiveCamera(camera);
            _camera = camera;
        }
        catch
        {
            camera?.Dispose();
            throw;
        }
    }

    public void Update(PlayerControlState player)
    {
        if (_disposed) return;
        if (!ReferenceEquals(_player, player))
        {
            throw new InvalidOperationException("A first-person camera remains bound to its constructed player control state.");
        }

        _cameraView.UpdateCamera(new CameraUpdateRequest(_camera, Descriptor(_player)));
    }

    /// <summary>The derived eye point submitted to the Engine camera descriptor.</summary>
    public WorldPoint Viewpoint => ViewpointFor(_player);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        List<Exception>? failures = null;
        try { _cameraView.ClearActiveCamera(new ClearActiveCameraRequest(0)); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { _camera.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }

        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private CameraDescriptor Descriptor(PlayerControlState player)
    {
        WorldPoint viewpoint = ViewpointFor(player);
        return new CameraDescriptor(
            new CameraPose(
                viewpoint.ToVector(),
                player.PitchRadians * RadiansToDegrees,
                player.YawRadians * RadiansToDegrees),
            CameraBasisMode.Derived,
            default,
            new CameraProjection(
                CameraProjectionKind.Perspective,
                _tuning.FieldOfViewYDegrees,
                0d,
                _tuning.NearPlane,
                _tuning.FarPlane),
            FullViewport);
    }

    private WorldPoint ViewpointFor(PlayerControlState player)
    {
        WorldPoint position = player.Position ?? throw new InvalidOperationException("A first-person camera requires a player position.");
        return WorldPoint.From(position.ToVector() + (Vector3.UnitY * _tuning.EyeHeight));
    }
}
