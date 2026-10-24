using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Editor.Rendering.Direct3D11;

internal sealed class VegetationGpuBatch : IDisposable
{
    private readonly VegetationGpuModel _model;
    private ID3D11Buffer? _instances;

    public string TextureName => _model.TextureName;
    public int InstanceCount { get; }

    public VegetationGpuBatch(
        ID3D11Device device,
        VegetationGpuModel model,
        VegetationInstance[] instances)
    {
        if (instances.Length == 0)
        {
            throw new ArgumentException(
                "Буфер экземпляров не может быть пустым.");
        }

        _model = model;
        InstanceCount = instances.Length;

        _instances = device.CreateBuffer(
            instances,
            BindFlags.VertexBuffer);
    }

    public void Draw(ID3D11DeviceContext context)
    {
        if (_instances is null)
        {
            return;
        }

        context.IASetVertexBuffer(
            0,
            _model.VertexBuffer,
            20);

        context.IASetVertexBuffer(
            1,
            _instances,
            32);

        context.IASetIndexBuffer(
            _model.IndexBuffer,
            Format.R16_UInt,
            0);

        context.DrawIndexedInstanced(
            (uint)_model.IndexCount,
            (uint)InstanceCount,
            0,
            0,
            0);
    }

    public void Dispose()
    {
        _instances?.Dispose();
        _instances = null;
    }
}
