Shader "ShiftFour/Outdoor Landscape"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        [Enum(Meadow,0,Concrete,1,Vegetation and Stone,2)] _LandscapeSurface("Surface", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _LandscapeSurface;
            CBUFFER_END
            struct Attributes { float4 position : POSITION; float3 normal : NORMAL; half4 color : COLOR; };
            struct Varyings
            {
                float4 clip : SV_POSITION;
                float3 world : TEXCOORD0;
                half3 normal : TEXCOORD1;
                half fog : TEXCOORD3;
                half4 color : COLOR;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.world = TransformObjectToWorld(v.position.xyz);
                o.clip = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.clip.z);
                return o;
            }

            float Hash(float2 p)
            {
                float3 h = frac(float3(p.xyx) * .1031);
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
            }

            float Noise(float2 p)
            {
                float2 tile = floor(p);
                float2 f = frac(p);
                f = f*f*(3.0-2.0*f);
                return lerp(lerp(Hash(tile), Hash(tile+float2(1,0)), f.x),
                            lerp(Hash(tile+float2(0,1)), Hash(tile+1), f.x), f.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.world.xz;
                half3 albedo;
                if (_LandscapeSurface < 1.5)
                {
                    float detailFade = 1.0 - saturate(length(fwidth(p)) * 11);
                    float grit = (Noise(p * 33) - .5) * detailFade;
                    float mottling = Noise(p * .66);
                    if (_LandscapeSurface < .5)
                    {
                        float cover = Noise(p * .095) * .55 + Noise(p * .23 + 13) * .32 + mottling * .13;
                        half3 grass = lerp(half3(.22,.31,.15), half3(.46,.49,.24), mottling);
                        half3 earth = lerp(half3(.31,.245,.16), half3(.49,.45,.34), Noise(p * 2.4));
                        albedo = lerp(earth, grass, smoothstep(.29,.53,cover + .17*grit));
                        float blades = Noise(float2(p.x * 28 + p.y * 8,p.y * 7));
                        albedo *= 1 + .26 * grit + .30 * (blades-.5) * detailFade;
                    }
                    else
                    {
                        float2 slab = p / float2(4,5);
                        float2 joint = abs(frac(slab+.5)-.5) * float2(4,5);
                        float aa = max(.008, length(fwidth(p))*.6);
                        float seam = 1 - smoothstep(.014,.014+aa,min(joint.x,joint.y));
                        seam *= saturate(.055/aa);
                        half concrete = .43 + .045 * (Hash(floor(slab))-.5) + .075*(mottling-.5) + .14*grit;
                        albedo = concrete * half3(.94,.98,1) * (1-.42*seam);
                    }
                }
                else
                {
                    // Imported primitive fallback foliage has no painted vertices.
                    albedo = i.color.a > .9 ? _BaseColor.rgb : i.color.rgb;
                    if (i.color.a > .70 && i.color.a < .90)
                        albedo *= 1 + .48 * (Noise(p * 24) - .5) * saturate(1-length(fwidth(p))*9);
                    else if (i.color.a > .25 && i.color.a < .70)
                    {
                        float2 outcrop = p + i.world.y * float2(.37,.69);
                        float strata = Noise(outcrop * float2(.55,1.13)) + Noise(outcrop * 2.7) * .34;
                        albedo *= .80 + .32 * strata;
                    }
                }
                half3 normal = normalize(i.normal);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.world));
                half3 light = max(.15h, SampleSH(normal)) + sun.color * saturate(dot(normal,sun.direction)) * sun.shadowAttenuation;
                return half4(MixFog(albedo * light, i.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
