using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Kadr.Recording;

/// <summary>Мост между Direct3D 11 (Vortice) и WinRT-типами Windows.Graphics.Capture / MediaStreamSource.</summary>
public static class Direct3DInterop
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11SurfaceFromDXGISurface")]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);

    public static ID3D11Device CreateD3DDevice()
    {
        var flags = DeviceCreationFlags.BgraSupport;
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        var hr = D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels, out ID3D11Device device);
        if (hr.Failure)
        {
            hr = D3D11.D3D11CreateDevice(null, DriverType.Warp, flags, levels, out device);
            hr.CheckError();
        }
        // Media Foundation использует устройство из своих потоков: без этого контекст зависает.
        using (var multithread = device.QueryInterface<ID3D11Multithread>())
            multithread.SetMultithreadProtected(true);
        return device;
    }

    public static IDirect3DDevice CreateWinRTDevice(ID3D11Device device)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var ptr));
        try { return MarshalInterface<IDirect3DDevice>.FromAbi(ptr); }
        finally { Marshal.Release(ptr); }
    }

    public static IDirect3DSurface CreateWinRTSurface(ID3D11Texture2D texture)
    {
        using var dxgi = texture.QueryInterface<IDXGISurface>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgi.NativePointer, out var ptr));
        try { return MarshalInterface<IDirect3DSurface>.FromAbi(ptr); }
        finally { Marshal.Release(ptr); }
    }

    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = typeof(ID3D11Texture2D).GUID;
        var ptr = access.GetInterface(ref iid);
        return new ID3D11Texture2D(ptr);
    }

    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hMonitor)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = GraphicsCaptureItemGuid;
        var ptr = interop.CreateForMonitor(hMonitor, ref iid);
        try { return GraphicsCaptureItem.FromAbi(ptr); }
        finally { Marshal.Release(ptr); }
    }
}
