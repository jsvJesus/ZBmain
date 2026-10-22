namespace Editor.Rendering;

public sealed record XRayMaterialReference(
    string Shader,
    string BaseTexture,
    IReadOnlyList<string> AuxiliaryTextures)
{
    public bool IsTerrain =>
        Shader.StartsWith(
            "levels\\",
            StringComparison.OrdinalIgnoreCase) ||
        Shader.StartsWith(
            "levels/",
            StringComparison.OrdinalIgnoreCase);

    public IEnumerable<string> EnumerateTextures()
    {
        if (!string.IsNullOrWhiteSpace(BaseTexture))
        {
            yield return BaseTexture;
        }

        foreach (string texture in AuxiliaryTextures)
        {
            if (!string.IsNullOrWhiteSpace(texture))
            {
                yield return texture;
            }
        }
    }

    public static XRayMaterialReference Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new XRayMaterialReference(
                string.Empty,
                string.Empty,
                Array.Empty<string>());
        }

        string source = value.Trim();
        string shader = string.Empty;
        string textureList = source;

        int separator = source.IndexOf('/');

        if (separator >= 0)
        {
            shader = source[..separator];
            textureList = source[(separator + 1)..];
        }

        string[] textures = textureList.Split(
            ',',
            StringSplitOptions.TrimEntries);

        string baseTexture = textures.Length > 0
            ? textures[0]
            : string.Empty;

        string[] auxiliary = textures
            .Skip(1)
            .Where(texture => !string.IsNullOrWhiteSpace(texture))
            .ToArray();

        return new XRayMaterialReference(
            shader,
            baseTexture,
            auxiliary);
    }
}
