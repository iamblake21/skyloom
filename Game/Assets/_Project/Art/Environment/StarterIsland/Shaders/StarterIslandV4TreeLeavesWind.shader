Shader "CML/Environment/Starter Island V4 Tree Leaves"
{
    Properties
    {
        [MainTexture] _BaseMap("Leaf Atlas", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.45

        _Smoothness("Smoothness", Range(0, 1)) = 0.18
        _Metallic("Metallic", Range(0, 1)) = 0
        _BumpScale("Normal Strength", Range(0, 2)) = 0.65
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}

        _WindDirection("Wind Direction", Vector) = (0.82, 0, 0.57, 0)
        _WindStrength("Canopy Sway", Range(0, 0.6)) = 0.24
        _WindSpeed("Wind Speed", Range(0, 4)) = 0.82
        _WindGustStrength("Gust Variation", Range(0, 1)) = 0.38
        _WindFlutterStrength("Leaf Flutter", Range(0, 0.15)) = 0.045
        _WindBaseHeight("Wind Base Height", Float) = 0.75
        _WindHeight("Wind Height Range", Float) = 9.5

        // Kept for compatibility with the URP material inspector and the
        // existing V4 tree setup/validation boundary.
        [HideInInspector] _Surface("__surface", Float) = 0
        [HideInInspector] _Blend("__blend", Float) = 0
        [HideInInspector] _Cull("__cull", Float) = 0
        [HideInInspector] _AlphaClip("__clip", Float) = 1
        [HideInInspector] _SrcBlend("__src", Float) = 1
        [HideInInspector] _DstBlend("__dst", Float) = 0
        [HideInInspector] _ZWrite("__zw", Float) = 1
        [HideInInspector] _ReceiveShadows("Receive Shadows", Float) = 1
        [HideInInspector] _QueueOffset("Queue Offset", Float) = 0
        [HideInInspector] _MainTex("Base Map", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);
    TEXTURE2D(_BumpMap);
    SAMPLER(sampler_BumpMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _Cutoff;
        half _Smoothness;
        half _Metallic;
        half _BumpScale;
        float4 _WindDirection;
        half _WindStrength;
        half _WindSpeed;
        half _WindGustStrength;
        half _WindFlutterStrength;
        half _WindBaseHeight;
        half _WindHeight;
    CBUFFER_END

    struct WindAttributes
    {
        float4 positionOS : POSITION;
        half3 normalOS : NORMAL;
        half4 tangentOS : TANGENT;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct WindVaryings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        half3 normalWS : TEXCOORD1;
        half4 tangentWS : TEXCOORD2;
        float2 uv : TEXCOORD3;
        half fogFactor : TEXCOORD4;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // All passes call this exact deformation function. The visible leaves,
    // their depth and their projected shadows therefore remain coincident.
    float3 GetWindPositionWS(float3 positionOS, float2 uv)
    {
        float3 positionWS = TransformObjectToWorld(positionOS);
        float3 objectOriginWS =
            TransformObjectToWorld(float3(0.0, 0.0, 0.0));

        float2 direction = _WindDirection.xz;
        direction *= rsqrt(max(dot(direction, direction), 0.0001));
        float2 crossDirection = float2(-direction.y, direction.x);

        half heightWeight = smoothstep(
            _WindBaseHeight,
            _WindBaseHeight + max(_WindHeight, 0.001h),
            positionOS.y);

        // Object phase prevents the whole forest from moving in lockstep,
        // while the low spatial frequencies keep each canopy cohesive.
        half objectPhase =
            dot(objectOriginWS.xz, float2(0.037h, 0.053h));
        half travelPhase =
            dot(positionWS.xz, float2(0.063h, 0.041h));
        half mainSway = sin(
            _Time.y * _WindSpeed + travelPhase + objectPhase);
        half gust = sin(
            _Time.y * (_WindSpeed * 0.37h) +
            objectPhase * 1.73h +
            travelPhase * 0.23h);
        half flutter = sin(
            _Time.y * (_WindSpeed * 2.8h) +
            positionOS.x * 1.31h +
            positionOS.z * 1.67h +
            (uv.x + uv.y) * 5.1h +
            objectPhase);

        half swayAmount =
            (mainSway + gust * _WindGustStrength) *
            _WindStrength *
            heightWeight;
        half flutterAmount =
            flutter * _WindFlutterStrength * heightWeight;

        positionWS.xz += direction * swayAmount;
        positionWS.xz += crossDirection * flutterAmount;
        positionWS.y +=
            abs(mainSway) *
            _WindStrength *
            heightWeight *
            0.035h;
        return positionWS;
    }

    half SampleLeafAlpha(float2 uv)
    {
        return SAMPLE_TEXTURE2D(
            _BaseMap,
            sampler_BaseMap,
            uv).a * _BaseColor.a;
    }

    void ClipLeaf(float2 uv)
    {
        clip(SampleLeafAlpha(uv) - _Cutoff);
    }

    WindVaryings WindForwardVertex(WindAttributes input)
    {
        WindVaryings output = (WindVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        float3 positionWS =
            GetWindPositionWS(input.positionOS.xyz, input.uv);
        half3 normalWS =
            TransformObjectToWorldNormal(input.normalOS);
        half3 tangentWS =
            TransformObjectToWorldDir(input.tangentOS.xyz);
        half tangentSign =
            input.tangentOS.w * GetOddNegativeScale();

        output.positionWS = positionWS;
        output.positionCS = TransformWorldToHClip(positionWS);
        output.normalWS = NormalizeNormalPerVertex(normalWS);
        output.tangentWS = half4(
            normalize(tangentWS),
            tangentSign);
        output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
        output.fogFactor =
            ComputeFogFactor(output.positionCS.z);
        return output;
    }

    half4 WindForwardFragment(
        WindVaryings input,
        bool isFrontFace : SV_IsFrontFace) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        half4 atlas = SAMPLE_TEXTURE2D(
            _BaseMap,
            sampler_BaseMap,
            input.uv);
        clip(atlas.a * _BaseColor.a - _Cutoff);

        half3 normalTS = UnpackNormalScale(
            SAMPLE_TEXTURE2D(
                _BumpMap,
                sampler_BumpMap,
                input.uv),
            _BumpScale);
        half3 normalWS =
            NormalizeNormalPerPixel(input.normalWS);
        half3 tangentWS = normalize(input.tangentWS.xyz);
        half3 bitangentWS =
            input.tangentWS.w *
            cross(normalWS, tangentWS);
        half3x3 tangentToWorld = half3x3(
            tangentWS,
            bitangentWS,
            normalWS);
        normalWS = normalize(
            TransformTangentToWorld(
                normalTS,
                tangentToWorld));
        normalWS *= isFrontFace ? 1.0h : -1.0h;

        SurfaceData surfaceData = (SurfaceData)0;
        surfaceData.albedo = atlas.rgb * _BaseColor.rgb;
        surfaceData.specular = half3(0.04h, 0.04h, 0.04h);
        surfaceData.metallic = _Metallic;
        surfaceData.smoothness = _Smoothness;
        surfaceData.normalTS = normalTS;
        surfaceData.emission = half3(0.0h, 0.0h, 0.0h);
        surfaceData.occlusion = 1.0h;
        surfaceData.alpha = 1.0h;
        surfaceData.clearCoatMask = 0.0h;
        surfaceData.clearCoatSmoothness = 0.0h;

        InputData inputData = (InputData)0;
        inputData.positionWS = input.positionWS;
        inputData.positionCS = input.positionCS;
        inputData.normalWS = normalWS;
        inputData.viewDirectionWS =
            GetWorldSpaceNormalizeViewDir(input.positionWS);
        inputData.shadowCoord =
            TransformWorldToShadowCoord(input.positionWS);
        inputData.fogCoord = input.fogFactor;
        inputData.vertexLighting =
            VertexLighting(input.positionWS, normalWS);
        inputData.bakedGI = SampleSH(normalWS);
        inputData.normalizedScreenSpaceUV =
            GetNormalizedScreenSpaceUV(input.positionCS);
        inputData.shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);
        inputData.tangentToWorld = tangentToWorld;

        half4 color =
            UniversalFragmentPBR(inputData, surfaceData);
        color.rgb = MixFog(color.rgb, input.fogFactor);
        color.a = 1.0h;
        return color;
    }

    struct DepthVaryings
    {
        float4 positionCS : SV_POSITION;
        half3 normalWS : TEXCOORD0;
        float2 uv : TEXCOORD1;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    DepthVaryings WindDepthVertex(WindAttributes input)
    {
        DepthVaryings output = (DepthVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        float3 positionWS =
            GetWindPositionWS(input.positionOS.xyz, input.uv);
        output.positionCS = TransformWorldToHClip(positionWS);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
        return output;
    }

    half4 WindDepthOnlyFragment(DepthVaryings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        ClipLeaf(input.uv);
        return 0.0h;
    }

    half4 WindDepthNormalsFragment(
        DepthVaryings input,
        bool isFrontFace : SV_IsFrontFace) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        ClipLeaf(input.uv);
        half3 normalWS =
            NormalizeNormalPerPixel(input.normalWS);
        normalWS *= isFrontFace ? 1.0h : -1.0h;
        #if defined(_GBUFFER_NORMALS_OCT)
            float2 octNormalWS =
                PackNormalOctQuadEncode(normalWS);
            float2 remappedOctNormalWS =
                saturate(octNormalWS * 0.5 + 0.5);
            return half4(
                PackFloat2To888(remappedOctNormalWS),
                0.0h);
        #else
            return half4(normalWS, 0.0h);
        #endif
    }

    float3 _LightDirection;
    float3 _LightPosition;

    DepthVaryings WindShadowVertex(WindAttributes input)
    {
        DepthVaryings output = (DepthVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        float3 positionWS =
            GetWindPositionWS(input.positionOS.xyz, input.uv);
        half3 normalWS =
            TransformObjectToWorldNormal(input.normalOS);
        float3 lightDirectionWS = _LightDirection;
        #if _CASTING_PUNCTUAL_LIGHT_SHADOW
            lightDirectionWS =
                normalize(_LightPosition - positionWS);
        #endif
        float4 positionCS = TransformWorldToHClip(
            ApplyShadowBias(
                positionWS,
                normalWS,
                lightDirectionWS));
        #if UNITY_REVERSED_Z
            positionCS.z =
                min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
        #else
            positionCS.z =
                max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
        #endif

        output.positionCS = positionCS;
        output.normalWS = normalWS;
        output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
        return output;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WindForwardVertex
            #pragma fragment WindForwardFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WindShadowVertex
            #pragma fragment WindDepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WindDepthVertex
            #pragma fragment WindDepthOnlyFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WindDepthVertex
            #pragma fragment WindDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
