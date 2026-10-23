using System.Numerics;
using Core.Levels;

namespace Editor.Rendering.Direct3D11;

internal readonly record struct SurfaceBounds(
    Vector3 Center,
    float Radius,
    bool Valid);

internal static class SurfaceBoundsBuilder
{
    public static SurfaceBounds[] Build(RenderLevel level)
    {
        var result = new SurfaceBounds[
            level.Surfaces.Length
        ];

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = BuildSurface(
                level,
                level.Surfaces[i]);
        }

        return result;
    }

    private static SurfaceBounds BuildSurface(
        RenderLevel level,
        RenderSurface surface)
    {
        if (surface.VertexCount <= 0)
        {
            return default;
        }

        RenderVertexBuffer buffer =
            level.Vertices[surface.VertexBuffer];

        float[] vertices = buffer.Data;

        Vector3 minimum = new(
            float.PositiveInfinity);

        Vector3 maximum = new(
            float.NegativeInfinity);

        int start = surface.VertexOffset;
        int end = start + surface.VertexCount;

        for (int i = start; i < end; i++)
        {
            int offset =
                i * RenderVertexBuffer.FloatsPerVertex;

            var position = new Vector3(
                vertices[offset],
                vertices[offset + 1],
                vertices[offset + 2]);

            if (!IsFinite(position))
            {
                return default;
            }

            minimum = Vector3.Min(
                minimum,
                position);

            maximum = Vector3.Max(
                maximum,
                position);
        }

        Vector3 worldMinimum = new(
            float.PositiveInfinity);

        Vector3 worldMaximum = new(
            float.NegativeInfinity);

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int z = 0; z < 2; z++)
                {
                    var corner = new Vector3(
                        x == 0 ? minimum.X : maximum.X,
                        y == 0 ? minimum.Y : maximum.Y,
                        z == 0 ? minimum.Z : maximum.Z);

                    Vector3 world = Vector3.Transform(
                        corner,
                        surface.Transform);

                    if (!IsFinite(world))
                    {
                        return default;
                    }

                    worldMinimum = Vector3.Min(
                        worldMinimum,
                        world);

                    worldMaximum = Vector3.Max(
                        worldMaximum,
                        world);
                }
            }
        }

        Vector3 center =
            (worldMinimum + worldMaximum) * 0.5f;

        float radius =
            Vector3.Distance(
                center,
                worldMaximum) + 0.1f;

        return new SurfaceBounds(
            center,
            radius,
            true);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z);
    }
}
