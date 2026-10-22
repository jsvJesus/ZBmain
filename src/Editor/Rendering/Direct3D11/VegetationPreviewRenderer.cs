using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Core.Levels;
using Core.Storage;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace Editor.Rendering.Direct3D11;

internal sealed class VegetationPreviewRenderer : IDisposable
{
    private readonly Stopwatch _clock = new();

    private IDXGIFactory2? _factory;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;

    private ID3D11RenderTargetView? _renderTarget;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;

    private ID3D11InputLayout? _inputLayout;
    private ID3D11RasterizerState? _rasterizer;
    private ID3D11Buffer? _instanceBuffer;
    private ID3D11Buffer? _frameBuffer;

    private int _width;
    private int _height;
    private int _instanceCount;

    public int InstanceCount => _instanceCount;

    public void Initialize(
        nint windowHandle,
        DetailLevel details)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Direct3D 11 доступен только на Windows.");
        }

        try
        {
            _factory = CreateDXGIFactory1<IDXGIFactory2>();

            FeatureLevel[] levels =
            [
                FeatureLevel.Level_11_0
            ];

            D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                levels,
                out ID3D11Device device,
                out FeatureLevel featureLevel,
                out ID3D11DeviceContext context).CheckError();

            _device = device;
            _context = context;

            if (featureLevel < FeatureLevel.Level_11_0)
            {
                throw new NotSupportedException(
                    "Видеокарта не поддерживает Direct3D 11.");
            }

            GetSize(windowHandle, out _width, out _height);

            var swapDescription = new SwapChainDescription1
            {
                Width = (uint)_width,
                Height = (uint)_height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = SampleDescription.Default,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
                Flags = SwapChainFlags.None
            };

            var fullscreenDescription =
                new SwapChainFullscreenDescription
                {
                    Windowed = true
                };

            _swapChain = _factory.CreateSwapChainForHwnd(
                _device,
                windowHandle,
                swapDescription,
                fullscreenDescription);

            _factory.MakeWindowAssociation(
                windowHandle,
                WindowAssociationFlags.IgnoreAltEnter);

            CreateRenderTarget();
            CreateShaders();
            CreateInstances(details);

            _frameBuffer = _device.CreateBuffer(
                16,
                BindFlags.ConstantBuffer);

            _rasterizer = _device.CreateRasterizerState(
                RasterizerDescription.CullNone);

            _clock.Restart();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Render(nint windowHandle)
    {
        if (_device is null ||
            _context is null ||
            _swapChain is null ||
            _renderTarget is null)
        {
            return;
        }

        GetSize(
            windowHandle,
            out int width,
            out int height);

        if (width != _width ||
            height != _height)
        {
            Resize(width, height);
        }

        if (_renderTarget is null)
        {
            return;
        }

        _context.OMSetRenderTargets(_renderTarget);

        _context.RSSetViewport(
            0,
            0,
            _width,
            _height);

        _context.ClearRenderTargetView(
            _renderTarget,
            new Color4(
                0.065f,
                0.105f,
                0.115f,
                1f));

        if (_instanceCount > 0 &&
            _instanceBuffer is not null &&
            _frameBuffer is not null &&
            _vertexShader is not null &&
            _pixelShader is not null &&
            _inputLayout is not null &&
            _rasterizer is not null)
        {
            Vector4 frame = new(
                (float)_clock.Elapsed.TotalSeconds,
                0f,
                0f,
                0f);

            _context.UpdateSubresource(
                in frame,
                _frameBuffer);

            _context.IASetInputLayout(_inputLayout);

            _context.IASetPrimitiveTopology(
                PrimitiveTopology.TriangleList);

            _context.IASetVertexBuffer(
                0,
                _instanceBuffer,
                32);

            _context.RSSetState(_rasterizer);

            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);

            _context.VSSetConstantBuffer(
                0,
                _frameBuffer);

            _context.DrawInstanced(
                12,
                (uint)_instanceCount,
                0,
                0);
        }

        _swapChain.Present(
            1,
            PresentFlags.None).CheckError();
    }

    private void CreateShaders()
    {
        string shaderPath = Path.Combine(
            ProjectPaths.PacksDirectory,
            "shaders",
            "Vegetation.hlsl");

        if (!File.Exists(shaderPath))
        {
            throw new FileNotFoundException(
                "Не найден HLSL-шейдер растительности.",
                shaderPath);
        }

        ReadOnlyMemory<byte> vertexBytecode =
            Compiler.CompileFromFile(
                shaderPath,
                "VSMain",
                "vs_5_0",
                ShaderFlags.EnableStrictness);

        ReadOnlyMemory<byte> pixelBytecode =
            Compiler.CompileFromFile(
                shaderPath,
                "PSMain",
                "ps_5_0",
                ShaderFlags.EnableStrictness);

        _vertexShader = _device!.CreateVertexShader(
            vertexBytecode.Span);

        _pixelShader = _device.CreatePixelShader(
            pixelBytecode.Span);

        InputElementDescription[] elements =
        [
            new InputElementDescription(
                "INSTANCE",
                0,
                Format.R32G32B32A32_Float,
                0,
                0,
                InputClassification.PerInstanceData,
                1),

            new InputElementDescription(
                "TINT",
                0,
                Format.R32G32B32A32_Float,
                16,
                0,
                InputClassification.PerInstanceData,
                1)
        ];

        _inputLayout = _device.CreateInputLayout(
            elements,
            vertexBytecode.Span);
    }

    private void CreateInstances(DetailLevel details)
    {
        VegetationInstance[] instances =
            VegetationInstanceBuilder.Build(details);

        _instanceCount = instances.Length;

        if (_instanceCount == 0)
        {
            return;
        }

        _instanceBuffer = _device!.CreateBuffer(
            instances,
            BindFlags.VertexBuffer);
    }

    private void CreateRenderTarget()
    {
        if (_device is null ||
            _swapChain is null)
        {
            return;
        }

        using ID3D11Texture2D backBuffer =
            _swapChain.GetBuffer<ID3D11Texture2D>(0);

        _renderTarget = _device.CreateRenderTargetView(
            backBuffer);
    }

    private void Resize(int width, int height)
    {
        if (_context is null ||
            _swapChain is null)
        {
            return;
        }

        _context.UnsetRenderTargets();

        _renderTarget?.Dispose();
        _renderTarget = null;

        _context.Flush();

        _swapChain.ResizeBuffers(
            2,
            (uint)width,
            (uint)height,
            Format.B8G8R8A8_UNorm,
            SwapChainFlags.None).CheckError();

        _width = width;
        _height = height;

        CreateRenderTarget();
    }

    private static void GetSize(
        nint windowHandle,
        out int width,
        out int height)
    {
        if (!GetClientRect(windowHandle, out Win32Rect bounds))
        {
            throw new InvalidOperationException(
                "Не удалось получить размеры окна Direct3D.");
        }

        width = Math.Max(
            1,
            bounds.Right - bounds.Left);

        height = Math.Max(
            1,
            bounds.Bottom - bounds.Top);
    }

    public void Dispose()
    {
        _clock.Stop();

        _context?.UnsetRenderTargets();
        _context?.Flush();

        _renderTarget?.Dispose();
        _renderTarget = null;

        _instanceBuffer?.Dispose();
        _instanceBuffer = null;

        _frameBuffer?.Dispose();
        _frameBuffer = null;

        _inputLayout?.Dispose();
        _inputLayout = null;

        _rasterizer?.Dispose();
        _rasterizer = null;

        _vertexShader?.Dispose();
        _vertexShader = null;

        _pixelShader?.Dispose();
        _pixelShader = null;

        _swapChain?.Dispose();
        _swapChain = null;

        _context?.Dispose();
        _context = null;

        _device?.Dispose();
        _device = null;

        _factory?.Dispose();
        _factory = null;

        _instanceCount = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(
        nint hWnd,
        out Win32Rect lpRect);
}
