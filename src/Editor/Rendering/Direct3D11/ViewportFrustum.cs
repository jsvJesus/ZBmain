using System.Numerics;

namespace Editor.Rendering.Direct3D11;

internal sealed class ViewportFrustum
{
    private readonly Vector4[] _planes;

    public ViewportFrustum(Matrix4x4 matrix)
    {
        _planes =
        [
            Normalize(new Vector4(
                matrix.M14 + matrix.M11,
                matrix.M24 + matrix.M21,
                matrix.M34 + matrix.M31,
                matrix.M44 + matrix.M41)),

            Normalize(new Vector4(
                matrix.M14 - matrix.M11,
                matrix.M24 - matrix.M21,
                matrix.M34 - matrix.M31,
                matrix.M44 - matrix.M41)),

            Normalize(new Vector4(
                matrix.M14 + matrix.M12,
                matrix.M24 + matrix.M22,
                matrix.M34 + matrix.M32,
                matrix.M44 + matrix.M42)),

            Normalize(new Vector4(
                matrix.M14 - matrix.M12,
                matrix.M24 - matrix.M22,
                matrix.M34 - matrix.M32,
                matrix.M44 - matrix.M42)),

            Normalize(new Vector4(
                matrix.M13,
                matrix.M23,
                matrix.M33,
                matrix.M43)),

            Normalize(new Vector4(
                matrix.M14 - matrix.M13,
                matrix.M24 - matrix.M23,
                matrix.M34 - matrix.M33,
                matrix.M44 - matrix.M43))
        ];
    }

    public bool IntersectsSphere(
        Vector3 center,
        float radius)
    {
        foreach (Vector4 plane in _planes)
        {
            float distance =
                plane.X * center.X +
                plane.Y * center.Y +
                plane.Z * center.Z +
                plane.W;

            if (distance < -radius)
            {
                return false;
            }
        }

        return true;
    }

    private static Vector4 Normalize(Vector4 plane)
    {
        float length = MathF.Sqrt(
            plane.X * plane.X +
            plane.Y * plane.Y +
            plane.Z * plane.Z);

        if (length < 0.000001f)
        {
            return plane;
        }

        return plane / length;
    }
}
