using System.Numerics;
using System.Text.Json;
using Core.Levels;
using Editor.Rendering;

namespace Editor.Rendering.Direct3D11;

internal sealed record TerrainAppearance(
    string DetailTexture,
    Vector3 Tint,
    float TextureScale);

internal sealed class TerrainMaterialCatalog
{
    private readonly Dictionary<
        string,
        TerrainAppearance> _profiles =
        new(StringComparer.OrdinalIgnoreCase);

    public TerrainMaterialCatalog(string levelName)
    {
        SetDefaults();
        LoadConfiguration(levelName);
    }

    public TerrainAppearance? Resolve(
        XRayMaterialReference material)
    {
        if (!material.IsTerrain)
        {
            return null;
        }

        string shader = material.Shader
            .Replace('/', '\\')
            .ToLowerInvariant();

        if (shader.Contains("grass"))
        {
            return _profiles["grass"];
        }

        if (shader.Contains("sand") ||
            shader.Contains("dirt"))
        {
            return _profiles["sand"];
        }

        if (shader.Contains("asfalt") ||
            shader.Contains("asphalt"))
        {
            return _profiles["asphalt"];
        }

        return _profiles["default"];
    }

    private void SetDefaults()
    {
        _profiles["grass"] = new TerrainAppearance(
            "terrain/grass",
            new Vector3(0.40f, 0.44f, 0.30f),
            0.35f);

        _profiles["sand"] = new TerrainAppearance(
            "terrain/sand",
            new Vector3(0.49f, 0.43f, 0.34f),
            0.35f);

        _profiles["asphalt"] = new TerrainAppearance(
            "terrain/asphalt",
            new Vector3(0.31f, 0.32f, 0.31f),
            0.28f);

        _profiles["default"] = new TerrainAppearance(
            "",
            new Vector3(0.42f, 0.41f, 0.35f),
            0.30f);
    }

    private void LoadConfiguration(string levelName)
    {
        string path = Path.Combine(
            LevelPaths.LevelDirectory(levelName),
            "materials",
            "terrain.json");

        if (!File.Exists(path))
        {
            return;
        }

        using JsonDocument document =
            JsonDocument.Parse(File.ReadAllText(path));

        if (document.RootElement.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "terrain.json должен содержать объект.");
        }

        foreach (JsonProperty property in
                 document.RootElement.EnumerateObject())
        {
            if (!_profiles.TryGetValue(
                    property.Name,
                    out TerrainAppearance? existing))
            {
                continue;
            }

            JsonElement value = property.Value;

            if (value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string texture = existing.DetailTexture;
            Vector3 color = existing.Tint;
            float scale = existing.TextureScale;

            if (value.TryGetProperty(
                    "texture",
                    out JsonElement textureValue))
            {
                texture = textureValue.GetString() ?? "";
            }

            if (value.TryGetProperty(
                    "color",
                    out JsonElement colorValue) &&
                colorValue.ValueKind == JsonValueKind.Array &&
                colorValue.GetArrayLength() == 3)
            {
                color = new Vector3(
                    colorValue[0].GetSingle(),
                    colorValue[1].GetSingle(),
                    colorValue[2].GetSingle());
            }

            if (value.TryGetProperty(
                    "tileMeters",
                    out JsonElement tileValue))
            {
                float meters = tileValue.GetSingle();

                if (float.IsFinite(meters) &&
                    meters > 0.01f)
                {
                    scale = 1f / meters;
                }
            }

            if (!IsFinite(color))
            {
                throw new InvalidDataException(
                    "Некорректный цвет материала террейна.");
            }

            _profiles[property.Name] =
                new TerrainAppearance(
                    texture,
                    color,
                    scale);
        }
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z);
    }
}
