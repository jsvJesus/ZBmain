using System.Numerics;

namespace Editor.Rendering.Direct3D11;

internal static class VegetationPreviewCamera
{
    private const float FieldOfView = MathF.PI / 3f;
    private const float NearPlane = 0.1f;
    private const float FarPlane = 500f;

    public static Matrix4x4 CreateViewProjection(
        Vector3 center,
        int width,
        int height)
    {
        Vector3 target = center + new Vector3(
            0f,
            0.8f,
            0f);

        Vector3 cameraPosition = center + new Vector3(
            14f,
            8f,
            16f);

        Matrix4x4 view = Matrix4x4.CreateLookAt(
            cameraPosition,
            target,
            Vector3.UnitY);

        float aspectRatio =
            Math.Max(1, width) /
            (float)Math.Max(1, height);

        Matrix4x4 projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                FieldOfView,
                aspectRatio,
                NearPlane,
                FarPlane);

        return view * projection;
    }
}
