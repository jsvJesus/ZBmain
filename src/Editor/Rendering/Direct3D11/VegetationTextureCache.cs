using System.Collections.Concurrent;
using System.Text;
using Core.Storage;
using Editor.Rendering;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Editor.Rendering.Direct3D11;

internal sealed class VegetationTextureCache : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly MaterialTextureLoader _loader;

    private readonly bool _backgroundLoading;

    private readonly Dictionary<
        string,
        ID3D11ShaderResourceView> _textures =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _unavailable =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentQueue<DecodedTexture> _ready =
        new();

    private readonly SemaphoreSlim _decodeLimit = new(2);
    private readonly CancellationTokenSource _cancellation = new();

    private readonly List<string> _report = new();

    private DateTime _lastReport;
    private bool _disposed;

    private ID3D11ShaderResourceView? _fallback;

    public VegetationTextureCache(
        ID3D11Device device,
        ID3D11DeviceContext context,
        string levelName,
        bool backgroundLoading = false)
    {
        _device = device;
        _context = context;
        _backgroundLoading = backgroundLoading;

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

        _report.Add($"Level: {levelName}");
        _report.Add($"Background loading: {backgroundLoading}");
        _report.Add("Mipmaps: enabled");

        WriteReport();
    }

    public bool HasTexture(string textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return false;
        }

        string key = textureName.Trim();

        if (_unavailable.Contains(key))
        {
            return false;
        }

        return _loader.ResolveReference(key) is not null;
    }

    public ID3D11ShaderResourceView Get(
        string textureName)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(VegetationTextureCache));
        }

        if (string.IsNullOrWhiteSpace(textureName))
        {
            return _fallback!;
        }

        string key = textureName.Trim();

        if (_textures.TryGetValue(
                key,
                out ID3D11ShaderResourceView? cached))
        {
            return cached;
        }

        if (_unavailable.Contains(key))
        {
            return _fallback!;
        }

        if (_backgroundLoading)
        {
            if (_pending.Add(key))
            {
                QueueDecode(key);
            }

            return _fallback!;
        }

        try
        {
            string? path = _loader.ResolveReference(key);

            TexturePixels? pixels = _loader.LoadReference(key);

            return FinishLoad(
                key,
                path,
                pixels);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                ArgumentException or
                NotSupportedException)
        {
            MarkUnavailable(key, exception.Message);
            return _fallback!;
        }
    }

    public void ProcessPendingUploads(int maximumUploads = 1)
    {
        if (_disposed || !_backgroundLoading)
        {
            return;
        }

        int processed = 0;

        while (processed < maximumUploads &&
               _ready.TryDequeue(out DecodedTexture? item))
        {
            _pending.Remove(item.Name);

            if (item.Error is not null)
            {
                MarkUnavailable(
                    item.Name,
                    item.Error);

                processed++;
                continue;
            }

            try
            {
                FinishLoad(
                    item.Name,
                    item.Path,
                    item.Pixels);
            }
            catch (Exception exception) when (
                exception is IOException or
                    InvalidDataException or
                    ArgumentException or
                    NotSupportedException)
            {
                MarkUnavailable(
                    item.Name,
                    exception.Message);
            }

            processed++;
        }

        FlushReport(false);
    }

    private void QueueDecode(string name)
    {
        CancellationToken token = _cancellation.Token;

        _ = Task.Run(async () =>
        {
            bool acquired = false;

            try
            {
                await _decodeLimit.WaitAsync(
                    token).ConfigureAwait(false);

                acquired = true;

                string? path = _loader.ResolveReference(name);

                TexturePixels? pixels =
                    _loader.LoadReference(name);

                if (!token.IsCancellationRequested)
                {
                    _ready.Enqueue(new DecodedTexture(
                        name,
                        path,
                        pixels,
                        null));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested)
                {
                    _ready.Enqueue(new DecodedTexture(
                        name,
                        null,
                        null,
                        exception.Message));
                }
            }
            finally
            {
                if (acquired)
                {
                    _decodeLimit.Release();
                }
            }
        });
    }

    private ID3D11ShaderResourceView FinishLoad(
        string name,
        string? path,
        TexturePixels? pixels)
    {
        if (pixels is null)
        {
            MarkUnavailable(name, "Texture not found");
            return _fallback!;
        }

        ID3D11ShaderResourceView view = CreateTexture(
            pixels.Width,
            pixels.Height,
            pixels.Rgba);

        _textures.Add(name, view);

        _report.Add(
            $"[LOADED] {name} | " +
            $"{pixels.Width}x{pixels.Height} | " +
            $"{path}");

        FlushReport(false);

        return view;
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
                "Invalid RGBA texture.");
        }

        bool generateMipmaps =
            width > 1 || height > 1;

        BindFlags bindFlags = generateMipmaps
            ? BindFlags.ShaderResource |
              BindFlags.RenderTarget
            : BindFlags.ShaderResource;

        ResourceOptionFlags resourceOptions =
            generateMipmaps
                ? ResourceOptionFlags.GenerateMips
                : ResourceOptionFlags.None;

        using ID3D11Texture2D texture =
            _device.CreateTexture2D(
                Format.R8G8B8A8_UNorm,
                (uint)width,
                (uint)height,
                mipLevels: generateMipmaps ? 0u : 1u,
                bindFlags: bindFlags,
                miscFlags: resourceOptions);

        _context.UpdateSubresource(
            rgba,
            texture,
            rowPitch: checked((uint)(width * 4)),
            depthPitch: checked((uint)rgba.Length));

        ID3D11ShaderResourceView view =
            _device.CreateShaderResourceView(texture);

        try
        {
            if (generateMipmaps)
            {
                _context.GenerateMips(view);
            }

            return view;
        }
        catch
        {
            view.Dispose();
            throw;
        }
    }

    private void MarkUnavailable(
        string name,
        string reason)
    {
        _unavailable.Add(name);

        _report.Add(
            $"[ERROR] {name}: {reason}");

        FlushReport(false);
    }

    private void FlushReport(bool force)
    {
        if (!force &&
            DateTime.UtcNow - _lastReport <
            TimeSpan.FromSeconds(2))
        {
            return;
        }

        WriteReport();
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
                "=== DX11 Texture Loading ===");

            foreach (string line in _report)
            {
                text.AppendLine(line);
            }

            File.WriteAllText(
                Path.Combine(
                    directory,
                    "vegetation-textures.log"),
                text.ToString());

            _lastReport = DateTime.UtcNow;
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cancellation.Cancel();

        FlushReport(true);

        foreach (ID3D11ShaderResourceView texture
                 in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        _pending.Clear();
        _unavailable.Clear();

        while (_ready.TryDequeue(out _))
        {
        }

        _fallback?.Dispose();
        _fallback = null;
    }

    private sealed record DecodedTexture(
        string Name,
        string? Path,
        TexturePixels? Pixels,
        string? Error);
}
