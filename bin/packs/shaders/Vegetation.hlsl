cbuffer FrameData : register(b0)
{
    row_major float4x4 ViewProjection;
    float4 Frame;
};

struct VertexInput
{
    float3 Position : POSITION;
    float2 Uv : TEXCOORD0;

    float4 PositionScale : INSTANCE;
    float4 TintRotation : TINT;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD1;
    float2 Uv : TEXCOORD0;
    float3 Tint : COLOR0;
    float Height : TEXCOORD2;
};

PixelInput VSMain(VertexInput input)
{
    PixelInput output;

    float angle = input.TintRotation.w;

    float sine;
    float cosine;

    sincos(angle, sine, cosine);

    float3 localPosition = input.Position;

    float3 rotatedPosition = float3(
        localPosition.x * cosine -
        localPosition.z * sine,
        localPosition.y,
        localPosition.x * sine +
        localPosition.z * cosine);

    float scale = input.PositionScale.w;

    float3 worldPosition =
        input.PositionScale.xyz +
        rotatedPosition * scale;

    float heightFactor = saturate(
        localPosition.y / 1.5f);

    heightFactor *= heightFactor;

    float time = Frame.x;

    float phase =
        input.PositionScale.x * 0.13f +
        input.PositionScale.z * 0.17f +
        angle;

    float wind = sin(
        time * 1.8f + phase);

    float gust = sin(
        time * 0.7f +
        input.PositionScale.x * 0.04f +
        input.PositionScale.z * 0.06f);

    float displacement =
        (wind * 0.7f + gust * 0.3f) *
        heightFactor *
        scale *
        0.12f;

    worldPosition.x += displacement;
    worldPosition.z += displacement * 0.55f;

    output.Position = mul(
        float4(worldPosition, 1.0f),
        ViewProjection);

    output.WorldPosition = worldPosition;
    output.Uv = input.Uv;
    output.Tint = input.TintRotation.rgb;
    output.Height = heightFactor;

    return output;
}

float4 PSMain(PixelInput input) : SV_TARGET
{
    float3 dx = ddx(input.WorldPosition);
    float3 dy = ddy(input.WorldPosition);

    float3 normal = cross(dx, dy);

    float normalLength = length(normal);

    if (normalLength > 0.00001f)
    {
        normal /= normalLength;
    }
    else
    {
        normal = float3(0.0f, 1.0f, 0.0f);
    }

    float3 lightDirection = normalize(
        float3(0.4f, 0.82f, 0.35f));

    float diffuse = abs(dot(
        normal,
        lightDirection));

    float lighting =
        0.48f +
        diffuse * 0.44f;

    float3 baseColor = float3(
        0.32f,
        0.40f,
        0.25f);

    float uvVariation = saturate(
        input.Uv.y) * 0.06f;

    baseColor += uvVariation;

    float heightLighting = lerp(
        0.90f,
        1.04f,
        input.Height);

    float3 finalColor =
        baseColor *
        input.Tint *
        lighting *
        heightLighting;

    return float4(
        saturate(finalColor),
        1.0f);
}
