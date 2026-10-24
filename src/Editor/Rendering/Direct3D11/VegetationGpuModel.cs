using Core.Levels;
using Vortice.Direct3D11;

namespace Editor.Rendering.Direct3D11;

internal sealed class VegetationGpuModel : IDisposable
{
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _indexBuffer;

    public string TextureName { get; }
    public int IndexCount { get; }

    public ID3D11Buffer VertexBuffer =>
        _vertexBuffer ?? throw new ObjectDisposedException(
            nameof(VegetationGpuModel));

    public ID3D11Buffer IndexBuffer =>
        _indexBuffer ?? throw new ObjectDisposedException(
            nameof(VegetationGpuModel));

    public VegetationGpuModel(
        ID3D11Device device,
        DetailModel model)
    {
        if (model.Vertices.Length == 0 ||
            model.Indices.Length < 3)
        {
            throw new ArgumentException(
                "Пустая detail-модель.");
        }

        TextureName = model.Texture;
        IndexCount = model.Indices.Length;

        var vertices = new VegetationGpuVertex[
            model.Vertices.Length
        ];

        for (int i = 0; i < vertices.Length; i++)
        {
            DetailVertex source = model.Vertices[i];

            vertices[i] = new VegetationGpuVertex(
                source.Position,
                source.Uv);
        }

        try
        {
            _vertexBuffer = device.CreateBuffer(
                vertices,
                BindFlags.VertexBuffer);

            _indexBuffer = device.CreateBuffer(
                model.Indices,
                BindFlags.IndexBuffer);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _indexBuffer?.Dispose();
        _indexBuffer = null;

        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
    }
}
