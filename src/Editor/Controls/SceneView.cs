
using System.Numerics;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Core.Levels;
using Editor.Rendering;
using Silk.NET.OpenGL;

namespace Editor.Controls;

public sealed unsafe class SceneView : OpenGlControlBase
{
    private const int VertexStride = 5 * sizeof(float);
    private const int MaxTextureUploadsPerFrame = 2;
    private const float MouseSensitivity = 0.003f;
    private int _alphaCutoffLocation;

    private GL? _gl;

    private uint _program;
    private uint _vao;
    private uint _gridBuffer;

    private int _viewProjectionLocation;
    private int _modelLocation;
    private int _tintLocation;
    private int _textureLocation;
    private int _hasTextureLocation;
    private int _gridLocation;

    private RenderLevel? _scene;
    private MaterialTextureLoader? _textureLoader;

    private readonly List<uint> _vertexBuffers = new();
    private readonly List<uint> _indexBuffers = new();
    private readonly Dictionary<int, uint> _textures = new();

    private Vector3 _cameraPosition = new(80f, 60f, 80f);

    private float _yaw;
    private float _pitch;
    private float _radius = 100f;
    private float _movementSpeed = 20f;

    private bool _resourcesDirty = true;

    public SceneView()
    {
        ClipToBounds = true;
        ResetCamera();
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

    public float MovementSpeed => _movementSpeed;

    public void ResetCamera()
    {
        Vector3 target;

        if (_scene is null)
        {
            target = Vector3.Zero;
            _radius = 100f;
        }
        else
        {
            target = (_scene.Min + _scene.Max) * 0.5f;

            _radius = MathF.Max(
                25f,
                (_scene.Max - _scene.Min).Length() * 0.5f);
        }

        _cameraPosition = target + new Vector3(
            _radius * 0.9f,
            _radius * 0.65f,
            _radius * 0.9f);

        Vector3 direction = Vector3.Normalize(
            target - _cameraPosition);

        _yaw = MathF.Atan2(
            direction.X,
            -direction.Z);

        _pitch = MathF.Asin(
            Math.Clamp(direction.Y, -1f, 1f));

        _movementSpeed = Math.Clamp(
            _radius * 0.08f,
            10f,
            120f);

        RequestNextFrameRendering();
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
            _yaw -= 2f * MathF.PI;
        }
        else if (_yaw < -MathF.PI)
        {
            _yaw += 2f * MathF.PI;
        }

        RequestNextFrameRendering();
    }

    public void AdjustSpeed(float wheelSteps)
    {
        if (wheelSteps == 0f)
        {
            return;
        }

        _movementSpeed = Math.Clamp(
            _movementSpeed * MathF.Pow(1.25f, wheelSteps),
            0.5f,
            3000f);
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

        Vector3 forward = GetLookDirection();

        Vector3 right = Vector3.Normalize(
            Vector3.Cross(forward, Vector3.UnitY));

        Vector3 movement =
            right * input.X +
            Vector3.UnitY * input.Y +
            forward * input.Z;

        if (movement.LengthSquared() > 1f)
        {
            movement = Vector3.Normalize(movement);
        }

        _cameraPosition +=
            movement * _movementSpeed * deltaTime;

        RequestNextFrameRendering();
    }

    private Vector3 GetLookDirection()
    {
        float cosPitch = MathF.Cos(_pitch);

        return Vector3.Normalize(new Vector3(
            MathF.Sin(_yaw) * cosPitch,
            MathF.Sin(_pitch),
            -MathF.Cos(_yaw) * cosPitch));
    }

    private Matrix4x4 CreateViewProjection(
        int width,
        int height)
    {
        Vector3 forward = GetLookDirection();

        Matrix4x4 view = Matrix4x4.CreateLookAt(
            _cameraPosition,
            _cameraPosition + forward,
            Vector3.UnitY);

        Matrix4x4 projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 3f,
                width / (float)height,
                0.1f,
                MathF.Max(10000f, _radius * 20f));

        return view * projection;
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

        string vertexShaderSource = shaderHeader + """
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
                vUv = vec2(aUv.x, 1.0 - aUv.y);

                gl_Position = uViewProjection * world;
            }
            """;

        string fragmentShaderSource = shaderHeader + """
            in vec2 vUv;
            in vec3 vWorldPosition;

            uniform sampler2D uTexture;
            uniform int uHasTexture;
            uniform int uGrid;
            uniform vec3 uTint;
            uniform float uAlphaCutoff;

            out vec4 outputColor;

            void main()
            {
                if (uGrid != 0)
                {
                    outputColor = vec4(uTint, 1.0);
                    return;
                }

                vec4 baseColor = vec4(uTint, 1.0);

                if (uHasTexture != 0)
                {
                    baseColor = texture(uTexture, vUv);
                }

                if (uAlphaCutoff > 0.0 && baseColor.a < uAlphaCutoff)
                {
                    discard;
                }

                vec3 dx = dFdx(vWorldPosition);
                vec3 dy = dFdy(vWorldPosition);

                vec3 normal = cross(dx, dy);

                float diffuse = 0.5;

                if (dot(normal, normal) > 0.000001)
                {
                    normal = normalize(normal);

                    diffuse = abs(dot(
                        normal,
                        normalize(vec3(0.4, 0.8, 0.5))));
                }

                float lighting = 0.45 + 0.55 * diffuse;

                outputColor = vec4(
                    baseColor.rgb * lighting,
                    1.0);
            }
            """;

