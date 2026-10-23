using System.Numerics;
using Core.Levels;

namespace Editor.Rendering.Direct3D11;

internal sealed class ViewportCamera
{
    private const float MouseSensitivity = 0.003f;

    private Vector3 _position;
    private Vector3 _target;

    private float _yaw;
    private float _pitch;
    private float _speed = 20f;
    private float _radius = 100f;

    public void Reset(
        RenderLevel? level,
        Vector3? vegetationCenter)
    {
        _target = vegetationCenter ??
            (level is null
                ? Vector3.Zero
                : (level.Min + level.Max) * 0.5f);

        _radius = level is null
            ? 100f
            : MathF.Max(
                25f,
                (level.Max - level.Min).Length() * 0.5f);

        float distance = vegetationCenter.HasValue
            ? 30f
            : Math.Clamp(_radius, 50f, 400f);

        _position = _target + new Vector3(
            distance * 0.75f,
            distance * 0.50f,
            distance * 0.75f);

        LookAt(_target);

        _speed = vegetationCenter.HasValue
            ? 15f
            : Math.Clamp(_radius * 0.08f, 10f, 120f);
    }

    public void Look(float deltaX, float deltaY)
    {
        _yaw += deltaX * MouseSensitivity;

        _pitch = Math.Clamp(
            _pitch - deltaY * MouseSensitivity,
            -1.5533f,
            1.5533f);

        if (_yaw > MathF.PI)
        {
            _yaw -= MathF.Tau;
        }
        else if (_yaw < -MathF.PI)
        {
            _yaw += MathF.Tau;
        }
    }

    public void Fly(Vector3 input, float deltaTime)
    {
        if (deltaTime <= 0f ||
            input.LengthSquared() <= 0f)
        {
            return;
        }

        if (input.LengthSquared() > 1f)
        {
            input = Vector3.Normalize(input);
        }

        Vector3 forward = GetForward();

        Vector3 right = Vector3.Normalize(
            Vector3.Cross(
                forward,
                Vector3.UnitY));

        Vector3 movement =
            right * input.X +
            Vector3.UnitY * input.Y +
            forward * input.Z;

        if (movement.LengthSquared() > 1f)
        {
            movement = Vector3.Normalize(movement);
        }

        _position += movement * _speed * deltaTime;
    }

    public void AdjustSpeed(float steps)
    {
        if (steps == 0f)
        {
            return;
        }

        _speed = Math.Clamp(
            _speed * MathF.Pow(1.25f, steps),
            0.5f,
            3000f);
    }

    public void ShowMapTopDown()
    {
        float altitude = MathF.Max(
            100f,
            _radius * 2.5f);

        _position = _target + new Vector3(
            0f,
            altitude,
            altitude * 0.02f);

        LookAt(_target);
    }

    public Matrix4x4 CreateViewProjection(
        int width,
        int height)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            _position,
            _position + GetForward(),
            Vector3.UnitY);

        float aspect = Math.Max(1, width) /
            (float)Math.Max(1, height);

        Matrix4x4 projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 3f,
                aspect,
                0.1f,
                MathF.Max(10000f, _radius * 20f));

        return view * projection;
    }

    private Vector3 GetForward()
    {
        float cosPitch = MathF.Cos(_pitch);

        return Vector3.Normalize(new Vector3(
            MathF.Sin(_yaw) * cosPitch,
            MathF.Sin(_pitch),
            -MathF.Cos(_yaw) * cosPitch));
    }

    private void LookAt(Vector3 target)
    {
        Vector3 direction = Vector3.Normalize(
            target - _position);

        _yaw = MathF.Atan2(
            direction.X,
            -direction.Z);

        _pitch = MathF.Asin(
            Math.Clamp(
                direction.Y,
                -1f,
                1f));
    }
}
