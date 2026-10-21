
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Core.Levels;
using Editor.Rendering;
using Silk.NET.OpenGL;

namespace Editor.Controls;

public sealed unsafe class SceneView : OpenGlControlBase
{
    private const int VertexStride = 5 * sizeof(float);

    private sealed record GpuBuffer(
        uint VertexHandle,
        uint IndexHandle);

    private GL? _gl;
    private uint _program;
    private uint _vao;
    private uint _gridBuffer;

    private int _viewProjectionLocation;
    private int _modelLocation;
    private int _tintLocation;
    private int _textureLocation;
    private int _hasTextureLocation;

    private RenderLevel? _scene;
    private MaterialTextureLoader? _textureLoader;

    private readonly List<GpuBuffer> _buffers = new();
    private readonly Dictionary<int, uint> _textures = new();

    private Vector3 _target;
    private float _radius = 100f;
    private float _distance = 250f;
    private float _yaw = 0.7f;
    private float _pitch = 0.5f;

    private Point? _lastPointer;
    private bool _orbiting;
    private bool _panning;
    private bool _resourcesDirty = true;

    public SceneView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public RenderLevel? Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            _resourcesDirty = true;
            ResetCamera();
            RequestNextFrameRendering();
        }
    }

    public void ResetCamera()
    {
        _yaw = 0.7f;
        _pitch = 0.5f;

        if (_scene is null)
        {
            _target = Vector3.Zero;
            _radius = 100f;
        }
        else
        {
            _target = (_scene.Min + _scene.Max) / 2f;

            _radius = MathF.Max(
                25f,
                (_scene.Max - _scene.Min).Length() * 0.5f);
        }

        _distance = _radius * 2.5f;
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface context)
    {
        base.OnOpenGlInit(context);

        _gl = GL.GetApi(context.GetProcAddress);

        string version = _gl.GetStringS(StringName.Version);
        bool isEs = version.Contains(
            "OpenGL ES",
            StringComparison.OrdinalIgnoreCase);

        string shaderHeader = isEs
            ? "#version 300 es\nprecision highp float;\n"
            : "#version 330 core\n";

        string vertexSource = shaderHeader + """
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec2 aUv;

            uniform mat4 uViewProjection;
            uniform mat4 uModel;

            out vec2 vUv;
            out vec3 vWorldPosition;

            void main()
            {
                vec4 world = uModel * vec4(aPosition, 1.0);

                vWorldPosition = world.xyz;
                vUv = aUv;

                gl_Position = uViewProjection * world;
            }
            """;

        string fragmentSource = shaderHeader + """
            in vec2 vUv;
            in vec3 vWorldPosition;

            uniform sampler2D uTexture;
            uniform int uHasTexture;
            uniform vec3 uTint;

            out vec4 outputColor;

            void main()
            {
                vec4 baseColor = vec4(uTint, 1.0);

                if (uHasTexture != 0)
                {
                    baseColor = texture(uTexture, vUv);
                }

                if (baseColor.a < 0.45)
                {
                    discard;
                }

                vec3 dx = dFdx(vWorldPosition);
                vec3 dy = dFdy(vWorldPosition);

                vec3 normal = normalize(cross(dx, dy));
                vec3 lightDirection = normalize(
                    vec3(0.4, 0.8, 0.5));

                float diffuse = abs(
                    dot(normal, lightDirection));

                float lighting = 0.45 + 0.55 * diffuse;

                outputColor = vec4(
                    baseColor.rgb * lighting,
                    1.0);
            }
            """;

        uint vertexShader = CompileShader(
            ShaderType.VertexShader,
            vertexSource);

        uint fragmentShader = CompileShader(
            ShaderType.FragmentShader,
            fragmentSource);

        _program = _gl.CreateProgram();

        _gl.AttachShader(_program, vertexShader);
        _gl.AttachShader(_program, fragmentShader);
        _gl.LinkProgram(_program);

        _gl.GetProgram(
            _program,
            ProgramPropertyARB.LinkStatus,
            out int linked);

        if (linked == 0)
        {
            throw new InvalidOperationException(
                _gl.GetProgramInfoLog(_program));
        }

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _viewProjectionLocation = _gl.GetUniformLocation(
            _program,
            "uViewProjection");

        _modelLocation = _gl.GetUniformLocation(
            _program,
            "uModel");

        _tintLocation = _gl.GetUniformLocation(
            _program,
            "uTint");

        _textureLocation = _gl.GetUniformLocation(
            _program,
            "uTexture");

        _hasTextureLocation = _gl.GetUniformLocation(
            _program,
            "uHasTexture");

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _gl.EnableVertexAttribArray(0);
        _gl.EnableVertexAttribArray(1);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.CullFace);

        _gl.UseProgram(_program);
        _gl.Uniform1(_textureLocation, 0);

        CreateGrid();

        _resourcesDirty = true;
    }

    protected override void OnOpenGlRender(
        GlInterface context,
        int framebuffer)
    {
        if (_gl is null)
        {
            return;
        }

        _gl.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            (uint)framebuffer);

        int width = Math.Max(
            1,
            (int)Bounds.Width);

        int height = Math.Max(
            1,
            (int)Bounds.Height);

        _gl.Viewport(
            0,
            0,
            (uint)width,
            (uint)height);

        _gl.ClearColor(
            0.035f,
            0.055f,
            0.085f,
            1f);

        _gl.Clear(
            (uint)(ClearBufferMask.ColorBufferBit |
                   ClearBufferMask.DepthBufferBit));

        _gl.Enable(EnableCap.DepthTest);

        if (_resourcesDirty)
        {
            UploadScene();
            _resourcesDirty = false;
        }

        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);

        Matrix4x4 viewProjection = CreateViewProjection(
            width,
            height);

        _gl.UniformMatrix4(
            _viewProjectionLocation,
            1,
            false,
            (float*)&viewProjection);

        if (_scene is null)
        {
            DrawGrid();
            return;
        }

        Matrix4x4 previousModel = Matrix4x4.Identity;
        int currentMaterial = int.MinValue;

        for (int i = 0; i < _scene.Surfaces.Length; i++)
        {
            RenderSurface surface = _scene.Surfaces[i];

            if (surface.IndexCount < 3)
            {
                continue;
            }

            GpuBuffer buffer = _buffers[surface.VertexBuffer];

            _gl.BindBuffer(
                BufferTargetARB.ArrayBuffer,
                buffer.VertexHandle);

            _gl.BindBuffer(
                BufferTargetARB.ElementArrayBuffer,
                _buffers[surface.IndexBuffer].IndexHandle);

            nint vertexOffset =
                (nint)surface.VertexOffset * VertexStride;

            _gl.VertexAttribPointer(
                0,
                3,
                VertexAttribPointerType.Float,
                false,
                VertexStride,
                (void*)vertexOffset);

            _gl.VertexAttribPointer(
                1,
                2,
                VertexAttribPointerType.Float,
                false,
                VertexStride,
                (void*)(vertexOffset + 3 * sizeof(float)));

            if (i == 0 || surface.Transform != previousModel)
            {
                Matrix4x4 model = surface.Transform;

                _gl.UniformMatrix4(
                    _modelLocation,
                    1,
                    false,
                    (float*)&model);

                previousModel = model;
            }

            if (surface.Material != currentMaterial)
            {
                BindMaterial(surface.Material);
                currentMaterial = surface.Material;
            }

            _gl.DrawElements(
                PrimitiveType.Triangles,
                (uint)surface.IndexCount,
                DrawElementsType.UnsignedShort,
                (void*)(surface.IndexOffset * sizeof(ushort)));
        }

        _gl.BindVertexArray(0);
    }

    private uint CompileShader(
        ShaderType type,
        string source)
    {
        GL gl = _gl!;

        uint shader = gl.CreateShader(type);

        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(
            shader,
            ShaderParameterName.CompileStatus,
            out int success);

        if (success == 0)
        {
            string log = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);

            throw new InvalidOperationException(
                "Ошибка GLSL: " + log);
        }

        return shader;
    }

    private void UploadScene()
    {
        if (_gl is null)
        {
            return;
        }

        _gl.BindVertexArray(_vao);
        DeleteSceneResources();

        if (_scene is null)
        {
            return;
        }

        _textureLoader = new MaterialTextureLoader();

        int count = Math.Max(
            _scene.Vertices.Length,
            _scene.Indices.Length);

        for (int i = 0; i < count; i++)
        {
            uint vertexHandle = _gl.GenBuffer();
            uint indexHandle = _gl.GenBuffer();

            _gl.BindBuffer(
                BufferTargetARB.ArrayBuffer,
                vertexHandle);

            float[] vertices = i < _scene.Vertices.Length
                ? _scene.Vertices[i].Data
                : [];

            fixed (float* vertexPointer = vertices)
            {
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    (nuint)(vertices.Length * sizeof(float)),
                    vertexPointer,
                    BufferUsageARB.StaticDraw);
            }

            _gl.BindBuffer(
                BufferTargetARB.ElementArrayBuffer,
                indexHandle);

            ushort[] indices = i < _scene.Indices.Length
                ? _scene.Indices[i].Data
                : [];

            fixed (ushort* indexPointer = indices)
            {
                _gl.BufferData(
                    BufferTargetARB.ElementArrayBuffer,
                    (nuint)(indices.Length * sizeof(ushort)),
                    indexPointer,
                    BufferUsageARB.StaticDraw);
            }

            _buffers.Add(new GpuBuffer(
                vertexHandle,
                indexHandle));
        }
    }

    private void BindMaterial(int materialId)
    {
        GL gl = _gl!;

        string name = materialId >= 0 &&
                      materialId < _scene!.Materials.Length
            ? _scene.Materials[materialId]
            : string.Empty;

        uint texture;

        if (!_textures.TryGetValue(materialId, out texture))
        {
            texture = CreateTexture(name);
            _textures.Add(materialId, texture);
        }

        gl.ActiveTexture(TextureUnit.Texture0);

        gl.BindTexture(
            TextureTarget.Texture2D,
            texture);

        gl.Uniform1(
            _hasTextureLocation,
            texture == 0 ? 0 : 1);

        int hash = materialId * 1103515245;

        gl.Uniform3(
            _tintLocation,
            0.48f + ((hash >> 4) & 15) / 60f,
            0.48f + ((hash >> 8) & 15) / 60f,
            0.48f + ((hash >> 12) & 15) / 60f);
    }

    private uint CreateTexture(string material)
    {
        if (_gl is null || _textureLoader is null)
        {
            return 0;
        }

        TexturePixels? image;

        try
        {
            image = _textureLoader.Load(material);
        }
        catch (Exception exception) when (
            exception is IOException or
                NotSupportedException or
                InvalidDataException or
                ArgumentException)
        {
            return 0;
        }

        if (image is null)
        {
            return 0;
        }

        uint texture = _gl.GenTexture();

        _gl.BindTexture(
            TextureTarget.Texture2D,
            texture);

        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Linear);

        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);

        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.Repeat);

        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.Repeat);

        fixed (byte* pointer = image.Rgba)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba,
                (uint)image.Width,
                (uint)image.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pointer);
        }

        return texture;
    }

    private Matrix4x4 CreateViewProjection(
        int width,
        int height)
    {
        Vector3 direction = GetCameraDirection();
        Vector3 position = _target + direction * _distance;

        Matrix4x4 view = Matrix4x4.CreateLookAt(
            position,
            _target,
            Vector3.UnitY);

        Matrix4x4 projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 3f,
                width / (float)height,
                MathF.Max(0.1f, _distance / 10000f),
                MathF.Max(
                    10000f,
                    _distance + _radius * 10f));

        return view * projection;
    }

    private Vector3 GetCameraDirection()
    {
        return new Vector3(
            MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch),
            MathF.Cos(_pitch) * MathF.Cos(_yaw));
    }

    private void CreateGrid()
    {
        var vertices = new List<float>();

        for (int i = -20; i <= 20; i++)
        {
            AddGridVertex(vertices, i * 5, 0, -100);
            AddGridVertex(vertices, i * 5, 0, 100);
            AddGridVertex(vertices, -100, 0, i * 5);
            AddGridVertex(vertices, 100, 0, i * 5);
        }

        _gridBuffer = _gl!.GenBuffer();

        _gl.BindBuffer(
            BufferTargetARB.ArrayBuffer,
            _gridBuffer);

        float[] data = vertices.ToArray();

        fixed (float* pointer = data)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(data.Length * sizeof(float)),
                pointer,
                BufferUsageARB.StaticDraw);
        }
    }

    private static void AddGridVertex(
        List<float> data,
        float x,
        float y,
        float z)
    {
        data.Add(x);
        data.Add(y);
        data.Add(z);
        data.Add(0);
        data.Add(0);
    }

    private void DrawGrid()
    {
        GL gl = _gl!;

        gl.BindBuffer(
            BufferTargetARB.ArrayBuffer,
            _gridBuffer);

        gl.VertexAttribPointer(
            0, 3, VertexAttribPointerType.Float,
            false, VertexStride, (void*)0);

        gl.VertexAttribPointer(
            1, 2, VertexAttribPointerType.Float,
            false, VertexStride, (void*)(3 * sizeof(float)));

        Matrix4x4 identity = Matrix4x4.Identity;

        gl.UniformMatrix4(
            _modelLocation,
            1,
            false,
            (float*)&identity);

        gl.Uniform1(_hasTextureLocation, 0);
        gl.Uniform3(_tintLocation, 0.2f, 0.32f, 0.45f);

        gl.DrawArrays(
            PrimitiveType.Lines,
            0,
            41 * 4);
    }

    protected override void OnPointerPressed(
        PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPointProperties properties =
            e.GetCurrentPoint(this).Properties;

        _orbiting = properties.IsLeftButtonPressed;

        _panning =
            properties.IsRightButtonPressed ||
            properties.IsMiddleButtonPressed;

        if (_orbiting || _panning)
        {
            _lastPointer = e.GetPosition(this);
            e.Pointer.Capture(this);
            Focus();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(
        PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_lastPointer is null)
        {
            return;
        }

        Point current = e.GetPosition(this);
        Avalonia.Vector delta = current - _lastPointer.Value;

        _lastPointer = current;

        if (_orbiting)
        {
            _yaw += (float)delta.X * 0.007f;

            _pitch = Math.Clamp(
                _pitch + (float)delta.Y * 0.007f,
                -1.4f,
                1.4f);
        }
        else if (_panning)
        {
            Vector3 direction = GetCameraDirection();

            Vector3 right = Vector3.Normalize(
                Vector3.Cross(Vector3.UnitY, direction));

            Vector3 up = Vector3.Normalize(
                Vector3.Cross(direction, right));

            float scale = _distance /
                          MathF.Max(1, (float)Bounds.Height);

            _target -= right * ((float)delta.X * scale);
            _target += up * ((float)delta.Y * scale);
        }

        RequestNextFrameRendering();
        e.Handled = true;
    }

    protected override void OnPointerReleased(
        PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _lastPointer = null;
        _orbiting = false;
        _panning = false;

        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(
        PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _lastPointer = null;
        _orbiting = false;
        _panning = false;
    }

    protected override void OnPointerWheelChanged(
        PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        _distance = Math.Clamp(
            _distance * MathF.Pow(
                0.84f,
                (float)e.Delta.Y),
            0.5f,
            _radius * 50f);

        RequestNextFrameRendering();
        e.Handled = true;
    }

    private void DeleteSceneResources()
    {
        if (_gl is null)
        {
            return;
        }

        foreach (GpuBuffer buffer in _buffers)
        {
            _gl.DeleteBuffer(buffer.VertexHandle);
            _gl.DeleteBuffer(buffer.IndexHandle);
        }

        _buffers.Clear();

        foreach (uint texture in _textures.Values)
        {
            if (texture != 0)
            {
                _gl.DeleteTexture(texture);
            }
        }

        _textures.Clear();
        _textureLoader = null;
    }

    protected override void OnOpenGlDeinit(
        GlInterface context)
    {
        if (_gl is not null)
        {
            DeleteSceneResources();

            _gl.DeleteBuffer(_gridBuffer);
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteProgram(_program);

            _gl.Dispose();
            _gl = null;
        }

        base.OnOpenGlDeinit(context);
    }
}
