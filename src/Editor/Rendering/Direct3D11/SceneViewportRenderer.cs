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

internal sealed class SceneViewportRenderer : IDisposable
{
    private readonly Stopwatch _clock = new();
    private readonly ViewportCamera _camera = new();

    private IDXGIFactory2? _factory;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;

    private ID3D11RenderTargetView? _renderTarget;
    private ID3D11Texture2D? _depthTexture;
    private ID3D11DepthStencilView? _depthView;

    private ID3D11DepthStencilState? _depthState;
    private ID3D11BlendState? _opaqueBlend;

    private ID3D11VertexShader? _vegetationVS;
    private ID3D11PixelShader? _vegetationPS;
    private ID3D11InputLayout? _vegetationLayout;

    private ID3D11RasterizerState? _rasterizer;
    private ID3D11SamplerState? _sampler;
    private ID3D11Buffer? _frameBuffer;

    private LevelGpuScene? _level;
    private VegetationTextureCache? _textures;
    private VegetationManager? _vegetation;

    private int _width;
    private int _height;

    public void Initialize(nint windowHandle)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Direct3D 11 требует Windows.");
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
                    "Требуется Direct3D 11.");
            }

            GetSize(
                windowHandle,
                out _width,
                out _height);

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

            _swapChain = _factory.CreateSwapChainForHwnd(
                _device,
                windowHandle,
                swapDescription,
                new SwapChainFullscreenDescription
                {
                    Windowed = true
                });

            _factory.MakeWindowAssociation(
                windowHandle,
                WindowAssociationFlags.IgnoreAltEnter);

            CreateRenderTargets();
            CreateVegetationShaders();

            _frameBuffer = _device.CreateBuffer(
                (uint)Marshal.SizeOf<FrameConstants>(),
                BindFlags.ConstantBuffer);

            _rasterizer = _device.CreateRasterizerState(
                RasterizerDescription.CullNone);

            _sampler = _device.CreateSamplerState(
                SamplerDescription.LinearWrap);

            _depthState = _device.CreateDepthStencilState(
                new DepthStencilDescription(
                    true,
                    DepthWriteMask.All,
                    ComparisonFunction.LessEqual));

            _opaqueBlend = _device.CreateBlendState(
                BlendDescription.Opaque);

            _camera.Reset(null, null);

            _clock.Restart();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetScene(
        RenderLevel? scene,
        DetailLevel? details)
    {
        if (_device is null ||
            _context is null)
        {
            return;
        }

        _context.PSUnsetShaderResources(0, 1);
        _context.Flush();

        ReleaseScene();

        if (scene is null)
        {
            _camera.Reset(null, null);
            return;
        }

        try
        {
            _textures = new VegetationTextureCache(
                _device,
                _context,
                scene.Name);

            _level = new LevelGpuScene(
                _device,
                scene);

            if (details is not null)
            {
                TerrainHeightSampler? terrain = null;

                try
                {
                    terrain = TerrainHeightSampler.Load(
                        scene.Name,
                        details);
                }
                catch (Exception exception) when (
                    exception is IOException or
                        InvalidDataException or
                        UnauthorizedAccessException)
                {
                    Trace.WriteLine(
                        "Не удалось загрузить collision.zmesh: " +
                        exception.Message);
                }

                _vegetation = new VegetationManager(
                    _device,
                    details,
                    terrain);
            }

            _camera.Reset(scene, null);
        }
        catch
        {
            ReleaseScene();
            throw;
        }
    }

    public void Look(float deltaX, float deltaY)
    {
        _camera.Look(deltaX, deltaY);
    }

    public void Fly(
        Vector3 direction,
        float deltaTime)
    {
        _camera.Fly(direction, deltaTime);
    }

    public void AdjustSpeed(float steps)
    {
        _camera.AdjustSpeed(steps);
    }

    public void ShowMapTopDown()
    {
        _camera.ShowMapTopDown();
    }

    public void Render(nint windowHandle)
    {
        if (_context is null ||
            _swapChain is null)
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

        if (_renderTarget is null ||
            _depthView is null)
        {
            return;
        }

        _context.OMSetRenderTargets(
            _renderTarget,
            _depthView);

        _context.OMSetDepthStencilState(
            _depthState,
            0);

        _context.OMSetBlendState(
            _opaqueBlend);

        _context.RSSetViewport(
            0,
            0,
            _width,
            _height);

        _context.ClearRenderTargetView(
            _renderTarget,
            new Color4(
                0.035f,
                0.055f,
                0.085f,
                1f));

        _context.ClearDepthStencilView(
            _depthView,
            DepthStencilClearFlags.Depth,
            1f,
            0);

        if (_frameBuffer is not null &&
            _textures is not null)
        {
            Matrix4x4 viewProjection =
                _camera.CreateViewProjection(
                    _width,
                    _height);

            var frame = new FrameConstants
            {
                ViewProjection = viewProjection,
                Frame = new Vector4(
                    (float)_clock.Elapsed.TotalSeconds,
                    0f,
                    0f,
                    0f)
            };

            _context.UpdateSubresource(
                in frame,
                _frameBuffer);

            _context.VSSetConstantBuffer(
                0,
                _frameBuffer);

            _context.RSSetState(
                _rasterizer);

            _context.PSSetSampler(
                0,
                _sampler);

            _level?.Draw(
                _context,
                _textures);

            if (_vegetation is not null)
            {
                _vegetation.Update(
                    _camera.Position);

                DrawVegetation(viewProjection);
            }
        }

        _swapChain.Present(
            1,
            PresentFlags.None).CheckError();
    }

    private void DrawVegetation(
        Matrix4x4 viewProjection)
    {
        if (_context is null ||
            _textures is null ||
            _vegetation is null ||
            _vegetationLayout is null ||
            _vegetationVS is null ||
            _vegetationPS is null)
        {
            return;
        }

        _context.IASetInputLayout(
            _vegetationLayout);

        _context.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _context.VSSetShader(
            _vegetationVS);

        _context.PSSetShader(
            _vegetationPS);

        _vegetation.Draw(
            _context,
            _textures,
            viewProjection);
    }

    private void CreateVegetationShaders()
    {
        string path = Path.Combine(
            ProjectPaths.PacksDirectory,
            "shaders",
            "Vegetation.hlsl");

        ReadOnlyMemory<byte> vertexCode =
            Compiler.CompileFromFile(
                path,
                "VSMain",
                "vs_5_0",
                ShaderFlags.EnableStrictness);

        ReadOnlyMemory<byte> pixelCode =
            Compiler.CompileFromFile(
                path,
                "PSMain",
                "ps_5_0",
                ShaderFlags.EnableStrictness);

        _vegetationVS = _device!.CreateVertexShader(
            vertexCode.Span);

        _vegetationPS = _device.CreatePixelShader(
            pixelCode.Span);

        InputElementDescription[] elements =
        [
            new InputElementDescription(
                "POSITION",
                0,
                Format.R32G32B32_Float,
                0,
                0,
                InputClassification.PerVertexData,
                0),

            new InputElementDescription(
                "TEXCOORD",
                0,
                Format.R32G32_Float,
                12,
                0,
                InputClassification.PerVertexData,
                0),

            new InputElementDescription(
                "INSTANCE",
                0,
                Format.R32G32B32A32_Float,
                0,
                1,
                InputClassification.PerInstanceData,
                1),

            new InputElementDescription(
                "TINT",
                0,
                Format.R32G32B32A32_Float,
                16,
                1,
                InputClassification.PerInstanceData,
                1)
        ];

        _vegetationLayout = _device.CreateInputLayout(
            elements,
            vertexCode.Span);
    }

    private void CreateRenderTargets()
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

        _depthTexture = _device.CreateTexture2D(
            Format.D24_UNorm_S8_UInt,
            (uint)_width,
            (uint)_height,
            1,
            1,
            null,
            BindFlags.DepthStencil);

        _depthView = _device.CreateDepthStencilView(
            _depthTexture,
            new DepthStencilViewDescription(
                _depthTexture,
                DepthStencilViewDimension.Texture2D));
    }

    private void Resize(int width, int height)
    {
        if (_context is null ||
            _swapChain is null)
        {
            return;
        }

        _context.UnsetRenderTargets();

        ReleaseRenderTargets();

        _context.Flush();

        _swapChain.ResizeBuffers(
            2,
            (uint)width,
            (uint)height,
            Format.B8G8R8A8_UNorm,
            SwapChainFlags.None).CheckError();

        _width = width;
        _height = height;

        CreateRenderTargets();
    }

    private void ReleaseScene()
    {
        _vegetation?.Dispose();
        _vegetation = null;

        _level?.Dispose();
        _level = null;

        _textures?.Dispose();
        _textures = null;
    }

    private void ReleaseRenderTargets()
    {
        _depthView?.Dispose();
        _depthView = null;

        _depthTexture?.Dispose();
        _depthTexture = null;

        _renderTarget?.Dispose();
        _renderTarget = null;
    }

    private static void GetSize(
        nint windowHandle,
        out int width,
        out int height)
    {
        if (!GetClientRect(
                windowHandle,
                out Win32Rect bounds))
        {
            throw new InvalidOperationException(
                "Не удалось получить размеры DX11 Viewport.");
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
        _context?.PSUnsetShaderResources(0, 1);
        _context?.Flush();

        ReleaseScene();
        ReleaseRenderTargets();

        _frameBuffer?.Dispose();
        _frameBuffer = null;

        _sampler?.Dispose();
        _sampler = null;

        _rasterizer?.Dispose();
        _rasterizer = null;

        _depthState?.Dispose();
        _depthState = null;

        _opaqueBlend?.Dispose();
        _opaqueBlend = null;

        _vegetationLayout?.Dispose();
        _vegetationLayout = null;

        _vegetationVS?.Dispose();
        _vegetationVS = null;

        _vegetationPS?.Dispose();
        _vegetationPS = null;

        _swapChain?.Dispose();
        _swapChain = null;

        _context?.Dispose();
        _context = null;

        _device?.Dispose();
        _device = null;

        _factory?.Dispose();
        _factory = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 ViewProjection;
        public Vector4 Frame;
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
