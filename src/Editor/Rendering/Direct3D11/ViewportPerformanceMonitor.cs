using System.Diagnostics;
using System.Text;
using Core.Storage;

namespace Editor.Rendering.Direct3D11;

internal sealed class ViewportPerformanceMonitor
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private int _frames;
    private double _totalFrameMilliseconds;
    private double _worstFrameMilliseconds;

    public void Record(
        TimeSpan frameTime,
        LevelGpuScene? level,
        VegetationManager? vegetation)
    {
        double milliseconds = frameTime.TotalMilliseconds;

        _frames++;
        _totalFrameMilliseconds += milliseconds;

        _worstFrameMilliseconds = Math.Max(
            _worstFrameMilliseconds,
            milliseconds);

        double elapsed = _clock.Elapsed.TotalSeconds;

        if (elapsed < 5.0)
        {
            return;
        }

        double fps = _frames / elapsed;

        double averageMilliseconds = _frames > 0
            ? _totalFrameMilliseconds / _frames
            : 0.0;

        var report = new StringBuilder();

        report.AppendLine(
            "=== DX11 Viewport Performance ===");

        report.AppendLine(
            $"FPS: {fps:F1}");

        report.AppendLine(
            $"Average frame: {averageMilliseconds:F2} ms");

        report.AppendLine(
            $"Worst frame: {_worstFrameMilliseconds:F2} ms");

        report.AppendLine(
            $"Visible surfaces: {level?.LastVisibleSurfaces ?? 0}");

        report.AppendLine(
            $"Culled surfaces: {level?.LastCulledSurfaces ?? 0}");

        report.AppendLine(
            $"Submitted triangles: " +
            $"{level?.LastSubmittedTriangles ?? 0:N0}");

        report.AppendLine(
            $"Vegetation chunks: {vegetation?.LoadedChunks ?? 0}");

        report.AppendLine(
            $"Vegetation instances: " +
            $"{vegetation?.LoadedInstances ?? 0:N0}");

        try
        {
            string directory = Path.Combine(
                ProjectPaths.BinDirectory,
                "logs");

            Directory.CreateDirectory(directory);

            File.WriteAllText(
                Path.Combine(
                    directory,
                    "dx11-viewport.log"),
                report.ToString());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        _frames = 0;
        _totalFrameMilliseconds = 0;
        _worstFrameMilliseconds = 0;

        _clock.Restart();
    }
}
