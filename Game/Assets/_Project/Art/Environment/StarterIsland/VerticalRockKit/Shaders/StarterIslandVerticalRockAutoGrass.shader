Shader "CML/Environment/Vertical Rock Auto Grass"
{
    Properties
    {
        [NoScaleOffset] _RockMap("Rock Color", 2D) = "white" {}
        [NoScaleOffset] _RockNormalMap("Rock Normal", 2D) = "bump" {}
        [NoScaleOffset] _GrassMap("Grass Color", 2D) = "white" {}
        [NoScaleOffset] _GrassNormalMap("Grass Normal", 2D) = "bump" {}
        _RockTint("Rock Tint", Color) = (1,1,1,1)
        _GrassTint("Grass Tint", Color) = (1,1,1,1)
        _RockTileScale("Rock World Tile Scale", Float) = 0.125
        _GrassTileScale("Grass World Tile Scale", Float) = 0.16
        _TriplanarSharpness("Triplanar Sharpness", Range(1,12)) = 5
        _RockNormalStrength("Rock Normal Strength", Range(0,1)) = 0.48
        _GrassNormalStrength("Grass Normal Strength", Range(0,1)) = 0.18
        _GrassSlopeStart("Grass Slope Start", Range(-1,1)) = 0.52
        _GrassSlopeEnd("Grass Slope End", Range(-1,1)) = 0.73
        _GrassNoiseScale("Grass Edge Scale", Float) = 0.32
        _GrassNoiseStrength("Grass Edge Breakup", Range(0,0.35)) = 0.12
        _RockShadowColor("Rock Shadow", Color) = (0.36,0.23,0.23,1)
        _RockBaseColor("Rock Base", Color) = (0.66,0.40,0.32,1)
        _RockHighlightColor("Rock Highlight", Color) = (0.82,0.61,0.44,1)
        _GrassShadowColor("Grass Shadow", Color) = (0.24,0.32,0.12,1)
        _GrassBaseColor("Grass Base", Color) = (0.50,0.61,0.20,1)
        _GrassHighlightColor("Grass Highlight", Color) = (0.68,0.72,0.28,1)
        _PaletteStrength("Palette Strength", Range(0,1)) = 0.78
        _PlaneToneStrength("Broad Plane Tone", Range(0,1)) = 0.36
        _SurfaceRoughness("Surface Roughness", Range(0.55,1)) = 0.88
        _SpecularStrength("Soft Specular", Range(0,0.15)) = 0.045
        _MacroVariation("Macro Variation", Range(0,0.2)) = 0.055
        _AmbientStrength("Ambient Strength", Range(0,2)) = 0.95
        _ShadowFloor("Shadow Floor", Range(0,1)) = 0.25
        [Toggle] _DebugGrassMask("Debug Grass Mask", Float) = 0
        [HideInInspector] _BaseMap("Compatibility Base Map", 2D) = "white" {}
        [HideInInspector] _BaseColor("Compatibility Base Color", Color) = (1,1,1,1)
        [HideInInspector] _Cutoff("Compatibility Cutoff", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
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

            TEXTURE2D(_RockMap); SAMPLER(sampler_RockMap);
            TEXTURE2D(_RockNormalMap); SAMPLER(sampler_RockNormalMap);
            TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
            TEXTURE2D(_GrassNormalMap); SAMPLER(sampler_GrassNormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _RockTint;
                half4 _GrassTint;
                half _RockTileScale;
                half _GrassTileScale;
                half _TriplanarSharpness;
                half _RockNormalStrength;
                half _GrassNormalStrength;
                half _GrassSlopeStart;
                half _GrassSlopeEnd;
                half _GrassNoiseScale;
                half _GrassNoiseStrength;
                half4 _RockShadowColor;
                half4 _RockBaseColor;
                half4 _RockHighlightColor;
                half4 _GrassShadowColor;
                half4 _GrassBaseColor;
                half4 _GrassHighlightColor;
                half _PaletteStrength;
                half _PlaneToneStrength;
                half _SurfaceRoughness;
                half _SpecularStrength;
                half _MacroVariation;
                half _AmbientStrength;
                half _ShadowFloor;
                half _DebugGrassMask;
                half4 _BaseColor;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                float3 macroNormalOS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half3 macroNormalWS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float ValueNoise2D(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1,0));
                float c = Hash21(cell + float2(0,1));
                float d = Hash21(cell + float2(1,1));
                return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y);
            }

            half3 Palette(half3 source, half3 shadowColor, half3 baseColor, half3 highlightColor)
            {
                half l = dot(source, half3(0.2126h,0.7152h,0.0722h));
                half t = smoothstep(0.055h,0.42h,l);
                half3 palette = t < 0.5h
                    ? lerp(shadowColor, baseColor, t * 2.0h)
                    : lerp(baseColor, highlightColor, (t - 0.5h) * 2.0h);
                return lerp(source, palette, _PaletteStrength);
            }

            void Triplanar(
                TEXTURE2D_PARAM(colorMap, colorSampler),
                TEXTURE2D_PARAM(normalMap, normalSampler),
                float3 positionWS,
                half3 geometricNormalWS,
                half tileScale,
                half normalStrength,
                out half3 albedo,
                out half3 normalWS)
            {
                half3 weights = pow(max(abs(geometricNormalWS), 0.0001h), _TriplanarSharpness);
                weights /= max(weights.x + weights.y + weights.z, 0.0001h);
                half3 axisSign = step(0.0h, geometricNormalWS) * 2.0h - 1.0h;
                float2 uvX = float2(positionWS.z, -positionWS.y * axisSign.x) * tileScale;
                float2 uvY = float2(positionWS.x, -positionWS.z * axisSign.y) * tileScale;
                float2 uvZ = float2(positionWS.x, positionWS.y * axisSign.z) * tileScale;
                half3 cX = SAMPLE_TEXTURE2D(colorMap, colorSampler, uvX).rgb;
                half3 cY = SAMPLE_TEXTURE2D(colorMap, colorSampler, uvY).rgb;
                half3 cZ = SAMPLE_TEXTURE2D(colorMap, colorSampler, uvZ).rgb;
                albedo = cX * weights.x + cY * weights.y + cZ * weights.z;

                half3 tX = UnpackNormalScale(SAMPLE_TEXTURE2D(normalMap, normalSampler, uvX), normalStrength);
                half3 tY = UnpackNormalScale(SAMPLE_TEXTURE2D(normalMap, normalSampler, uvY), normalStrength);
                half3 tZ = UnpackNormalScale(SAMPLE_TEXTURE2D(normalMap, normalSampler, uvZ), normalStrength);
                half3 nX = half3(axisSign.x * tX.z, -axisSign.x * tX.y, tX.x);
                half3 nY = half3(tY.x, axisSign.y * tY.z, -axisSign.y * tY.y);
                half3 nZ = half3(tZ.x, axisSign.z * tZ.y, axisSign.z * tZ.z);
                normalWS = normalize(nX * weights.x + nY * weights.y + nZ * weights.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = NormalizeNormalPerVertex(n.normalWS);
                output.macroNormalWS = TransformObjectToWorldNormal(input.macroNormalOS);
                output.shadowCoord = GetShadowCoord(p);
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(
                Varyings input,
                FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Mesh normals are authored outward. Do not invert them from
                // face winding: the mask must remain stable on repaired and
                // mirrored static meshes and must never paint undersides.
                half3 geometricNormalWS = NormalizeNormalPerPixel(input.normalWS);
                half macroNormalValid = step(0.01h, dot(input.macroNormalWS, input.macroNormalWS));
                half3 macroNormalWS = normalize(lerp(
                    geometricNormalWS,
                    input.macroNormalWS,
                    macroNormalValid));
                half3 rockAlbedo;
                half3 rockNormal;
                half3 grassAlbedo;
                half3 grassNormal;
                Triplanar(TEXTURE2D_ARGS(_RockMap, sampler_RockMap), TEXTURE2D_ARGS(_RockNormalMap, sampler_RockNormalMap),
                    input.positionWS, geometricNormalWS, _RockTileScale, _RockNormalStrength, rockAlbedo, rockNormal);
                Triplanar(TEXTURE2D_ARGS(_GrassMap, sampler_GrassMap), TEXTURE2D_ARGS(_GrassNormalMap, sampler_GrassNormalMap),
                    input.positionWS, geometricNormalWS, _GrassTileScale, _GrassNormalStrength, grassAlbedo, grassNormal);

                rockAlbedo *= _RockTint.rgb;
                grassAlbedo *= _GrassTint.rgb;
                rockAlbedo = Palette(rockAlbedo, _RockShadowColor.rgb, _RockBaseColor.rgb, _RockHighlightColor.rgb);
                grassAlbedo = Palette(grassAlbedo, _GrassShadowColor.rgb, _GrassBaseColor.rgb, _GrassHighlightColor.rgb);

                // Large-scale tone blocks reinforce the authored rock planes.
                // They are intentionally independent from the triplanar micro
                // texture so geometric hierarchy remains readable at distance.
                half planeFacing = saturate(dot(
                    geometricNormalWS,
                    normalize(half3(0.42h, 0.34h, 0.84h))) * 0.5h + 0.5h);
                half3 broadRock = planeFacing < 0.5h
                    ? lerp(_RockShadowColor.rgb, _RockBaseColor.rgb, planeFacing * 2.0h)
                    : lerp(_RockBaseColor.rgb, _RockHighlightColor.rgb, (planeFacing - 0.5h) * 2.0h);
                half rockLuma = dot(rockAlbedo, half3(0.2126h, 0.7152h, 0.0722h));
                broadRock *= lerp(0.88h, 1.08h, saturate(rockLuma * 1.35h));
                rockAlbedo = lerp(rockAlbedo, broadRock, _PlaneToneStrength);

                half lowNoiseA = (half)ValueNoise2D(
                    input.positionWS.xz * _GrassNoiseScale + float2(31.7, 9.2));
                half lowNoiseB = (half)ValueNoise2D(
                    input.positionWS.xz * (_GrassNoiseScale * 0.43h) + float2(8.4, 41.1));
                half lowNoise = lowNoiseA * 0.68h + lowNoiseB * 0.32h;
                half slope = macroNormalWS.y + (lowNoise - 0.5h) * _GrassNoiseStrength;
                half grassMask = smoothstep(_GrassSlopeStart, _GrassSlopeEnd, slope);
                // A macro-up region may cross a sharp overhang boundary. This
                // broad support gate prevents undersides without reintroducing
                // one-triangle green islands from the faceted render normal.
                grassMask *= smoothstep(0.08h, 0.42h, geometricNormalWS.y);
                half macro = (half)ValueNoise2D(input.positionWS.xz * 0.075 + float2(4.8, 17.1));
                rockAlbedo *= 1.0h + (macro - 0.5h) * _MacroVariation;
                grassAlbedo *= 1.0h + (macro - 0.5h) * (_MacroVariation * 0.65h);

                half3 albedo = lerp(rockAlbedo, grassAlbedo, grassMask);
                if (_DebugGrassMask > 0.5h)
                {
                    return half4(grassMask.xxx, 1.0h);
                }
                half3 detailNormal = normalize(lerp(rockNormal, grassNormal, grassMask));
                half3 normalWS = normalize(lerp(geometricNormalWS, detailNormal,
                    lerp(_RockNormalStrength, _GrassNormalStrength, grassMask)));

                Light mainLight = GetMainLight(input.shadowCoord);
                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half wrappedLight = smoothstep(0.01h, 0.93h, nDotL);
                half direct = lerp(_ShadowFloor, 1.0h, wrappedLight) *
                    mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 ambient = max(SampleSH(normalWS), 0.0h);
                half3 color = albedo * (ambient * _AmbientStrength + mainLight.color * direct);
                half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 halfDirectionWS = normalize(mainLight.direction + viewDirectionWS);
                half specularPower = lerp(38.0h, 8.0h, _SurfaceRoughness);
                half softSpecular = pow(saturate(dot(normalWS, halfDirectionWS)), specularPower) *
                    _SpecularStrength * mainLight.shadowAttenuation;
                color += mainLight.color * softSpecular;
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
