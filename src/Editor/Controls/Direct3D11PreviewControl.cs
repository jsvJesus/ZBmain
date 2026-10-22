using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Core.Levels;
using Editor.Rendering.Direct3D11;

namespace Editor.Controls;

public sealed class Direct3D11PreviewControl : NativeControlHost
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsClipChildren = 0x02000000;

    private readonly DispatcherTimer _timer;

    private VegetationPreviewRenderer? _renderer;
    private nint _windowHandle;

    public Direct3D11PreviewControl()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += RenderTick;
    }

    public DetailLevel? Vegetation { get; set; }

    public string LevelName { get; set; } = string.Empty;

    public event Action<string>? RenderFailed;

    protected override IPlatformHandle CreateNativeControlCore(
        IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Direct3D 11 поддерживается только в Windows.");
        }

        nint handle = CreateWindowExW(
            0,
            "STATIC",
            string.Empty,
            WsChild |
            WsVisible |
            WsClipSiblings |
            WsClipChildren,
            0,
            0,
            1,
            1,
            parent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
        }

        _windowHandle = handle;

        if (Vegetation is not null)
        {
            try
            {
                _renderer = new VegetationPreviewRenderer();

                _renderer.Initialize(
                    _windowHandle,
                    Vegetation,
                    LevelName);

                _timer.Start();
            }
            catch (Exception exception)
            {
                _timer.Stop();

                _renderer?.Dispose();
                _renderer = null;

                RenderFailed?.Invoke(
                    "Ошибка Direct3D 11: " +
                    exception.Message);
            }
        }

        return new PlatformHandle(handle, "HWND");
    }

    protected override void DestroyNativeControlCore(
        IPlatformHandle control)
    {
        _timer.Stop();

        _renderer?.Dispose();
        _renderer = null;

        _windowHandle = IntPtr.Zero;

        if (control.Handle != IntPtr.Zero)
        {
            DestroyWindow(control.Handle);
        }
    }

    private void RenderTick(
        object? sender,
        EventArgs e)
    {
        if (_renderer is null ||
            _windowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _renderer.Render(_windowHandle);
        }
        catch (Exception exception)
        {
            _timer.Stop();

            _renderer.Dispose();
            _renderer = null;

            RenderFailed?.Invoke(
                "Ошибка рендеринга DX11: " +
                exception.Message);
        }
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "CreateWindowExW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hWnd);
}
