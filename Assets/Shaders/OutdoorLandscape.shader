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
                        // Broad drainage patches bend across the site, breaking up the fine grain.
                        float drainage = Noise(p * .042 + float2(6,17));
                        float cover = .58 * drainage + .29 * Noise(p * .19 + drainage * 2.8) + .13 * mottling;
                        half3 grass = lerp(half3(.205,.275,.185), half3(.37,.40,.25), mottling);
                        half3 earth = lerp(half3(.265,.24,.20), half3(.41,.37,.30), Noise(p * 1.8));
                        albedo = lerp(earth, grass, smoothstep(.33,.58,cover + .11*grit));
                        float litter = Noise(float2(p.x * 17 + p.y * 6,p.y * 9));
                        albedo *= 1 + .30 * grit + .23 * (litter-.5) * detailFade;
                    }
                    else
                    {
                        // Offset panel joints every other row. Wear and stains cross slab boundaries.
                        float2 slab = p / float2(4,5);
                        slab.x += frac(floor(slab.y) * .5);
                        float2 joint = abs(frac(slab+.5)-.5) * float2(4,5);
                        float aa = max(.008, length(fwidth(p))*.6);
                        float seam = 1 - smoothstep(.014,.014+aa,min(joint.x,joint.y));
                        seam *= saturate(.055/aa);
                        float damp = smoothstep(.45,.78,Noise(p*.14 + mottling*.5));
                        float concrete = .42 + .055*(Hash(floor(slab))-.5) + .08*(mottling-.5) + .13*grit;
                        albedo = concrete * lerp(half3(1,.99,.94),half3(.76,.80,.82),damp) * (1-.30*seam);
                    }
                }
                else
                {
                    // Imported primitive fallback foliage has no painted vertices.
                    albedo = i.color.a > .9 ? _BaseColor.rgb : i.color.rgb;
                    if (i.color.a > .70 && i.color.a < .90)
                    {
                        float gravel = Noise(p * 1.7) * .20 + (Noise(p * 24) - .5) * .47 * saturate(1-length(fwidth(p))*9);
                        albedo *= .90 + gravel;
                    }
                    else if (i.color.a > .25 && i.color.a < .70)
                    {
                        float2 outcrop = p + i.world.y * float2(.37,.69);
                        float strata = Noise(outcrop * float2(.55,1.13)) + Noise(outcrop * 2.7) * .34;
                        float moss = smoothstep(.49,.80,strata) * saturate(i.normal.y);
                        albedo = lerp(albedo, albedo * half3(.78,.92,.66), moss * .65);
                        albedo *= .76 + .37 * strata;
                    }
                }
                half3 normal = normalize(i.normal);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.world));
                half shade = lerp(sun.shadowAttenuation, 1, GetMainLightShadowFade(i.world));
                half3 light = max(.15h, SampleSH(normal)) + sun.color * saturate(dot(normal,sun.direction)) * shade;
                return half4(MixFog(albedo * light, i.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
