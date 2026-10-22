
cbuffer FrameData : register(b0)
{
    float4 Frame;
};

struct VertexInput
{
    float4 PositionScale : INSTANCE;
    float4 TintPhase : TINT;
    uint VertexId : SV_VertexID;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
    float3 Color : COLOR0;
    float Height : TEXCOORD0;
    float Edge : TEXCOORD1;
};

PixelInput VSMain(VertexInput input)
{
    PixelInput output;

    uint localVertex = input.VertexId % 6;
    uint blade = input.VertexId / 6;

    float side = 1.0;

    if (localVertex == 0 || localVertex == 3)
    {
        side = -1.0;
    }
    else if (localVertex == 1)
    {
        side = -0.45;
    }
    else if (localVertex == 2 || localVertex == 4)
    {
        side = 0.45;
    }

    float height = 0.0;

    if (localVertex == 1 ||
        localVertex == 2 ||
        localVertex == 4)
    {
        height = 1.0;
    }

    float3 world = input.PositionScale.xyz;
    float size = input.PositionScale.w;

    float2 bladeDirection =
        blade == 0
            ? float2(1.0, 0.0)
            : float2(0.55, 0.83);

    float2 origin = float2(
        world.x * 0.76 + world.z * 0.24,
        world.z * 0.29 - world.x * 0.08 - world.y * 0.18);

    float wind = sin(
        Frame.x * 1.7 +
        input.TintPhase.w +
        world.x * 12.0 +
        world.z * 8.0);

    float gust = sin(
        Frame.x * 0.56 +
        world.x * 5.0 +
        world.z * 4.0);

    float bend = (wind * 0.65 + gust * 0.35) *
                 height * height *
                 size * 0.68;

    float2 offset = bladeDirection * side * size;

    offset.x += bend;
    offset.y += height * size * 3.3;

    float2 position = origin + offset;

    output.Position = float4(
        position,
        saturate(0.5 + world.z * 0.12),
        1.0);

    output.Color = input.TintPhase.rgb;
    output.Height = height;
    output.Edge = abs(side);

    return output;
}

float4 PSMain(PixelInput input) : SV_TARGET
{
    float3 rootColor = input.Color * 0.43;
    float3 tipColor = input.Color * 1.22;

    float lighting = lerp(
        0.8,
        1.08,
        saturate(input.Height));

    float3 color = lerp(
        rootColor,
        tipColor,
        saturate(input.Height));

    color *= lighting;

    return float4(saturate(color), 1.0);
}
