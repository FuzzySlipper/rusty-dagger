using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace RustyDagger.Product;

public enum InputKind : uint { Key = 1, PointerButton = 2, PointerDelta = 3, Wheel = 4, ControllerButton = 5, ControllerAxis = 6, Clear = 7, DigitalIntent = 8, AxisIntent = 9, UiIntent = 10 }
public enum InputEdge : uint { None = 0, Press = 1, Release = 2 }
public readonly record struct PhysicalInput(InputKind Kind, InputEdge Edge, ulong Sequence, float X, float Y, string Label);

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ContentFile { public byte* Path; public nuint PathLength; public byte* Bytes; public nuint BytesLength; }
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CreateArgs { public ContentFile* Content; public nuint ContentLength; }
[StructLayout(LayoutKind.Sequential)]
public unsafe struct InputEvent { public uint Kind; public uint Edge; public ulong Sequence; public float X; public float Y; public byte* Label; public nuint LabelLength; }
[StructLayout(LayoutKind.Sequential)]
public unsafe struct TurnArgs { public uint Kind; public uint Reserved; public ulong ObservedTimeOrStep; public InputEvent* Events; public nuint EventCount; }
[StructLayout(LayoutKind.Sequential)]
public unsafe struct OutputBuffer { public byte* Data; public nuint Length; }

public static unsafe class NativeExports
{
    private static long _freedOutputs;
    [UnmanagedCallersOnly(EntryPoint = "rusty_product_create", CallConvs = [typeof(CallConvCdecl)])]
    public static int Create(CreateArgs* args, void** outHandle, OutputBuffer* output)
    {
        try
        {
            if (args is null || outHandle is null || output is null) return 0;
            var nav = FindContent(args, "privateers-hold.navgrid.json");
            if (nav is null) return 0;
            var project = FindContent(args, "privateers-hold.project.json");
            var playerStart = project is null
                ? new WorldPoint(28.375f, 38.975002f, -12.400001f)
                : FindNamedPlayerStart(project) ?? new WorldPoint(28.375f, 38.975002f, -12.400001f);
            var session = new GameplaySession(NavigationGrid.FromJson(nav), playerStart);
            var handle = GCHandle.Alloc(session);
            *outHandle = (void*)GCHandle.ToIntPtr(handle);
            Write(output, session.ToCreateJson(Interlocked.Read(ref _freedOutputs)));
            return 1;
        }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(EntryPoint = "rusty_product_start", CallConvs = [typeof(CallConvCdecl)])]
    public static int Start(void* handle, OutputBuffer* output) => WriteSession(handle, output);

    [UnmanagedCallersOnly(EntryPoint = "rusty_product_turn", CallConvs = [typeof(CallConvCdecl)])]
    public static int Turn(void* handle, TurnArgs* args, OutputBuffer* output)
    {
        try
        {
            if (handle is null || args is null || output is null) return 0;
            if (args->EventCount > 0 && args->Events is null) return 0;
            var session = Session(handle);
            var inputs = new List<PhysicalInput>((int)args->EventCount);
            for (nuint index = 0; index < args->EventCount; index++)
            {
                var value = args->Events[index];
                inputs.Add(new PhysicalInput((InputKind)value.Kind, (InputEdge)value.Edge, value.Sequence, value.X, value.Y, Utf8(value.Label, value.LabelLength)));
            }
            session.ApplyTurn(args->ObservedTimeOrStep, args->Kind == 1, inputs);
            Write(output, session.ToOutputJson(Interlocked.Read(ref _freedOutputs)));
            return 1;
        }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(EntryPoint = "rusty_product_pause", CallConvs = [typeof(CallConvCdecl)])]
    public static int Pause(void* handle, OutputBuffer* output) => WriteSession(handle, output);
    [UnmanagedCallersOnly(EntryPoint = "rusty_product_resume", CallConvs = [typeof(CallConvCdecl)])]
    public static int Resume(void* handle, OutputBuffer* output) => WriteSession(handle, output);
    [UnmanagedCallersOnly(EntryPoint = "rusty_product_shutdown", CallConvs = [typeof(CallConvCdecl)])]
    public static int Shutdown(void* handle, OutputBuffer* output)
    {
        if (handle is null) return 0;
        // Engine Drop has no need for a final projection and is allowed to
        // call shutdown with a null buffer before destroy.
        return output is null ? 1 : WriteSession(handle, output);
    }

    [UnmanagedCallersOnly(EntryPoint = "rusty_product_destroy", CallConvs = [typeof(CallConvCdecl)])]
    public static void Destroy(void* handle)
    {
        if (handle is not null) GCHandle.FromIntPtr((nint)handle).Free();
    }

    [UnmanagedCallersOnly(EntryPoint = "rusty_product_free_output", CallConvs = [typeof(CallConvCdecl)])]
    public static void FreeOutput(OutputBuffer buffer)
    {
        if (buffer.Data is not null)
        {
            NativeMemory.Free(buffer.Data);
            Interlocked.Increment(ref _freedOutputs);
        }
    }

    private static int WriteSession(void* handle, OutputBuffer* output)
    {
        try { if (handle is null || output is null) return 0; Write(output, Session(handle).ToOutputJson(Interlocked.Read(ref _freedOutputs))); return 1; }
        catch { return 0; }
    }

    private static GameplaySession Session(void* handle) => (GameplaySession)GCHandle.FromIntPtr((nint)handle).Target!;

    private static void Write(OutputBuffer* output, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        output->Data = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
        output->Length = (nuint)bytes.Length;
        bytes.CopyTo(new Span<byte>(output->Data, bytes.Length));
    }

    private static byte[]? FindContent(CreateArgs* args, string suffix)
    {
        for (nuint index = 0; index < args->ContentLength; index++)
        {
            var file = args->Content[index];
            if (Utf8(file.Path, file.PathLength).EndsWith(suffix, StringComparison.Ordinal))
                return new ReadOnlySpan<byte>(file.Bytes, checked((int)file.BytesLength)).ToArray();
        }
        return null;
    }

    private static WorldPoint? FindNamedPlayerStart(byte[] projectBytes)
    {
        using var document = JsonDocument.Parse(projectBytes);
        return FindNamedPlayerStart(document.RootElement);
    }

    private static WorldPoint? FindNamedPlayerStart(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                && name.GetString() == "player"
                && value.TryGetProperty("translation", out var translation)
                && translation.ValueKind == JsonValueKind.Array
                && translation.GetArrayLength() == 3)
                return new WorldPoint(translation[0].GetSingle(), translation[1].GetSingle(), translation[2].GetSingle());
            foreach (var property in value.EnumerateObject())
            {
                var found = FindNamedPlayerStart(property.Value);
                if (found is not null) return found;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var found = FindNamedPlayerStart(item);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static string Utf8(byte* text, nuint length) => text is null || length == 0 ? string.Empty : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(text, checked((int)length)));
}
