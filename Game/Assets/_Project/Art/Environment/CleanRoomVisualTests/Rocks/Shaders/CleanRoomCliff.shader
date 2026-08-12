Shader "CML/Clean Room/Measured Cliff"
{
    Properties
    {
        _RockDark("Rock Dark", Color) = (0.30, 0.105, 0.040, 1)
        _RockBase("Rock Base", Color) = (0.64, 0.255, 0.090, 1)
        _RockLight("Rock Light", Color) = (0.86, 0.49, 0.25, 1)
        _MacroScale("Macro Scale", Range(0.005, 0.2)) = 0.026
        _StrataScale("Vertical Strata Scale", Range(0.01, 0.5)) = 0.082
        _StrataStrength("Vertical Strata Strength", Range(0, 0.35)) = 0.14
        _GrassDark("Grass Fringe", Color) = (0.13, 0.24, 0.045, 1)
        _GrassBase("Grass Base", Color) = (0.30, 0.46, 0.075, 1)
        _GrassLight("Grass Light", Color) = (0.50, 0.65, 0.12, 1)
        _GrassSlopeStart("Grass Slope Start", Range(0, 1)) = 0.52
        _GrassSlopeEnd("Grass Slope End", Range(0, 1)) = 0.84
        _GrassBreakup("Grass Edge Breakup", Range(0, 0.3)) = 0.12
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.92
        _DirectStrength("Direct Strength", Range(0, 2)) = 0.92
        _ShadowFloor("Shadow Floor", Range(0, 1)) = 0.34
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
            "UniversalMaterialType"="Lit"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RockDark;
                half4 _RockBase;
                half4 _RockLight;
                half _MacroScale;
                half _StrataScale;
                half _StrataStrength;
                half4 _GrassDark;
                half4 _GrassBase;
                half4 _GrassLight;
                half _GrassSlopeStart;
                half _GrassSlopeEnd;
                half _GrassBreakup;
                half _AmbientStrength;
                half _DirectStrength;
                half _ShadowFloor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1, 0));
                float c = Hash21(cell + float2(0, 1));
                float d = Hash21(cell + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normals.normalWS);
                output.shadowCoord = GetShadowCoord(positions);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half macro = (half)ValueNoise(input.positionWS.xz * _MacroScale + 17.3);
                half broad = (half)ValueNoise(float2(
                    (input.positionWS.x + input.positionWS.z * 0.63) * _StrataScale,
                    input.positionWS.y * _StrataScale * 0.12 + 41.0));
                half fine = (half)ValueNoise(float2(
                    (input.positionWS.x * 0.31 - input.positionWS.z) * _StrataScale * 2.7,
                    input.positionWS.y * _StrataScale * 0.24 + 79.0));
                half rockValue = saturate(macro * 0.68h + broad * 0.23h + fine * 0.09h);
                half3 rock = rockValue < 0.5h
                    ? lerp(_RockDark.rgb, _RockBase.rgb, rockValue * 2.0h)
                    : lerp(_RockBase.rgb, _RockLight.rgb, (rockValue - 0.5h) * 2.0h);
                half strata = (broad - 0.5h) * _StrataStrength;
                rock *= 1.0h + strata;

                half topNoise = (half)ValueNoise(input.positionWS.xz * 0.11 + 13.0);
                half fineTop = (half)ValueNoise(input.positionWS.xz * 0.37 + 63.0);
                half slope = saturate(normalWS.y +
                    (topNoise - 0.5h) * _GrassBreakup +
                    (fineTop - 0.5h) * _GrassBreakup * 0.42h);
                half fringe = smoothstep(_GrassSlopeStart - 0.08h,
                    _GrassSlopeEnd - 0.12h, slope);
                half crown = smoothstep(_GrassSlopeStart + 0.08h, _GrassSlopeEnd, slope);
                half grassVariation = (half)ValueNoise(input.positionWS.xz * 0.145 + 107.0);
                half3 grass = grassVariation < 0.5h
                    ? lerp(_GrassDark.rgb, _GrassBase.rgb, grassVariation * 2.0h)
                    : lerp(_GrassBase.rgb, _GrassLight.rgb, (grassVariation - 0.5h) * 2.0h);
                half3 baseColor = lerp(rock, _GrassDark.rgb, fringe * (1.0h - crown));
                baseColor = lerp(baseColor, grass, crown);

                Light mainLight = GetMainLight(input.shadowCoord);
                half nDotL = dot(normalWS, mainLight.direction);
                half wrapped = lerp(_ShadowFloor, 1.04h, smoothstep(-0.08h, 0.82h, nDotL));
                // Broad stylized cliffs should retain contact shadow without
                // turning shallow sculpt grooves into black cracks.
                half softenedShadow = lerp(0.72h, 1.0h, mainLight.shadowAttenuation);
                half attenuation = mainLight.distanceAttenuation * softenedShadow;
                half3 ambient = max(SampleSH(normalWS), 0.0h) * _AmbientStrength;
                half3 direct = mainLight.color * wrapped * attenuation * _DirectStrength;
                half3 color = baseColor * (ambient + direct);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack Off
}
