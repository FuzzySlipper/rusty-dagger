using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rusty.Engine.Native;

namespace RustyDagger.Product;

/// <summary>ABI composition only. Dagger gameplay starts at <see cref="DaggerRuntime"/>.</summary>
public static unsafe class NativeProduct
{
    [UnmanagedCallersOnly(EntryPoint = "rusty_product_bind", CallConvs = [typeof(CallConvCdecl)])]
    public static int Bind(NativeProductApi* api)
    {
        if (api is null) return 2;
        api->create = &Create;
        api->start = &Start;
        api->turn = &Turn;
        api->pause = &Pause;
        api->resume = &Resume;
        api->shutdown = &Shutdown;
        api->destroy = &Destroy;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Create(NativeProductCreateArgs* args, void** handle)
    {
        try
        {
            if (args is null || handle is null || (args->content_len != 0 && args->content is null)) return 2;
            var content = PrivateersHoldContent.Read(args);
            var engine = new EngineApi(args->engine);
            var runtime = new DaggerRuntime(engine, content);
            *handle = (void*)GCHandle.ToIntPtr(GCHandle.Alloc(runtime));
            return 1;
        }
        catch { return 99; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Start(void* handle) => WithRuntime(handle, runtime => runtime.Start());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Turn(void* handle, NativeTurnArgs* args) => WithRuntime(handle, runtime => runtime.Turn(args));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Pause(void* handle) => WithRuntime(handle, runtime => runtime.Pause());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Resume(void* handle) => WithRuntime(handle, runtime => runtime.Resume());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Shutdown(void* handle) => WithRuntime(handle, runtime => runtime.Shutdown());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Destroy(void* handle)
    {
        if (handle is not null)
        {
            var pinned = GCHandle.FromIntPtr((nint)handle);
            ((DaggerRuntime)pinned.Target!).Dispose();
            pinned.Free();
        }
    }

    private static int WithRuntime(void* handle, Func<DaggerRuntime, int> action)
    {
        try
        {
            if (handle is null) return 2;
            return action((DaggerRuntime)GCHandle.FromIntPtr((nint)handle).Target!);
        }
        catch { return 99; }
    }
}
