cbuffer FrameData : register(b0)
{
    row_major float4x4 ViewProjection;
    float4 Frame;
};

cbuffer ObjectData : register(b1)
{
    row_major float4x4 Model;
    float4 MaterialSettings;
};

Texture2D DiffuseTexture : register(t0);
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

float4 PSMain(PixelInput input) : SV_TARGET
{
    float4 albedo = DiffuseTexture.Sample(
        DiffuseSampler,
        input.Uv);

    if (MaterialSettings.x > 0.0f)
    {
        clip(albedo.a - MaterialSettings.x);
    }

    float3 dx = ddx(input.WorldPosition);
    float3 dy = ddy(input.WorldPosition);

    float3 normal = cross(dx, dy);
    float lengthSquared = dot(normal, normal);

    normal = lengthSquared > 0.000001f
        ? normalize(normal)
        : float3(0.0f, 1.0f, 0.0f);

    float3 sunlight = normalize(
        float3(0.4f, 0.8f, 0.5f));

    float diffuse = abs(dot(normal, sunlight));

    float lighting = 0.45f + diffuse * 0.55f;

    return float4(
        saturate(albedo.rgb * lighting),
        1.0f);
}
