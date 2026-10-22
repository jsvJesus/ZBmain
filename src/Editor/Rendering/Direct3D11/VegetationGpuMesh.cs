using System.Numerics;
using System.Runtime.InteropServices;
using Core.Levels;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Editor.Rendering.Direct3D11;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct VegetationGpuVertex(
    Vector3 Position,
    Vector2 Uv);

internal sealed class VegetationGpuMesh : IDisposable
{
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _indexBuffer;
    private ID3D11Buffer? _instanceBuffer;

    public int IndexCount { get; }
    public int InstanceCount { get; }

    public VegetationGpuMesh(
        ID3D11Device device,
        DetailModel model,
        VegetationInstance[] instances)
    {
        if (model.Vertices.Length == 0 ||
            model.Indices.Length < 3 ||
            instances.Length == 0)
        {
            throw new ArgumentException(
                "Нельзя создать пустой GPU-меш растительности.");
        }

        IndexCount = model.Indices.Length;
        InstanceCount = instances.Length;

        var vertices = new VegetationGpuVertex[
            model.Vertices.Length
        ];

        for (int i = 0; i < vertices.Length; i++)
        {
            DetailVertex vertex = model.Vertices[i];

            vertices[i] = new VegetationGpuVertex(
                vertex.Position,
                vertex.Uv);
        }

        try
        {
            _vertexBuffer = device.CreateBuffer(
                vertices,
                BindFlags.VertexBuffer);

            _indexBuffer = device.CreateBuffer(
                model.Indices,
                BindFlags.IndexBuffer);

            _instanceBuffer = device.CreateBuffer(
                instances,
                BindFlags.VertexBuffer);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Draw(ID3D11DeviceContext context)
    {
        if (_vertexBuffer is null ||
            _indexBuffer is null ||
            _instanceBuffer is null)
        {
            return;
        }

        context.IASetVertexBuffer(
            0,
            _vertexBuffer,
            20);

        context.IASetVertexBuffer(
            1,
            _instanceBuffer,
            32);

        context.IASetIndexBuffer(
            _indexBuffer,
            Format.R16_UInt,
            0);

        context.DrawIndexedInstanced(
            (uint)IndexCount,
            (uint)InstanceCount,
            0,
            0,
            0);
    }

    public void Dispose()
    {
        _instanceBuffer?.Dispose();
        _instanceBuffer = null;

        _indexBuffer?.Dispose();
        _indexBuffer = null;

        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
    }
}
