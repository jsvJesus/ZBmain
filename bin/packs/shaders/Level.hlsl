cbuffer FrameData : register(b0)
{
    row_major float4x4 ViewProjection;
    float4 Frame;
};

cbuffer ObjectData : register(b1)
{
    row_major float4x4 Model;
    float4 MaterialSettings;
    float4 MaterialColor;
};

Texture2D DiffuseTexture : register(t0);
Texture2D TerrainDetailTexture : register(t1);

SamplerState DiffuseSampler : register(s0);

struct VertexInput
{
    float3 Position : POSITION;
    float2 Uv : TEXCOORD0;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD1;
    float2 Uv : TEXCOORD0;
};

PixelInput VSMain(VertexInput input)
{
    PixelInput output;

    float4 world = mul(
        float4(input.Position, 1.0f),
        Model);

    output.Position = mul(
        world,
        ViewProjection);

    output.WorldPosition = world.xyz;
    output.Uv = input.Uv;

    return output;
}

float3 CalculateNormal(float3 worldPosition)
{
    float3 dx = ddx(worldPosition);
    float3 dy = ddy(worldPosition);

    float3 normal = cross(dx, dy);

    float lengthSquared = dot(normal, normal);

    if (lengthSquared > 0.000001f)
    {
        return normalize(normal);
    }

    return float3(0.0f, 1.0f, 0.0f);
}

float3 SampleTerrain(
    float4 macroTexture,
    float3 worldPosition)
{
    float luminance = dot(
        macroTexture.rgb,
        float3(0.333333f, 0.333333f, 0.333334f));

    float macroVariation = saturate(
        luminance / 0.32f);

    float brightness = lerp(
        0.72f,
        1.15f,
        macroVariation);

    float3 baseColor =
        MaterialColor.rgb * brightness;

    if (MaterialSettings.w > 0.5f)
    {
        float2 detailUv =
            worldPosition.xz * MaterialSettings.z;

        float3 detailColor =
            TerrainDetailTexture.Sample(
                DiffuseSampler,
                detailUv).rgb;

        baseColor = detailColor * brightness;
    }

    return saturate(baseColor);
}

float4 PSMain(PixelInput input) : SV_TARGET
{
    float4 albedo = DiffuseTexture.Sample(
        DiffuseSampler,
        input.Uv);

    if (MaterialSettings.x > 0.0f)
    {
        clip(albedo.a - MaterialSettings.x);
    }

    float3 baseColor = albedo.rgb;

    if (MaterialSettings.y > 0.5f)
    {
        baseColor = SampleTerrain(
            albedo,
            input.WorldPosition);
    }

    float3 normal = CalculateNormal(
        input.WorldPosition);

    float3 sunlight = normalize(
        float3(0.4f, 0.8f, 0.5f));

    float diffuse = abs(dot(
        normal,
        sunlight));

    float lighting =
        0.45f + diffuse * 0.55f;

    return float4(
        saturate(baseColor * lighting),
        1.0f);
}
