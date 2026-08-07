Shader "CML/Environment/Starter Island Cliff Rock"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Cliff Texture", 2D) = "white" {}
        [NoScaleOffset] _NormalMap("Cliff Normal", 2D) = "bump" {}
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _TileScale("World Tile Scale", Float) = 0.083333
        _TriplanarSharpness("Projection Sharpness", Range(1, 12)) = 5.2
        _NormalStrength("Normal Strength", Range(0, 1)) = 0.62
        _Brightness("Brightness", Range(0.8, 1.3)) = 1.08
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.66
        _ShadowFloor("Shadow Floor", Range(0, 1)) = 0.16
        _MacroVariation("Macro Variation", Range(0, 0.25)) = 0.15
        _RunoffVariation("Runoff Variation", Range(0, 0.12)) = 0.04
        _CliffShadowColor("Cliff Shadow Color", Color) = (0.455, 0.243, 0.212, 1)
        _CliffBaseColor("Cliff Base Color", Color) = (0.725, 0.376, 0.263, 1)
        _CliffHighlightColor("Cliff Highlight Color", Color) = (0.855, 0.541, 0.373, 1)
        _CliffPaletteStrength("Cliff Palette Strength", Range(0, 1)) = 0.82
        _CliffCavityColor("Cliff Cavity Color", Color) = (0.408, 0.227, 0.208, 1)
        _CliffCavityStrength("Cliff Cavity Strength", Range(0, 0.6)) = 0.28
        _CliffReliefNormalStrength("Cliff Broad Relief", Range(0, 6)) = 2.6
        [HideInInspector] _CMLHitOffsetWS("Hit Offset", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _TileScale;
                half _TriplanarSharpness;
                half _NormalStrength;
                half _Brightness;
                half _AmbientStrength;
                half _ShadowFloor;
                half _MacroVariation;
                half _RunoffVariation;
                half4 _CliffShadowColor;
                half4 _CliffBaseColor;
                half4 _CliffHighlightColor;
                half _CliffPaletteStrength;
                half4 _CliffCavityColor;
                half _CliffCavityStrength;
                half _CliffReliefNormalStrength;
                float4 _CMLHitOffsetWS;
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

            float Hash21(float2 position)
            {
                position = frac(position * float2(123.34, 345.45));
                position += dot(position, position + 34.345);
                return frac(position.x * position.y);
            }

            float ValueNoise2D(float2 position)
            {
                float2 cell = floor(position);
                float2 blend = frac(position);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(
                    lerp(a, b, blend.x),
                    lerp(c, d, blend.x),
                    blend.y);
            }

            half3 ApplyCliffPalette(half3 source)
            {
                half luminance =
                    dot(source, half3(0.2126h, 0.7152h, 0.0722h));
                half tone =
                    smoothstep(0.055h, 0.34h, luminance);
                half3 palette =
                    tone < 0.5h
                        ? lerp(
                            _CliffShadowColor.rgb,
                            _CliffBaseColor.rgb,
                            tone * 2.0h)
                        : lerp(
                            _CliffBaseColor.rgb,
                            _CliffHighlightColor.rgb,
                            (tone - 0.5h) * 2.0h);
                half3 sourceChroma =
                    source / max(luminance, 0.025h);
                half3 detailedPalette =
                    palette * lerp(half3(1.0h, 1.0h, 1.0h), sourceChroma, 0.10h);
                return lerp(source, detailedPalette, _CliffPaletteStrength);
            }

            half3 ApplyHeightNormal(
                float3 positionWS,
                half3 normalWS,
                half height,
                half strength)
            {
                float3 positionDx = ddx(positionWS);
                float3 positionDy = ddy(positionWS);
                half heightDx = ddx(height);
                half heightDy = ddy(height);
                float3 positionDyPerpendicular =
                    cross(positionDy, (float3)normalWS);
                float3 positionDxPerpendicular =
                    cross((float3)normalWS, positionDx);
                float determinant =
                    dot(positionDx, positionDyPerpendicular);
                float inverseDeterminant =
                    (determinant < 0.0 ? -1.0 : 1.0) /
                    max(abs(determinant), 0.000001);
                float3 surfaceGradient =
                    (positionDyPerpendicular * heightDx +
                     positionDxPerpendicular * heightDy) *
                    inverseDeterminant;
                return normalize(
                    (float3)normalWS -
                    surfaceGradient * strength);
            }

            void SampleTriplanar(
                float3 positionWS,
                half3 geometricNormalWS,
                out half3 albedo,
                out half3 sampledNormalWS)
            {
                half3 weights =
                    pow(
                        max(
                            abs(geometricNormalWS),
                            half3(0.0001h, 0.0001h, 0.0001h)),
                        _TriplanarSharpness);
                weights /= max(
                    weights.x + weights.y + weights.z,
                    0.0001h);
                half3 axisSign =
                    step(
                        half3(0.0h, 0.0h, 0.0h),
                        geometricNormalWS) *
                    2.0h -
                    1.0h;
                float2 uvX =
                    float2(
                        positionWS.z,
                        -positionWS.y * axisSign.x) *
                    _TileScale;
                float2 uvY =
                    float2(
                        positionWS.x,
                        -positionWS.z * axisSign.y) *
                    _TileScale;
                float2 uvZ =
                    float2(
                        positionWS.x,
                        positionWS.y * axisSign.z) *
                    _TileScale;

                half3 alongX =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        uvX).rgb;
                half3 alongY =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        uvY).rgb;
                half3 alongZ =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        uvZ).rgb;
                albedo =
                    (alongX * weights.x +
                     alongY * weights.y +
                     alongZ * weights.z) *
                    _Tint.rgb;

                half3 tangentX =
                    UnpackNormalScale(
                        SAMPLE_TEXTURE2D(
                            _NormalMap,
                            sampler_NormalMap,
                            uvX),
                        _NormalStrength);
                half3 tangentY =
                    UnpackNormalScale(
                        SAMPLE_TEXTURE2D(
                            _NormalMap,
                            sampler_NormalMap,
                            uvY),
                        _NormalStrength);
                half3 tangentZ =
                    UnpackNormalScale(
                        SAMPLE_TEXTURE2D(
                            _NormalMap,
                            sampler_NormalMap,
                            uvZ),
                        _NormalStrength);
                half3 normalX =
                    half3(
                        axisSign.x * tangentX.z,
                        -axisSign.x * tangentX.y,
                        tangentX.x);
                half3 normalY =
                    half3(
                        tangentY.x,
                        axisSign.y * tangentY.z,
                        -axisSign.y * tangentY.y);
                half3 normalZ =
                    half3(
                        tangentZ.x,
                        axisSign.z * tangentZ.y,
                        axisSign.z * tangentZ.z);
                sampledNormalWS =
                    normalize(
                        normalX * weights.x +
                        normalY * weights.y +
                        normalZ * weights.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Move only the vertices: the renderer transform stays at its
                // baked position, so its lightmap association remains valid.
                float3 positionWS =
                    TransformObjectToWorld(input.positionOS.xyz) +
                    _CMLHitOffsetWS.xyz;
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS =
                    NormalizeNormalPerVertex(normalInputs.normalWS);
                output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                output.fogFactor =
                    ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 geometricNormalWS =
                    NormalizeNormalPerPixel(input.normalWS);
                half3 albedo;
                half3 sampledNormalWS;
                SampleTriplanar(
                    input.positionWS,
                    geometricNormalWS,
                    albedo,
                    sampledNormalWS);
                half3 normalWS =
                    normalize(
                        lerp(
                            geometricNormalWS,
                            sampledNormalWS,
                            _NormalStrength));

                half horizontalNormalSum =
                    abs(geometricNormalWS.x) +
                    abs(geometricNormalWS.z);
                half facesX =
                    abs(geometricNormalWS.x) /
                    max(horizontalNormalSum, 0.0001h);
                half largeZ =
                    (half)ValueNoise2D(
                        input.positionWS.xy *
                        float2(0.055, 0.045) +
                        float2(11.7, 38.2));
                half largeX =
                    (half)ValueNoise2D(
                        input.positionWS.zy *
                        float2(0.055, 0.045) +
                        float2(11.7, 38.2));
                half mediumZ =
                    (half)ValueNoise2D(
                        input.positionWS.xy *
                        float2(0.14, 0.10) +
                        float2(57.9, 3.6));
                half mediumX =
                    (half)ValueNoise2D(
                        input.positionWS.zy *
                        float2(0.14, 0.10) +
                        float2(57.9, 3.6));
                half runoffZ =
                    (half)ValueNoise2D(
                        input.positionWS.xy *
                        float2(0.24, 0.028) +
                        float2(93.4, 21.8));
                half runoffX =
                    (half)ValueNoise2D(
                        input.positionWS.zy *
                        float2(0.24, 0.028) +
                        float2(93.4, 21.8));
                half large = lerp(largeZ, largeX, facesX);
                half medium = lerp(mediumZ, mediumX, facesX);
                half runoff = lerp(runoffZ, runoffX, facesX);
                half macro =
                    (large * 2.0h - 1.0h) * 0.70h +
                    (medium * 2.0h - 1.0h) * 0.30h;
                albedo *=
                    max(
                        0.72h,
                        1.0h +
                        macro * _MacroVariation +
                        (runoff * 2.0h - 1.0h) *
                        _RunoffVariation);
                albedo *= _Brightness;
                albedo = ApplyCliffPalette(albedo);
                half cavity =
                    smoothstep(
                        0.20h,
                        0.88h,
                        saturate(
                            0.46h -
                            macro * 0.82h +
                            (0.48h - runoff) * 0.22h));
                albedo =
                    lerp(
                        albedo,
                        _CliffCavityColor.rgb,
                        cavity * _CliffCavityStrength);
                normalWS =
                    ApplyHeightNormal(
                        input.positionWS,
                        normalWS,
                        macro * 0.82h +
                        (runoff * 2.0h - 1.0h) * 0.18h,
                        _CliffReliefNormalStrength);

                Light mainLight = GetMainLight(input.shadowCoord);
                half nDotL =
                    saturate(dot(normalWS, mainLight.direction));
                half wrappedLight =
                    smoothstep(0.02h, 0.92h, nDotL);
                half direct =
                    lerp(_ShadowFloor, 1.0h, wrappedLight) *
                    mainLight.shadowAttenuation *
                    mainLight.distanceAttenuation;
                half3 ambient =
                    max(SampleSH(normalWS), 0.0h);
                half3 color =
                    albedo *
                    (ambient * _AmbientStrength +
                     mainLight.color * direct);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack "Universal Render Pipeline/Lit"
}
