// Persistent fluid displacement, with restrained graphite and silver luminance bands.
float Time : register(c0);
float Aspect : register(c1);
sampler2D Flow : register(s1);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 lookup = (uv * float2(63.0, 39.0) + 0.5) / float2(128.0, 40.0);
    float2 packedX = tex2D(Flow, lookup).rg;
    float2 packedY = tex2D(Flow, lookup + float2(0.5, 0.0)).rg;
    float2 displacement = float2(dot(packedX, float2(256.0, 1.0)), dot(packedY, float2(256.0, 1.0))) / 257.0 - 0.5;
    float t = Time * 0.18;
    float2 p = (uv + displacement - 0.5) * float2(Aspect, 1.0) * 2.4;
    [unroll] for (int i = 1; i <= 3; i++)
    {
        float k = (float)i;
        p += float2(sin(p.y * 1.65 + k * 1.2 + t * 0.7), cos(p.x * 1.45 - k * 0.9 - t * 0.6)) * (0.65 / k);
    }
    float phase = p.x * 1.6 + p.y * 1.1 + 0.65 * sin(p.y * 1.8 - p.x * 0.7 + t * 0.3);
    float band = 0.5 + 0.5 * sin(phase);
    float3 color = lerp(float3(0.065, 0.072, 0.085), float3(0.46, 0.49, 0.535), smoothstep(0.06, 0.94, band));
    float sheen = pow(0.5 + 0.5 * sin(phase + 0.55), 14.0) * 0.22;
    color = lerp(color, float3(0.76, 0.79, 0.835), sheen);
    float2 center = uv - 0.5;
    return float4(color * (0.96 - 0.3 * dot(center, center)), 1.0);
}
