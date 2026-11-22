Shader "ShiftFour/Hall Instrumentation"
{
    Properties
    {
        _Ink("Signal colour", Color) = (0.1,0.85,0.95,1)
        _Pattern("Instrument pattern", Float) = 0
        _Level("Mission state", Float) = 0
        _Offset("Clock offset", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Ink;
                float _Pattern, _Level, _Offset;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                return o;
            }

            // Filter diagram strokes at their projected pixel width.
            float stroke(float distanceToLine, float width)
            {
                return 1 - smoothstep(width, width + max(fwidth(distanceToLine), .001), abs(distanceToLine));
            }
            float segment(float2 p, float2 a, float2 b, float width)
            {
                float2 d = b - a;
                return stroke(length(p - a - saturate(dot(p - a,d) / dot(d,d)) * d), width);
            }
            float ring(float2 p, float r) { return stroke(length(p) - r, .009); }
            float grid(float2 uv, float2 freq)
            {
                float2 cell = abs(frac(uv * freq + .5) - .5);
                return max(stroke(cell.x,.018), stroke(cell.y,.018));
            }
            half4 lightInk(half3 ink, float strength, float opacity)
            {
                return half4(ink * (0.6 + 1.25 * strength), saturate(opacity));
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv, p = uv * 2 - 1;
                float t = _Time.y + _Offset;
                half3 ink = _Ink.rgb;
                if (_Pattern < .5) // Optical curtain. Only the passing line and two edge rulers are visible.
                {
                    float y = .5 + .45 * sin(t * 1.4);
                    float beam = stroke(uv.y-y,.002);
                    float ticks = step(.965,abs(p.x)) * step(.72,frac(uv.y*16));
                    return lightInk(ink,1,max(beam * .42,ticks * .78));
                }
                if (_Pattern < 1.5) // Segmented indicator / conduit.
                {
                    float cell = smoothstep(.025,.10,frac(uv.x*12)) * (1-smoothstep(.86,.98,frac(uv.x*12)));
                    float pulse = pow(saturate(.5+.5*cos(6.283*(uv.x-t*.42))),4);
                    float level = _Level > 1 ? .76 : _Level < 0 ? .58+.16*sin(t*2) : lerp(.16,.62+.32*pulse,_Level);
                    return lightInk(ink,level,cell * (.35+.55*level));
                }
                if (_Pattern < 2.5) // Rack blades, perforated grille and asynchronous packet LEDs.
                {
                    float2 bank=uv*float2(13,12), cell=frac(bank);
                    float rail=stroke(cell.y-.06,.025);
                    float vent=step(.40,cell.y)*step(cell.y,.62)*step(.2,cell.x)*step(cell.x,.72);
                    float led=step(.76,uv.x)*step(.27,cell.x)*step(cell.x,.77)*step(.47,cell.y)*step(cell.y,.77);
                    float activity=.25+.75*pow(saturate(.5+.5*sin(t*2.3+dot(floor(bank),float2(2.3,1.7)))),3);
                    return half4(ink*(.16*rail+.20*vent+led*activity*1.6), .78);
                }
                if (_Pattern < 3.5) // Packet trace travels across the actual network sequence.
                {
                    float wire=segment(p,float2(-.85,0),float2(.85,0),.028);
                    float nodes=ring(float2((frac(uv.x*3)-.5)*4,p.y),.43);
                    float packet=pow(saturate(.5+.5*cos(6.283*(uv.x-t*.36))),12);
                    float lit=step(uv.x,saturate(_Level));
                    return lightInk(ink,1,max(nodes,wire*(.22+.55*packet+.22*lit)));
                }
                if (_Pattern < 4.5) // Axial fan blades and fixed concentric shroud.
                {
                    float r=length(p);
                    float a=atan2(p.y,p.x)+r*.82-t*1.3*_Level;
                    float blade=smoothstep(-.30,.18,sin(a*7))*smoothstep(.15,.24,r)*(1-smoothstep(.76,.83,r));
                    float rim=ring(p,.92)+.6*ring(p,.84);
                    float hub=1-smoothstep(.14,.19,r);
                    return half4(ink*(.13+.40*blade+.8*rim+.5*hub), (1-smoothstep(.94,.98,r))*.96);
                }
                if (_Pattern < 5.5) // Range scope with fixed contacts and a phosphor sweep.
                {
                    float r=length(p);
                    float a=atan2(p.y,p.x);
                    float sweep=pow(saturate(.5+.5*cos(a-t*.85)),25)*step(r,.89);
                    float contacts=ring(p-float2(.33,.25),.05)+ring(p-float2(-.48,-.19),.04);
                    float marks=.2*grid(uv,6)+.52*(ring(p,.88)+ring(p,.56)+ring(p,.26));
                    return lightInk(ink,1,saturate(marks+.45*sweep+contacts));
                }
                if (_Pattern < 6.5) // Facility map: four connected footprints and a slow survey sweep.
                {
                    float2 d=abs(p)-float2(.51,.43);
                    float outline=stroke(max(abs(d.x)-.18,abs(d.y)-.13),.012);
                    float streets=max(stroke(abs(p.x)-.51,.014)*step(abs(p.y),.43),
                                      stroke(abs(p.y)-.43,.014)*step(abs(p.x),.51));
                    float sweep=exp(-8*abs(p.y-.86*sin(t*.54)));
                    return lightInk(ink,1,.13*grid(uv,10)+.52*streets+outline+.11*sweep);
                }
                if (_Pattern > 10.5) // Projected containment rings and vertical bus contacts.
                {
                    float hoop = stroke(abs(p.y)-.89,.035);
                    float contacts = stroke(frac(uv.x*8+.5)-.5,.03);
                    float flow = pow(saturate(.5+.5*cos(11*p.y-t*2.1)),5);
                    return lightInk(ink,1,.035+hoop*(.25+.39*_Level)+contacts*(.15+.55*flow*_Level));
                }
                if (_Pattern > 9.5) // Stamped wall panels, bevel returns, fasteners and recessed light slots.
                {
                    float2 q = float2(frac(uv.x*4),uv.y);
                    float2 local = abs(q-.5);
                    float chamfer = max(max(local.x-.48,local.y-.46),local.x+local.y-.87);
                    float bevel = stroke(chamfer,.004);
                    float slit = stroke(q.y-.79,.006)*step(.12,q.x)*step(q.x,.81);
                    float grill = step(.22,q.y)*step(q.y,.52)*step(.16,q.x)*step(q.x,.42)
                        *smoothstep(.56,.80,frac(q.y*28+q.x*2));
                    float pin = 1-smoothstep(.009,.016,length(local-float2(.42,.36)));
                    half3 steel = half3(.064,.089,.10)*(1+.5*q.y);
                    return half4(steel + ink*(.22*bevel+.74*slit+.17*grill+.49*pin),.89);
                }
                if (_Pattern > 8.5) // The four volumes are free-standing, translucent wireframes.
                {
                    float edge = stroke(1-max(abs(p.x),abs(p.y)),.025);
                    float scan = stroke(uv.y-frac(t*.23),.03);
                    return lightInk(ink,1,.075+.80*edge+.18*scan);
                }
                if (_Pattern > 7.5) // Painted chevrons remain stationary, even when power is off.
                {
                    float stripe = smoothstep(.42,.48,frac(uv.x*13 + .56*uv.y));
                    return half4(lerp(half3(.045,.052,.052),ink*.76,stripe),.90);
                }
                // Generator field coils. Offline retains the physical rings. Power adds circulating charge.
                float r=length(p);
                float angle=atan2(p.y,p.x);
                float rings=ring(p,.88)+ring(p,.73)+ring(p,.43);
                float arc=stroke(r-.59,.06)*smoothstep(-.2,.3,sin(3*angle-t*1.65));
                float core=exp(-9*r*r);
                return lightInk(ink,1,rings*(.28+.52*_Level)+_Level*(arc*.83+core*.71));
            }
            ENDHLSL
        }
    }
}
