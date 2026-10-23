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
    private readonly SurfaceBounds[] _bounds;

    private readonly XRayMaterialReference[] _materials;
    private readonly TerrainAppearance?[] _terrainMaterials;

    private readonly bool[] _detailChecked;
    private readonly bool[] _detailAvailable;

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _objectBuffer;

    public int LastVisibleSurfaces { get; private set; }
    public int LastCulledSurfaces { get; private set; }
    public long LastSubmittedTriangles { get; private set; }

    public LevelGpuScene(
        ID3D11Device device,
        RenderLevel level)
    {
        _surfaces = level.Surfaces;
        _bounds = SurfaceBoundsBuilder.Build(level);

        _materials = level.Materials
            .Select(XRayMaterialReference.Parse)
            .ToArray();

        _terrainMaterials = new TerrainAppearance?[
            _materials.Length
        ];

        _detailChecked = new bool[_materials.Length];
        _detailAvailable = new bool[_materials.Length];

        var catalog = new TerrainMaterialCatalog(
            level.Name);

        for (int i = 0; i < _materials.Length; i++)
        {
            _terrainMaterials[i] = catalog.Resolve(
                _materials[i]);
        }

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
        VegetationTextureCache textures,
        Matrix4x4 viewProjection)
    {
        if (_vertexShader is null ||
            _pixelShader is null ||
            _inputLayout is null ||
            _objectBuffer is null)
        {
            return;
        }

        var frustum = new ViewportFrustum(
            viewProjection);

        LastVisibleSurfaces = 0;
        LastCulledSurfaces = 0;
        LastSubmittedTriangles = 0;

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

        int previousMaterial = int.MinValue;
        int previousVertexBuffer = -1;
        int previousVertexOffset = -1;
        int previousIndexBuffer = -1;

        Matrix4x4 previousModel = default;

        bool constantsInitialized = false;

        Vector4 materialSettings = Vector4.Zero;
        Vector4 materialColor = Vector4.One;

        for (int i = 0; i < _surfaces.Length; i++)
        {
            RenderSurface surface = _surfaces[i];

            if (surface.IndexCount < 3)
            {
                continue;
            }

            SurfaceBounds bounds = _bounds[i];

            if (bounds.Valid &&
                !frustum.IntersectsSphere(
                    bounds.Center,
                    bounds.Radius))
            {
                LastCulledSurfaces++;
                continue;
            }

            bool materialChanged =
                surface.Material != previousMaterial;

            if (materialChanged)
            {
                ResolveMaterial(
                    surface.Material,
                    textures,
                    out string baseTexture,
                    out string detailTexture,
                    out materialSettings,
                    out materialColor);

                context.PSSetShaderResource(
                    0,
                    textures.Get(baseTexture));

                if (!string.IsNullOrEmpty(detailTexture))
                {
                    context.PSSetShaderResource(
                        1,
                        textures.Get(detailTexture));
                }
                else
                {
                    context.PSUnsetShaderResources(1, 1);
                }

                previousMaterial = surface.Material;
            }

            if (materialChanged ||
                !constantsInitialized ||
                previousModel != surface.Transform)
            {
                var constants = new ObjectConstants
                {
                    Model = surface.Transform,
                    MaterialSettings = materialSettings,
                    MaterialColor = materialColor
                };

                context.UpdateSubresource(
                    in constants,
                    _objectBuffer);

                previousModel = surface.Transform;
                constantsInitialized = true;
            }

            if (previousVertexBuffer !=
                    surface.VertexBuffer ||
                previousVertexOffset !=
                    surface.VertexOffset)
            {
                context.IASetVertexBuffer(
                    0,
                    _vertices[surface.VertexBuffer],
                    28,
                    checked(
                        (uint)(surface.VertexOffset * 28L)));

                previousVertexBuffer =
                    surface.VertexBuffer;

                previousVertexOffset =
                    surface.VertexOffset;
            }

            if (previousIndexBuffer != surface.IndexBuffer)
            {
                context.IASetIndexBuffer(
                    _indices[surface.IndexBuffer],
                    Format.R16_UInt,
                    0);

                previousIndexBuffer =
                    surface.IndexBuffer;
            }

            context.DrawIndexed(
                (uint)surface.IndexCount,
                (uint)surface.IndexOffset,
                0);

            LastVisibleSurfaces++;

            LastSubmittedTriangles +=
                surface.IndexCount / 3;
        }

        context.PSUnsetShaderResources(1, 1);
    }

    private void ResolveMaterial(
        int materialIndex,
        VegetationTextureCache textures,
        out string baseTexture,
        out string detailTexture,
        out Vector4 settings,
        out Vector4 color)
    {
        baseTexture = string.Empty;
        detailTexture = string.Empty;

        settings = Vector4.Zero;
        color = Vector4.One;

        if (materialIndex < 0 ||
            materialIndex >= _materials.Length)
        {
            return;
        }

        XRayMaterialReference material =
            _materials[materialIndex];

        baseTexture = material.BaseTexture;

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

        float cutoff = alphaTest ? 0.45f : 0f;

        TerrainAppearance? terrain =
            _terrainMaterials[materialIndex];

        if (terrain is null)
        {
            settings = new Vector4(
                cutoff,
                0f,
                0f,
                0f);

            return;
        }

        if (!_detailChecked[materialIndex])
        {
            _detailAvailable[materialIndex] =
                textures.HasTexture(
                    terrain.DetailTexture);

            _detailChecked[materialIndex] = true;
        }

        bool hasDetail =
            _detailAvailable[materialIndex];

        if (hasDetail)
        {
            detailTexture = terrain.DetailTexture;
        }

        settings = new Vector4(
            cutoff,
            1f,
            terrain.TextureScale,
            hasDetail ? 1f : 0f);

        color = new Vector4(
            terrain.Tint,
            1f);
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
            _vertices.Add(
                device.CreateBuffer(
                    buffer.Data,
                    BindFlags.VertexBuffer));
        }

        foreach (RenderIndexBuffer buffer in level.Indices)
        {
            _indices.Add(
                device.CreateBuffer(
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
        public Vector4 MaterialColor;
    }
}
