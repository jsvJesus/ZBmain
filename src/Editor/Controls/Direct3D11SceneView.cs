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
    private const string WindowClassName =
        "Direct3D11ViewportHost";

    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsClipChildren = 0x02000000;

    private const uint WmEraseBackground = 0x0014;
    private const uint WmKillFocus = 0x0008;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmRightButtonDown = 0x0204;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmCaptureChanged = 0x0215;
    private const uint WmKeyDown = 0x0100;

    private const int VkW = 0x57;
    private const int VkA = 0x41;
    private const int VkS = 0x53;
    private const int VkD = 0x44;
    private const int VkSpace = 0x20;
    private const int VkShift = 0x10;
    private const int VkHome = 0x24;

    private const int ErrorClassAlreadyExists = 1410;

    private static readonly WindowProcedureCallback
        SharedProcedure = DispatchWindowMessage;

    private static readonly Dictionary<
        nint,
        Direct3D11SceneView> Windows = new();

    private static bool _windowClassRegistered;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private SceneViewportRenderer? _renderer;

    private RenderLevel? _scene;
    private DetailLevel? _vegetation;

    private nint _handle;

    private bool _rightButtonHeld;
    private bool _renderingEnabled = true;

    private int _lastX;
    private int _lastY;
    private double _lastFrameTime;

    public event Action<string>? RenderFailed;

    public Direct3D11SceneView()
    {
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
            _renderer.SetScene(
                scene,
                vegetation);
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

            if (_rightButtonHeld)
            {
                _rightButtonHeld = false;
                ReleaseCapture();
            }

            return;
        }

        if (_renderer is not null &&
            _handle != IntPtr.Zero)
        {
            _lastFrameTime =
                _clock.Elapsed.TotalSeconds;

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

        RegisterWindowClass();

        nint instance = GetModuleHandleW(null);

        nint handle = CreateWindowExW(
            0,
            WindowClassName,
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
            instance,
            IntPtr.Zero);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
        }

        _handle = handle;

        Windows.Add(handle, this);

        try
        {
            _renderer = new SceneViewportRenderer();

            _renderer.Initialize(handle);

            _renderer.SetScene(
                _scene,
                _vegetation);

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

        _rightButtonHeld = false;

        if (GetCapture() == control.Handle)
        {
            ReleaseCapture();
        }

        _renderer?.Dispose();
        _renderer = null;

        Windows.Remove(control.Handle);

        _handle = IntPtr.Zero;

        if (control.Handle != IntPtr.Zero)
        {
            DestroyWindow(control.Handle);
        }
    }

    private static void RegisterWindowClass()
    {
        if (_windowClassRegistered)
        {
            return;
        }

        var definition = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Instance = GetModuleHandleW(null),
            Procedure = Marshal.GetFunctionPointerForDelegate(
                SharedProcedure),
            ClassName = WindowClassName
        };

        ushort classAtom = RegisterClassExW(
            in definition);

        if (classAtom == 0)
        {
            int error = Marshal.GetLastWin32Error();

            if (error != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error);
            }
        }

        _windowClassRegistered = true;
    }

    private static nint DispatchWindowMessage(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (Windows.TryGetValue(
                hwnd,
                out Direct3D11SceneView? control))
        {
            return control.ProcessWindowMessage(
                hwnd,
                message,
                wParam,
                lParam);
        }

        return DefWindowProcW(
            hwnd,
            message,
            wParam,
            lParam);
    }

    private nint ProcessWindowMessage(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam)
    {
        switch (message)
        {
            case WmEraseBackground:
                return (nint)1;

            case WmLeftButtonDown:
                SetFocus(hwnd);
                return IntPtr.Zero;

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

                if (GetCapture() == hwnd)
                {
                    ReleaseCapture();
                }

                return IntPtr.Zero;

            case WmMouseMove:
                if (_rightButtonHeld &&
                    _renderingEnabled)
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

                return IntPtr.Zero;

            case WmMouseWheel:
                if (_renderingEnabled)
                {
                    short wheel = unchecked((short)(
                        ((ulong)wParam >> 16) & 0xFFFF));

                    _renderer?.AdjustSpeed(
                        wheel / 120f);
                }

                return IntPtr.Zero;

            case WmKeyDown:
                if ((int)wParam == VkHome)
                {
                    _renderer?.ShowMapTopDown();
                    return IntPtr.Zero;
                }

                break;

            case WmKillFocus:
            case WmCaptureChanged:
                _rightButtonHeld = false;
                break;
        }

        return DefWindowProcW(
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

        try
        {
            if (GetFocus() == _handle)
            {
                Vector3 movement = GetMovement();

                if (movement.LengthSquared() > 0f)
                {
                    _renderer.Fly(
                        movement,
                        deltaTime);
                }
            }

            _renderer.Render(_handle);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private static Vector3 GetMovement()
    {
        float x = 0f;
        float y = 0f;
        float z = 0f;

        if (IsPressed(VkW))
        {
            z += 1f;
        }

        if (IsPressed(VkS))
        {
            z -= 1f;
        }

        if (IsPressed(VkD))
        {
            x += 1f;
        }

        if (IsPressed(VkA))
        {
            x -= 1f;
        }

        if (IsPressed(VkSpace))
        {
            y += 1f;
        }

        if (IsPressed(VkShift))
        {
            y -= 1f;
        }

        return new Vector3(x, y, z);
    }

    private static bool IsPressed(int key)
    {
        return (GetAsyncKeyState(key) & 0x8000) != 0;
    }

    private void ReportFailure(Exception exception)
    {
        _timer.Stop();

        _renderer?.Dispose();
        _renderer = null;

        RenderFailed?.Invoke(
            "Ошибка DX11 Viewport: " +
            exception.Message);
    }

    private static void ReadPosition(
        nint lParam,
        out int x,
        out int y)
    {
        long value = lParam.ToInt64();

        x = unchecked((short)(value & 0xFFFF));

        y = unchecked((short)(
            (value >> 16) & 0xFFFF));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedureCallback(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam);

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? MenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string ClassName;

        public nint SmallIcon;
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "RegisterClassExW",
        SetLastError = true)]
    private static extern ushort RegisterClassExW(
        in WindowClass definition);

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

    [DllImport(
        "user32.dll",
        EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProcW(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleW",
        CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(
        string? moduleName);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);
}
