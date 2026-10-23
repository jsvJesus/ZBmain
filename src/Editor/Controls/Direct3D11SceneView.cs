using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Core.Levels;
using Editor.Rendering.Direct3D11;

namespace Editor.Controls;

public sealed class Direct3D11SceneView : NativeControlHost
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsClipChildren = 0x02000000;

    private const uint WmMouseMove = 0x0200;
    private const uint WmRightButtonDown = 0x0204;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmKillFocus = 0x0008;

    private const int VkW = 0x57;
    private const int VkA = 0x41;
    private const int VkS = 0x53;
    private const int VkD = 0x44;
    private const int VkSpace = 0x20;
    private const int VkShift = 0x10;
    private const int VkHome = 0x24;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly HashSet<int> _keys = new();

    private readonly SubclassProcedure _procedure;

    private SceneViewportRenderer? _renderer;
    private RenderLevel? _scene;
    private DetailLevel? _vegetation;

    private nint _handle;
    private bool _subclassInstalled;
    private bool _rightButtonHeld;
    private bool _renderingEnabled = true;

    private int _lastX;
    private int _lastY;
    private double _lastFrameTime;

    public event Action<string>? RenderFailed;

    public Direct3D11SceneView()
    {
        _procedure = WindowProcedure;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += RenderTick;
    }

    public void SetScene(
        RenderLevel? scene,
        DetailLevel? vegetation)
    {
        _scene = scene;
        _vegetation = vegetation;

        if (_renderer is null)
        {
            return;
        }

        try
        {
            _renderer.SetScene(scene, vegetation);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    public void SetRenderingEnabled(bool enabled)
    {
        _renderingEnabled = enabled;

        if (!enabled)
        {
            _timer.Stop();
            _keys.Clear();
            _rightButtonHeld = false;
        }
        else if (_renderer is not null &&
                 _handle != IntPtr.Zero)
        {
            _lastFrameTime = _clock.Elapsed.TotalSeconds;
            _timer.Start();
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(
        IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Direct3D 11 требует Windows.");
        }

        nint handle = CreateWindowExW(
            0,
            "STATIC",
            string.Empty,
            WsChild |
            WsVisible |
            WsTabStop |
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

        _handle = handle;

        try
        {
            _renderer = new SceneViewportRenderer();
            _renderer.Initialize(handle);
            _renderer.SetScene(_scene, _vegetation);

            if (!SetWindowSubclass(
                    handle,
                    _procedure,
                    1,
                    0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            _subclassInstalled = true;

            _clock.Restart();
            _lastFrameTime = 0;

            if (_renderingEnabled)
            {
                _timer.Start();
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }

        return new PlatformHandle(handle, "HWND");
    }

    protected override void DestroyNativeControlCore(
        IPlatformHandle control)
    {
        _timer.Stop();
        _clock.Stop();

        _keys.Clear();
        _rightButtonHeld = false;

        if (_subclassInstalled)
        {
            RemoveWindowSubclass(
                control.Handle,
                _procedure,
                1);

            _subclassInstalled = false;
        }

        _renderer?.Dispose();
        _renderer = null;

        _handle = IntPtr.Zero;

        if (control.Handle != IntPtr.Zero)
        {
            DestroyWindow(control.Handle);
        }
    }

    private nint WindowProcedure(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        switch (message)
        {
            case WmRightButtonDown:
                SetFocus(hwnd);
                SetCapture(hwnd);

                _rightButtonHeld = true;

                ReadPosition(
                    lParam,
                    out _lastX,
                    out _lastY);

                return IntPtr.Zero;

            case WmRightButtonUp:
                _rightButtonHeld = false;
                ReleaseCapture();
                return IntPtr.Zero;

            case WmMouseMove:
                if (_rightButtonHeld)
                {
                    ReadPosition(
                        lParam,
                        out int x,
                        out int y);

                    _renderer?.Look(
                        x - _lastX,
                        y - _lastY);

                    _lastX = x;
                    _lastY = y;
                }

                break;

            case WmMouseWheel:
                short wheel = unchecked((short)(
                    ((ulong)wParam >> 16) & 0xFFFF));

                _renderer?.AdjustSpeed(
                    wheel / 120f);

                return IntPtr.Zero;

            case WmKeyDown:
                int keyDown = (int)wParam;

                if (keyDown == VkHome)
                {
                    _renderer?.ShowMapTopDown();
                }
                else
                {
                    _keys.Add(keyDown);
                }

                return IntPtr.Zero;

            case WmKeyUp:
                _keys.Remove((int)wParam);
                return IntPtr.Zero;

            case WmKillFocus:
                _keys.Clear();
                _rightButtonHeld = false;
                ReleaseCapture();
                break;
        }

        return DefSubclassProc(
            hwnd,
            message,
            wParam,
            lParam);
    }

    private void RenderTick(
        object? sender,
        EventArgs e)
    {
        if (!_renderingEnabled ||
            _renderer is null ||
            _handle == IntPtr.Zero)
        {
            return;
        }

        double now = _clock.Elapsed.TotalSeconds;

        float deltaTime = (float)Math.Clamp(
            now - _lastFrameTime,
            0.0,
            0.05);

        _lastFrameTime = now;

        Vector3 movement = GetMovement();

        if (movement.LengthSquared() > 0f)
        {
            _renderer.Fly(
                movement,
                deltaTime);
        }

        try
        {
            _renderer.Render(_handle);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private Vector3 GetMovement()
    {
        float x = 0f;
        float y = 0f;
        float z = 0f;

        if (_keys.Contains(VkW)) z += 1f;
        if (_keys.Contains(VkS)) z -= 1f;
        if (_keys.Contains(VkD)) x += 1f;
        if (_keys.Contains(VkA)) x -= 1f;

        if (_keys.Contains(VkSpace)) y += 1f;
        if (_keys.Contains(VkShift)) y -= 1f;

        return new Vector3(x, y, z);
    }

    private void ReportFailure(Exception exception)
    {
        _timer.Stop();

        _renderer?.Dispose();
        _renderer = null;

        RenderFailed?.Invoke(
            "Ошибка DX11 Viewport: " + exception.Message);
    }

    private static void ReadPosition(
        nint lParam,
        out int x,
        out int y)
    {
        long value = lParam.ToInt64();

        x = unchecked((short)(value & 0xFFFF));
        y = unchecked((short)((value >> 16) & 0xFFFF));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint hwnd,
        SubclassProcedure procedure,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint hwnd,
        SubclassProcedure procedure,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam);

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

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);
}
