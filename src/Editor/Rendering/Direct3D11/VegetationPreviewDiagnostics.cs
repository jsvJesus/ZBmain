using System.Numerics;
using System.Text;
using Core.Levels;
using Core.Storage;

namespace Editor.Rendering.Direct3D11;

internal static class VegetationPreviewDiagnostics
{
    public static void Write(
        DetailLevel details,
        VegetationBuildResult result,
        int processedCells,
        int generatedInstances)
    {
        var report = new StringBuilder();

        report.AppendLine("=== DX11 Vegetation Preview ===");

        report.AppendLine(
            $"Cells: {details.Cells.Length}");

        report.AppendLine(
            $"Models: {details.Models.Length}");

        report.AppendLine(
            $"Processed cells: {processedCells}");

        report.AppendLine(
            $"Generated instances: {generatedInstances}");

        report.AppendLine(
            $"Selected instances: {result.InstanceCount}");

        report.AppendLine(
            $"GPU batches: {result.Batches.Length}");

        report.AppendLine(
            $"Focus: {result.Center.X:F2}, " +
            $"{result.Center.Y:F2}, " +
            $"{result.Center.Z:F2}");

        foreach (VegetationBatch batch in result.Batches)
        {
            DetailModel model = details.Models[
                batch.ModelIndex
            ];

            Vector3 minimum = new(
                float.PositiveInfinity);

            Vector3 maximum = new(
                float.NegativeInfinity);

            foreach (DetailVertex vertex in model.Vertices)
            {
                minimum = Vector3.Min(
                    minimum,
                    vertex.Position);

                maximum = Vector3.Max(
                    maximum,
                    vertex.Position);
            }

            report.AppendLine();
            report.AppendLine(
                $"Model: {batch.ModelIndex}");

            report.AppendLine(
                $"Texture: {model.Texture}");

            report.AppendLine(
                $"Vertices: {model.Vertices.Length}");

            report.AppendLine(
                $"Indices: {model.Indices.Length}");

            report.AppendLine(
                $"Instances: {batch.Instances.Length}");

            report.AppendLine(
                $"Scale: {model.MinScale:F3} - " +
                $"{model.MaxScale:F3}");

            report.AppendLine(
                $"Bounds min: {minimum}");

            report.AppendLine(
                $"Bounds max: {maximum}");

            if (batch.Instances.Length > 0)
            {
                report.AppendLine(
                    $"First instance: " +
                    $"{batch.Instances[0].PositionScale}");
            }
        }

        try
        {
            string directory = Path.Combine(
                ProjectPaths.BinDirectory,
                "logs");

            Directory.CreateDirectory(directory);

            File.WriteAllText(
                Path.Combine(
                    directory,
                    "vegetation-preview.log"),
                report.ToString());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
