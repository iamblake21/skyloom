Shader "CML/Environment/Starter Island Stylized Surface"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.47, 0.73, 0.31, 1)
        _SecondaryColor("Secondary Color", Color) = (0.78, 0.64, 0.42, 1)
        _WetColor("Wet Color", Color) = (0.18, 0.40, 0.34, 1)
        _VertexBlend("Use Vertex Blend", Range(0, 1)) = 0
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.72
        _ShadowFloor("Shadow Floor", Range(0, 1)) = 0.38
        _ColorVariation("World Color Variation", Range(0, 0.2)) = 0.035
        _RockDetail("Rock Surface Detail", Range(0, 1)) = 0
        _RockTopColor("Rock Lit Top Color", Color) = (0.82, 0.79, 0.73, 1)
        _RockUnderColor("Rock Underside Color", Color) = (0.42, 0.46, 0.43, 1)
        _RockTopStrength("Rock Top Strength", Range(0, 1)) = 0.62
        _RockUnderStrength("Rock Underside Strength", Range(0, 1)) = 0.34
        _RockMacroScale("Rock Macro Scale", Range(0.05, 2)) = 0.42
        _RockMacroStrength("Rock Macro Strength", Range(0, 0.3)) = 0.12
        _RockGrainScale("Rock Grain Scale", Range(0.5, 12)) = 4.2
        _RockGrainStrength("Rock Grain Strength", Range(0, 0.2)) = 0.055
        _RockContactBlend("Rock Terrain Contact", Range(0, 1)) = 0
        _RockContactHeight("Rock Contact Height", Range(-0.5, 1.5)) = 0.22
        _RockContactFeather("Rock Contact Feather", Range(0.01, 0.8)) = 0.18
        _RockContactNoise("Rock Contact Irregularity", Range(0, 0.5)) = 0.12
        _RockContactGrassColor("Rock Grass Contact", Color) = (0.25, 0.39, 0.18, 1)
        _RockContactDeepGrassColor("Rock Deep Grass Contact", Color) = (0.16, 0.29, 0.15, 1)
        _RockContactDirtColor("Rock Dirt Contact", Color) = (0.66, 0.49, 0.31, 1)
        _RockContactCliffColor("Rock Cliff Contact", Color) = (0.53, 0.30, 0.23, 1)
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

            TEXTURE2D(_CMLTerrainBlendControl);
            SAMPLER(sampler_CMLTerrainBlendControl);
            float4 _CMLTerrainBlendOriginInvSize;
            float _CMLTerrainBlendEnabled;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _SecondaryColor;
                half4 _WetColor;
                half _VertexBlend;
                half _AmbientStrength;
                half _ShadowFloor;
                half _ColorVariation;
                half _RockDetail;
                half4 _RockTopColor;
                half4 _RockUnderColor;
                half _RockTopStrength;
                half _RockUnderStrength;
                half _RockMacroScale;
                half _RockMacroStrength;
                half _RockGrainScale;
                half _RockGrainStrength;
                half _RockContactBlend;
                half _RockContactHeight;
                half _RockContactFeather;
                half _RockContactNoise;
                half4 _RockContactGrassColor;
                half4 _RockContactDeepGrassColor;
                half4 _RockContactDirtColor;
                half4 _RockContactCliffColor;
                float4 _CMLHitOffsetWS;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                float4 shadowCoord : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                float localHeight : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

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
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.color = input.color;
                output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                output.fogFactor =
                    ComputeFogFactor(output.positionCS.z);
                output.localHeight = input.positionOS.y;
                return output;
            }

            // Stable object-independent value noise. Rock meshes do not need
            // authored UVs and differently scaled instances do not reveal a
            // repeated bitmap pattern.
            float Hash31(float3 samplePosition)
            {
                samplePosition = frac(samplePosition * 0.1031);
                samplePosition +=
                    dot(
                        samplePosition,
                        samplePosition.yzx + 33.33);
                return frac(
                    (samplePosition.x + samplePosition.y) *
                    samplePosition.z);
            }

            float ValueNoise(float3 samplePosition)
            {
                float3 cell = floor(samplePosition);
                float3 local = frac(samplePosition);
                float3 blend = local * local * (3.0 - 2.0 * local);

                float x00 = lerp(
                    Hash31(cell + float3(0.0, 0.0, 0.0)),
                    Hash31(cell + float3(1.0, 0.0, 0.0)),
                    blend.x);
                float x10 = lerp(
                    Hash31(cell + float3(0.0, 1.0, 0.0)),
                    Hash31(cell + float3(1.0, 1.0, 0.0)),
                    blend.x);
                float x01 = lerp(
                    Hash31(cell + float3(0.0, 0.0, 1.0)),
                    Hash31(cell + float3(1.0, 0.0, 1.0)),
                    blend.x);
                float x11 = lerp(
                    Hash31(cell + float3(0.0, 1.0, 1.0)),
                    Hash31(cell + float3(1.0, 1.0, 1.0)),
                    blend.x);

                return lerp(
                    lerp(x00, x10, blend.y),
                    lerp(x01, x11, blend.y),
                    blend.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);

                half grassWeight = saturate(input.color.r);
                half wetWeight = saturate(input.color.a);
                half3 authoredBlend =
                    lerp(_SecondaryColor.rgb, _BaseColor.rgb, grassWeight);
                half3 baseColor =
                    lerp(_BaseColor.rgb, authoredBlend, _VertexBlend);
                baseColor =
                    lerp(baseColor, _WetColor.rgb, wetWeight * _VertexBlend * 0.46h);

                half variation =
                    sin(input.positionWS.x * 0.087h + input.positionWS.z * 0.061h)
                    * sin(input.positionWS.x * 0.031h - input.positionWS.z * 0.109h);
                baseColor *= 1.0h + variation * _ColorVariation;

                // The reference stone is neutral grey. Its warm bright areas
                // come from upward-facing facets and the scene light, while
                // downward-facing facets retain a cooler ground/grass bounce.
                // Two world-space noise bands break the flat fill without
                // turning the low-poly surface into photographic stone.
                half topWeight =
                    smoothstep(0.18h, 0.88h, saturate(normalWS.y));
                half underWeight =
                    smoothstep(0.05h, 0.76h, saturate(-normalWS.y));
                half macroNoise =
                    (half)ValueNoise(input.positionWS * _RockMacroScale);
                half grainNoise =
                    (half)ValueNoise(
                        input.positionWS * _RockGrainScale +
                        float3(17.2, 5.7, 11.9));
                half rockVariation =
                    (macroNoise * 2.0h - 1.0h) * _RockMacroStrength +
                    (grainNoise * 2.0h - 1.0h) * _RockGrainStrength;
                half3 rockColor = baseColor * (1.0h + rockVariation);
                rockColor = lerp(
                    rockColor,
                    _RockTopColor.rgb * (1.0h + rockVariation * 0.35h),
                    topWeight * _RockTopStrength);
                rockColor = lerp(
                    rockColor,
                    _RockUnderColor.rgb * (1.0h + rockVariation * 0.22h),
                    underWeight * _RockUnderStrength);

                // Read the very same RGBA alphamap used by the Unity Terrain:
                // R/G are the two grass layers, B is the path/dirt layer and
                // A is cliff. Local mesh height limits the transfer to the
                // buried base; world noise makes the boundary organic.
                float2 terrainUv =
                    (input.positionWS.xz -
                     _CMLTerrainBlendOriginInvSize.xy) *
                    _CMLTerrainBlendOriginInvSize.zw;
                half terrainBounds =
                    step(0.0h, terrainUv.x) *
                    step(terrainUv.x, 1.0h) *
                    step(0.0h, terrainUv.y) *
                    step(terrainUv.y, 1.0h);
                half4 terrainWeights =
                    SAMPLE_TEXTURE2D(
                        _CMLTerrainBlendControl,
                        sampler_CMLTerrainBlendControl,
                        saturate(terrainUv));
                terrainWeights /=
                    max(
                        dot(
                            terrainWeights,
                            half4(1.0h, 1.0h, 1.0h, 1.0h)),
                        0.0001h);
                half3 terrainContactColor =
                    _RockContactGrassColor.rgb * terrainWeights.r +
                    _RockContactDeepGrassColor.rgb * terrainWeights.g +
                    _RockContactDirtColor.rgb * terrainWeights.b +
                    _RockContactCliffColor.rgb * terrainWeights.a;
                half contactEdgeNoise =
                    ((half)ValueNoise(
                        input.positionWS * 1.37 +
                        float3(4.1, 23.7, 8.9)) -
                     0.5h) *
                    _RockContactNoise;
                half contactMask =
                    1.0h -
                    smoothstep(
                        _RockContactHeight - _RockContactFeather,
                        _RockContactHeight + _RockContactFeather,
                        input.localHeight + contactEdgeNoise);
                contactMask *=
                    terrainBounds *
                    saturate(_CMLTerrainBlendEnabled) *
                    _RockContactBlend;
                rockColor =
                    lerp(
                        rockColor,
                        terrainContactColor *
                        (0.93h + macroNoise * 0.14h),
                        contactMask);
                baseColor = lerp(baseColor, rockColor, _RockDetail);

                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half wrappedLight = smoothstep(0.02h, 0.92h, nDotL);
                half direct =
                    lerp(_ShadowFloor, 1.0h, wrappedLight)
                    * mainLight.shadowAttenuation
                    * mainLight.distanceAttenuation;
                half3 ambient = max(SampleSH(normalWS), 0.0h);
                half3 color =
                    baseColor
                    * (ambient * _AmbientStrength + mainLight.color * direct);

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
