using System.Numerics;
using System.Runtime.InteropServices;
using Core.Levels;
using Core.Storage;
using Editor.Rendering;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Editor.Rendering.Direct3D11;

internal sealed class LevelGpuScene : IDisposable
{
    private readonly List<ID3D11Buffer> _vertices = new();
    private readonly List<ID3D11Buffer> _indices = new();

    private readonly RenderSurface[] _surfaces;
    private readonly XRayMaterialReference[] _materials;

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _objectBuffer;

    public LevelGpuScene(
        ID3D11Device device,
        RenderLevel level)
    {
        _surfaces = level.Surfaces;

        _materials = level.Materials
            .Select(XRayMaterialReference.Parse)
            .ToArray();

        try
        {
            CreateShaders(device);
            CreateBuffers(device, level);

            _objectBuffer = device.CreateBuffer(
                (uint)Marshal.SizeOf<ObjectConstants>(),
                BindFlags.ConstantBuffer);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Draw(
        ID3D11DeviceContext context,
        VegetationTextureCache textures)
    {
        if (_vertexShader is null ||
            _pixelShader is null ||
            _inputLayout is null ||
            _objectBuffer is null)
        {
            return;
        }

        context.VSSetShader(_vertexShader);
        context.PSSetShader(_pixelShader);
        context.IASetInputLayout(_inputLayout);

        context.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        context.VSSetConstantBuffer(
            1,
            _objectBuffer);

        context.PSSetConstantBuffer(
            1,
            _objectBuffer);

        foreach (RenderSurface surface in _surfaces)
        {
            if (surface.IndexCount < 3)
            {
                continue;
            }

            string textureName = string.Empty;
            float alphaCutoff = 0f;

            if (surface.Material >= 0 &&
                surface.Material < _materials.Length)
            {
                XRayMaterialReference material =
                    _materials[surface.Material];

                textureName = material.BaseTexture;

                bool alphaTest =
                    material.Shader.Contains(
                        "aref",
                        StringComparison.OrdinalIgnoreCase) ||
                    material.Shader.StartsWith(
                        "flora\\leaf",
                        StringComparison.OrdinalIgnoreCase) ||
                    material.Shader.StartsWith(
                        "details\\lod",
                        StringComparison.OrdinalIgnoreCase);

                alphaCutoff = alphaTest ? 0.45f : 0f;
            }

            var constants = new ObjectConstants
            {
                Model = surface.Transform,
                MaterialSettings = new Vector4(
                    alphaCutoff,
                    0f,
                    0f,
                    0f)
            };

            context.UpdateSubresource(
                in constants,
                _objectBuffer);

            context.PSSetShaderResource(
                0,
                textures.Get(textureName));

            context.IASetVertexBuffer(
                0,
                _vertices[surface.VertexBuffer],
                28,
                checked((uint)(surface.VertexOffset * 28L)));

            context.IASetIndexBuffer(
                _indices[surface.IndexBuffer],
                Format.R16_UInt,
                0);

            context.DrawIndexed(
                (uint)surface.IndexCount,
                (uint)surface.IndexOffset,
                0);
        }
    }

    private void CreateShaders(ID3D11Device device)
    {
        string path = Path.Combine(
            ProjectPaths.PacksDirectory,
            "shaders",
            "Level.hlsl");

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

        _vertexShader = device.CreateVertexShader(
            vertexCode.Span);

        _pixelShader = device.CreatePixelShader(
            pixelCode.Span);

        InputElementDescription[] layout =
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
                0)
        ];

        _inputLayout = device.CreateInputLayout(
            layout,
            vertexCode.Span);
    }

    private void CreateBuffers(
        ID3D11Device device,
        RenderLevel level)
    {
        foreach (RenderVertexBuffer buffer in level.Vertices)
        {
            _vertices.Add(device.CreateBuffer(
                buffer.Data,
                BindFlags.VertexBuffer));
        }

        foreach (RenderIndexBuffer buffer in level.Indices)
        {
            _indices.Add(device.CreateBuffer(
                buffer.Data,
                BindFlags.IndexBuffer));
        }
    }

    public void Dispose()
    {
        foreach (ID3D11Buffer buffer in _vertices)
        {
            buffer.Dispose();
        }

        foreach (ID3D11Buffer buffer in _indices)
        {
            buffer.Dispose();
        }

        _vertices.Clear();
        _indices.Clear();

        _objectBuffer?.Dispose();
        _objectBuffer = null;

        _inputLayout?.Dispose();
        _inputLayout = null;

        _vertexShader?.Dispose();
        _vertexShader = null;

        _pixelShader?.Dispose();
        _pixelShader = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectConstants
    {
        public Matrix4x4 Model;
        public Vector4 MaterialSettings;
    }
}
