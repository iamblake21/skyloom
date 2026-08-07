Shader "CML/Environment/Starter Island CloudTall Tree"
{
    // The CloudTall trees carry their painted colour in the mesh vertex colour
    // and keep the maps in luminance only, so one shader serves the opaque
    // crown, the opaque trunk and the alpha clipped leaf clusters. Alpha
    // clipping stays on for every material: the opaque maps are fully opaque,
    // and the render queue plus RenderType tag are overridden per material.
    Properties
    {
        [MainTexture] _BaseMap("Luminance Map", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.45

        _Smoothness("Smoothness", Range(0, 1)) = 0.04
        _Metallic("Metallic", Range(0, 1)) = 0

        _WindDirection("Wind Direction", Vector) = (0.82, 0, 0.57, 0)
        _WindStrength("Canopy Sway", Range(0, 0.6)) = 0.24
        _WindSpeed("Wind Speed", Range(0, 4)) = 0.82
        _WindGustStrength("Gust Variation", Range(0, 1)) = 0.38
        _WindFlutterStrength("Leaf Flutter", Range(0, 0.15)) = 0.045
        _WindBaseHeight("Wind Base Height", Float) = 2
        _WindHeight("Wind Height Range", Float) = 8
        [HideInInspector] _CMLHitOffsetWS("Hit Offset", Vector) = (0, 0, 0, 0)

        [HideInInspector] _Surface("__surface", Float) = 0
        [HideInInspector] _Blend("__blend", Float) = 0
        [HideInInspector] _Cull("__cull", Float) = 0
        [HideInInspector] _AlphaClip("__clip", Float) = 1
        [HideInInspector] _SrcBlend("__src", Float) = 1
        [HideInInspector] _DstBlend("__dst", Float) = 0
        [HideInInspector] _ZWrite("__zw", Float) = 1
        [HideInInspector] _ReceiveShadows("Receive Shadows", Float) = 1
        [HideInInspector] _QueueOffset("Queue Offset", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _Cutoff;
        half _Smoothness;
        half _Metallic;
        float4 _WindDirection;
        half _WindStrength;
        half _WindSpeed;
        half _WindGustStrength;
        half _WindFlutterStrength;
        half _WindBaseHeight;
        half _WindHeight;
        float4 _CMLHitOffsetWS;
    CBUFFER_END

    struct WindAttributes
    {
        float4 positionOS : POSITION;
        half3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        float4 chopData : TEXCOORD1;
        half4 color : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct WindVaryings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        half3 normalWS : TEXCOORD1;
        float2 uv : TEXCOORD2;
        half4 color : TEXCOORD3;
        half fogFactor : TEXCOORD4;
        float3 positionOS : TEXCOORD5;
        float4 chopData : TEXCOORD6;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // Every pass calls this exact deformation, so the visible foliage, its
    // depth and its projected shadow stay coincident.
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
        positionWS += _CMLHitOffsetWS.xyz;
        return positionWS;
    }

    half ChopSectorRadius(int sector)
    {
        if (sector == 0) return 0.91h;
        if (sector == 1) return 1.06h;
        if (sector == 2) return 0.94h;
        if (sector == 3) return 1.09h;
        if (sector == 4) return 0.96h;
        if (sector == 5) return 1.03h;
        if (sector == 6) return 0.89h;
        if (sector == 7) return 1.07h;
        if (sector == 8) return 0.93h;
        if (sector == 9) return 1.04h;
        if (sector == 10) return 0.90h;
        return 1.01h;
    }

    half ChopNormalizedRadius(half nx, half ny)
    {
        half endBlend = smoothstep(0.58h, 1.0h, abs(nx));
        half availableHalfHeight = lerp(1.0h, 0.24h, endBlend);
        half shapedY = ny / availableHalfHeight;
        half sectorFloat = clamp(
            (atan2(shapedY, nx) + 3.14159265h) * 1.90985932h,
            0.0h,
            11.9999h);
        int sector = (int)floor(sectorFloat);
        int next = sector == 11 ? 0 : sector + 1;
        half radiusLimit = lerp(
            ChopSectorRadius(sector),
            ChopSectorRadius(next),
            frac(sectorFloat));
        half asymmetricReach = lerp(
            0.90h,
            1.06h,
            saturate(nx * 0.5h + 0.5h));
        return length(half2(nx, shapedY)) /
            (radiusLimit * asymmetricReach);
    }

    half3 ApplyChopAppearance(
        half3 barkAlbedo,
        float4 chopData)
    {
        // Only vertices generated by TreeChopVoxelCarver carry the out-of-band
        // +16 marker. Authored/default canopy vertex data can therefore never
        // enter the wound shading, even when TEXCOORD1 is absent in the FBX.
        if (chopData.w < 16.5h)
        {
            return barkAlbedo;
        }

        half materialId = chopData.w - 16.0h;
        if (chopData.z < -0.05h)
        {
            // Negative voxel depth marks a connected bark tongue. Keeping
            // this flag independent from the interpolated material id makes
            // the small flap remain legible at its tapered edges.
            half flapCore = smoothstep(
                0.12h,
                0.45h,
                saturate(-chopData.z));
            half3 warmUnderside = half3(0.48h, 0.24h, 0.10h);
            half3 barkFace = lerp(
                half3(0.74h, 0.69h, 0.56h),
                barkAlbedo,
                0.45h);
            return saturate(lerp(
                warmUnderside,
                barkFace,
                flapCore));
        }

        if (materialId < 0.5h)
        {
            return barkAlbedo;
        }

        if (materialId > 3.5h)
        {
            // A generated flap keeps the authored bark sample but receives a
            // slight lift in value so its thin, raised silhouette does not
            // disappear when it happens to cross a black birch marking.
            return barkAlbedo;
        }

        half nx = chopData.x;
        half ny = chopData.y;
        half depth01 = saturate(chopData.z);

        // The cambium is a narrow physical wall between unchanged birch bark
        // and generated wood. No spatial mask can bleed onto intact bark:
        // material ids exist only on vertices made by the voxel subtraction.
        half3 cambium = half3(1.00h, 0.70h, 0.39h);
        if (materialId < 1.5h)
        {
            half cambiumFiber =
                sin(nx * 42.0h - ny * 7.0h) * 0.5h + 0.5h;
            half3 cambiumAppearance = cambium *
                lerp(0.98h, 1.02h, cambiumFiber);
            half reveal = smoothstep(0.48h, 1.42h, materialId);
            return lerp(
                barkAlbedo,
                cambiumAppearance,
                reveal * 0.92h);
        }

        // The fresh wood is one continuous material. Geometric voxel depth
        // and short fibres provide variation without painting separate
        // circular stamps or full-width bands on the cut.
        half3 lightWood = half3(1.00h, 0.72h, 0.42h);
        half3 baseWood = half3(0.88h, 0.55h, 0.30h);
        half3 deepWood = half3(0.72h, 0.40h, 0.20h);
        half3 warmWood = half3(0.94h, 0.62h, 0.35h);
        half3 wood = lightWood;
        wood = lerp(wood, baseWood, step(2.10h, materialId));
        wood = lerp(wood, deepWood, step(2.30h, materialId));
        wood = lerp(wood, warmWood, step(2.50h, materialId));
        wood *= lerp(1.05h, 0.98h, depth01);

        // Short, broken fibres only. Their vertical window changes every
        // 8-18 mm, preventing the long uniform pinstripes of the rejected
        // candidate while retaining the fibrous fresh-wood read.
        half fiber =
            sin(nx * 38.0h + sin(ny * 11.0h) * 1.2h) * 0.5h + 0.5h;
        half fiberWindow = smoothstep(
            0.28h,
            0.62h,
            sin(ny * 18.0h - nx * 4.0h) * 0.5h + 0.5h);
        half crossGrain =
            sin(nx * 19.0h - ny * 7.0h) * 0.5h + 0.5h;
        half broadFacet =
            sin(nx * 5.1h + ny * 3.4h +
                sin(ny * 7.0h) * 0.7h) * 0.5h + 0.5h;
        wood = lerp(
            wood * 0.92h,
            wood * 1.10h,
            smoothstep(0.28h, 0.72h, broadFacet));
        wood *= lerp(
            0.96h,
            1.04h,
            lerp(crossGrain, fiber, fiberWindow));

        if (materialId > 2.90h)
        {
            wood = lerp(
                wood,
                half3(0.25h, 0.12h, 0.055h),
                0.38h);
        }

        return wood;
    }

    void ClipSurface(float2 uv, float3 positionOS)
    {
        clip(
            SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a *
            _BaseColor.a - _Cutoff);
    }

    WindVaryings CloudTallForwardVertex(WindAttributes input)
    {
        WindVaryings output = (WindVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        float3 positionWS =
            GetWindPositionWS(input.positionOS.xyz, input.uv);
        output.positionWS = positionWS;
        output.positionCS = TransformWorldToHClip(positionWS);
        output.normalWS = NormalizeNormalPerVertex(
            TransformObjectToWorldNormal(input.normalOS));
        output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
        output.color = input.color;
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
        output.positionOS = input.positionOS.xyz;
        output.chopData = input.chopData;
        return output;
    }

    half4 CloudTallForwardFragment(
        WindVaryings input,
        bool isFrontFace : SV_IsFrontFace) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        half4 map = SAMPLE_TEXTURE2D(
            _BaseMap,
            sampler_BaseMap,
            input.uv);
        clip(map.a * _BaseColor.a - _Cutoff);

        half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
        normalWS *= isFrontFace ? 1.0h : -1.0h;

        SurfaceData surfaceData = (SurfaceData)0;
        // The map carries luminance only; the painted colour lives in the mesh.
        surfaceData.albedo = ApplyChopAppearance(
            map.rgb * input.color.rgb * _BaseColor.rgb,
            input.chopData);
        surfaceData.specular = half3(0.04h, 0.04h, 0.04h);
        surfaceData.metallic = _Metallic;
        surfaceData.smoothness = input.chopData.w > 16.5h
            ? 0.12h
            : _Smoothness;
        surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
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
        inputData.tangentToWorld = half3x3(
            half3(1.0h, 0.0h, 0.0h),
            half3(0.0h, 1.0h, 0.0h),
            normalWS);

        half4 color = UniversalFragmentPBR(inputData, surfaceData);
        color.rgb = MixFog(color.rgb, input.fogFactor);
        color.a = 1.0h;
        return color;
    }

    struct DepthVaryings
    {
        float4 positionCS : SV_POSITION;
        half3 normalWS : TEXCOORD0;
        float2 uv : TEXCOORD1;
        float3 positionOS : TEXCOORD2;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    DepthVaryings CloudTallDepthVertex(WindAttributes input)
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
        output.positionOS = input.positionOS.xyz;
        return output;
    }

    half4 CloudTallDepthOnlyFragment(DepthVaryings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        ClipSurface(input.uv, input.positionOS);
        return 0.0h;
    }

    half4 CloudTallDepthNormalsFragment(
        DepthVaryings input,
        bool isFrontFace : SV_IsFrontFace) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        ClipSurface(input.uv, input.positionOS);
        half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
        normalWS *= isFrontFace ? 1.0h : -1.0h;
        #if defined(_GBUFFER_NORMALS_OCT)
            float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
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

    DepthVaryings CloudTallShadowVertex(WindAttributes input)
    {
        DepthVaryings output = (DepthVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        float3 positionWS =
            GetWindPositionWS(input.positionOS.xyz, input.uv);
        half3 normalWS = TransformObjectToWorldNormal(input.normalOS);
        float3 lightDirectionWS = _LightDirection;
        #if _CASTING_PUNCTUAL_LIGHT_SHADOW
            lightDirectionWS = normalize(_LightPosition - positionWS);
        #endif
        float4 positionCS = TransformWorldToHClip(
            ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
        #if UNITY_REVERSED_Z
            positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
        #else
            positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
        #endif

        output.positionCS = positionCS;
        output.normalWS = normalWS;
        output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
        output.positionOS = input.positionOS.xyz;
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
            #pragma vertex CloudTallForwardVertex
            #pragma fragment CloudTallForwardFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma shader_feature_local_fragment _ _RECEIVE_SHADOWS_OFF
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
            #pragma vertex CloudTallShadowVertex
            #pragma fragment CloudTallDepthOnlyFragment
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
            #pragma vertex CloudTallDepthVertex
            #pragma fragment CloudTallDepthOnlyFragment
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
            #pragma vertex CloudTallDepthVertex
            #pragma fragment CloudTallDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
