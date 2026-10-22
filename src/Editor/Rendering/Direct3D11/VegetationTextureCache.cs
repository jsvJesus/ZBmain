using System.Text;
using Editor.Rendering;
using Core.Storage;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Editor.Rendering.Direct3D11;

internal sealed class VegetationTextureCache : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly MaterialTextureLoader _loader;

    private readonly Dictionary<
        string,
        ID3D11ShaderResourceView> _textures =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _report = new();
    private readonly HashSet<string> _unavailable = new(StringComparer.OrdinalIgnoreCase);

    private ID3D11ShaderResourceView? _fallback;

    public VegetationTextureCache(
        ID3D11Device device,
        ID3D11DeviceContext context,
        string levelName)
    {
        _device = device;
        _context = context;

        _loader = new MaterialTextureLoader(levelName);

        try
        {
            _fallback = CreateTexture(
                1,
                1,
                [190, 205, 175, 255]);
        }
        catch
        {
            Dispose();
            throw;
        }

        _report.Add(
            $"Level: {levelName}");

        WriteReport();
    }

    public ID3D11ShaderResourceView Get(
        string textureName)
    {
        string key = textureName.Trim();

        if (_unavailable.Contains(key))
        {
            return _fallback!;
        }

        if (_textures.TryGetValue(
                key,
                out ID3D11ShaderResourceView? cached))
        {
            return cached;
        }

        try
        {
            string? resolvedPath =
                _loader.ResolveReference(key);

            TexturePixels? pixels =
                _loader.LoadReference(key);

            if (pixels is null)
            {
                _unavailable.Add(key);
                _report.Add(
                    $"[MISSING] {key}");

                WriteReport();
                return _fallback!;
            }

            ID3D11ShaderResourceView texture =
                CreateTexture(
                    pixels.Width,
                    pixels.Height,
                    pixels.Rgba);

            _textures.Add(key, texture);

            _report.Add(
                $"[LOADED] {key}");

            _report.Add(
                $"  Source: {resolvedPath}");

            _report.Add(
                $"  Size: {pixels.Width}x{pixels.Height}");

            WriteReport();

            return texture;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                ArgumentException or
                NotSupportedException)
        {
            _report.Add(
                $"[ERROR] {key}: {exception.Message}");

            WriteReport();

            return _fallback!;
        }
    }

    private ID3D11ShaderResourceView CreateTexture(
        int width,
        int height,
        byte[] rgba)
    {
        if (width <= 0 ||
            height <= 0 ||
            rgba.Length != checked(width * height * 4))
        {
            throw new InvalidDataException(
                "Некорректные RGBA-данные текстуры.");
        }

        using ID3D11Texture2D texture =
            _device.CreateTexture2D(
                Format.R8G8B8A8_UNorm,
                (uint)width,
                (uint)height,
                1,
                1,
                null,
                BindFlags.ShaderResource);

        _context.UpdateSubresource(
            rgba,
            texture,
            rowPitch: checked((uint)(width * 4)),
            depthPitch: checked((uint)rgba.Length));

        return _device.CreateShaderResourceView(
            texture);
    }

    private void WriteReport()
    {
        try
        {
            string directory = Path.Combine(
                ProjectPaths.BinDirectory,
                "logs");

            Directory.CreateDirectory(directory);

            var text = new StringBuilder();

            text.AppendLine(
                "=== DX11 Vegetation Textures ===");

            foreach (string line in _report)
            {
                text.AppendLine(line);
            }

            File.WriteAllText(
                Path.Combine(
                    directory,
                    "vegetation-textures.log"),
                text.ToString());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        foreach (ID3D11ShaderResourceView texture
                 in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();

        _fallback?.Dispose();
        _fallback = null;
    }
}