        uint vertexShader = CompileShader(
            ShaderType.VertexShader,
            vertexShaderSource);

        uint fragmentShader = CompileShader(
            ShaderType.FragmentShader,
            fragmentShaderSource);

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

        _gridLocation = _gl.GetUniformLocation(
            _program,
            "uGrid");

        _alphaCutoffLocation = _gl.GetUniformLocation(
            _program,
            "uAlphaCutoff");

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

        GL gl = _gl;

        gl.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            (uint)framebuffer);

        int width = Math.Max(1, (int)Bounds.Width);
        int height = Math.Max(1, (int)Bounds.Height);

        gl.Viewport(
            0,
            0,
            (uint)width,
            (uint)height);

        gl.ClearColor(
            0.035f,
            0.055f,
            0.085f,
            1f);

        gl.Clear(
            (uint)(ClearBufferMask.ColorBufferBit |
                   ClearBufferMask.DepthBufferBit));

        gl.Enable(EnableCap.DepthTest);
        gl.BindVertexArray(_vao);
        gl.UseProgram(_program);

        if (_resourcesDirty)
        {
            UploadScene();
            _resourcesDirty = false;
        }

        Matrix4x4 viewProjection =
            CreateViewProjection(width, height);

        gl.UniformMatrix4(
            _viewProjectionLocation,
            1,
            false,
            (float*)&viewProjection);

        if (_scene is null)
        {
            DrawGrid();
            return;
        }

        gl.Uniform1(_gridLocation, 0);

        int remainingUploads = MaxTextureUploadsPerFrame;
        bool needsAnotherFrame = false;

        int currentMaterial = int.MinValue;
        Matrix4x4 previousModel = Matrix4x4.Identity;

        for (int i = 0; i < _scene.Surfaces.Length; i++)
        {
            RenderSurface surface = _scene.Surfaces[i];

            if (surface.IndexCount < 3)
            {
                continue;
            }

            gl.BindBuffer(
                BufferTargetARB.ArrayBuffer,
                _vertexBuffers[surface.VertexBuffer]);

            gl.BindBuffer(
                BufferTargetARB.ElementArrayBuffer,
                _indexBuffers[surface.IndexBuffer]);

            nint vertexOffset =
                (nint)surface.VertexOffset * VertexStride;

            gl.VertexAttribPointer(
                0,
                3,
                VertexAttribPointerType.Float,
                false,
                VertexStride,
                (void*)vertexOffset);

            gl.VertexAttribPointer(
                1,
                2,
                VertexAttribPointerType.Float,
                false,
                VertexStride,
                (void*)(vertexOffset + 3 * sizeof(float)));

            if (i == 0 || surface.Transform != previousModel)
            {
                Matrix4x4 model = surface.Transform;

                gl.UniformMatrix4(
                    _modelLocation,
                    1,
                    false,
                    (float*)&model);

                previousModel = model;
            }

            if (surface.Material != currentMaterial)
            {
                BindMaterial(
                    surface.Material,
                    ref remainingUploads,
                    ref needsAnotherFrame);

                currentMaterial = surface.Material;
            }

            gl.DrawElements(
                PrimitiveType.Triangles,
                (uint)surface.IndexCount,
                DrawElementsType.UnsignedShort,
                (void*)(surface.IndexOffset * sizeof(ushort)));
        }

        gl.BindVertexArray(0);

        if (needsAnotherFrame)
        {
            RequestNextFrameRendering();
        }
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
        GL? gl = _gl;

        if (gl is null)
        {
            return;
        }

        DeleteSceneResources();

        if (_scene is null)
        {
            return;
        }

        gl.BindVertexArray(_vao);

        _textureLoader = new MaterialTextureLoader(_scene.Name);
        _textureLoader.WriteMaterialReport(_scene.Materials);

        foreach (RenderVertexBuffer buffer in _scene.Vertices)
        {
            uint handle = gl.GenBuffer();

            gl.BindBuffer(
                BufferTargetARB.ArrayBuffer,
                handle);

            fixed (float* pointer = buffer.Data)
            {
                gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    (nuint)(buffer.Data.Length * sizeof(float)),
                    pointer,
                    BufferUsageARB.StaticDraw);
            }

            _vertexBuffers.Add(handle);
        }

        foreach (RenderIndexBuffer buffer in _scene.Indices)
        {
            uint handle = gl.GenBuffer();

            gl.BindBuffer(
                BufferTargetARB.ElementArrayBuffer,
                handle);

            fixed (ushort* pointer = buffer.Data)
            {
                gl.BufferData(
                    BufferTargetARB.ElementArrayBuffer,
                    (nuint)(buffer.Data.Length * sizeof(ushort)),
                    pointer,
                    BufferUsageARB.StaticDraw);
            }

            _indexBuffers.Add(handle);
        }
    }

    private void BindMaterial(
        int materialId,
        ref int remainingUploads,
        ref bool needsAnotherFrame)
    {
        GL gl = _gl!;

        string name = materialId >= 0 &&
                      materialId < _scene!.Materials.Length
            ? _scene.Materials[materialId]
            : string.Empty;

        XRayMaterialReference reference =
            XRayMaterialReference.Parse(name);

        string shader = reference.Shader;

        bool alphaTest =
            shader.Contains(
                "aref",
                StringComparison.OrdinalIgnoreCase) ||
            shader.StartsWith(
                "flora\\leaf",
                StringComparison.OrdinalIgnoreCase) ||
            shader.StartsWith(
                "details\\lod",
                StringComparison.OrdinalIgnoreCase);

        float alphaCutoff = alphaTest ? 0.45f : 0f;

        gl.Uniform1(
            _alphaCutoffLocation,
            alphaCutoff);

        uint texture;

        if (!_textures.TryGetValue(materialId, out texture))
        {
            if (remainingUploads > 0)
            {
                remainingUploads--;

                texture = CreateTexture(name);
                _textures.Add(materialId, texture);
            }
            else
            {
                texture = 0;
                needsAnotherFrame = true;
            }
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

        TexturePixels? pixels;

        try
        {
            pixels = _textureLoader.Load(material);
        }
        catch (Exception exception)
        {
            _textureLoader.ReportError(
                material,
                exception);

            return 0;
        }

        if (pixels is null)
        {
            return 0;
        }

        GL gl = _gl;

        uint texture = gl.GenTexture();

        gl.ActiveTexture(TextureUnit.Texture0);

        gl.BindTexture(
            TextureTarget.Texture2D,
            texture);

        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Linear);

        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);

        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.Repeat);

        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.Repeat);

        gl.PixelStore(
            PixelStoreParameter.UnpackAlignment,
            1);

        fixed (byte* pointer = pixels.Rgba)
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba,
                (uint)pixels.Width,
                (uint)pixels.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pointer);
        }

        return texture;
    }

    private void CreateGrid()
    {
        GL gl = _gl!;

        var vertices = new List<float>();

        for (int i = -20; i <= 20; i++)
        {
            AddGridVertex(vertices, i * 5, 0, -100);
            AddGridVertex(vertices, i * 5, 0, 100);

            AddGridVertex(vertices, -100, 0, i * 5);
            AddGridVertex(vertices, 100, 0, i * 5);
        }

        _gridBuffer = gl.GenBuffer();

        gl.BindBuffer(
            BufferTargetARB.ArrayBuffer,
            _gridBuffer);

        float[] data = vertices.ToArray();

        fixed (float* pointer = data)
        {
            gl.BufferData(
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

        gl.BindVertexArray(_vao);

        gl.BindBuffer(
            BufferTargetARB.ArrayBuffer,
            _gridBuffer);

        gl.VertexAttribPointer(
            0,
            3,
            VertexAttribPointerType.Float,
            false,
            VertexStride,
            (void*)0);

        gl.VertexAttribPointer(
            1,
            2,
            VertexAttribPointerType.Float,
            false,
            VertexStride,
            (void*)(3 * sizeof(float)));

        Matrix4x4 identity = Matrix4x4.Identity;

        gl.UniformMatrix4(
            _modelLocation,
            1,
            false,
            (float*)&identity);

        gl.Uniform1(_gridLocation, 1);
        gl.Uniform1(_hasTextureLocation, 0);

        gl.Uniform3(
            _tintLocation,
            0.2f,
            0.32f,
            0.45f);

        gl.DrawArrays(
            PrimitiveType.Lines,
            0,
            41 * 4);

        gl.Uniform1(_gridLocation, 0);
        gl.BindVertexArray(0);
    }

    private void DeleteSceneResources()
    {
        if (_gl is null)
        {
            return;
        }

        foreach (uint handle in _vertexBuffers)
        {
            _gl.DeleteBuffer(handle);
        }

        foreach (uint handle in _indexBuffers)
        {
            _gl.DeleteBuffer(handle);
        }

        _vertexBuffers.Clear();
        _indexBuffers.Clear();

        foreach (uint handle in _textures.Values)
        {
            if (handle != 0)
            {
                _gl.DeleteTexture(handle);
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
