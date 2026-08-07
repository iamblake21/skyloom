Shader "CML/Cinematics/Star Streak"
{
    Properties
    {
        _Color("Tint", Color) = (0.72, 0.88, 1, 1)
        _CoreBoost("Core Boost", Range(0, 8)) = 2.4
        _Softness("Softness", Range(0.5, 12)) = 5.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "StarStreak"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _CoreBoost;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Velocity-stretched billboards arrive with the long axis on U.
                // Tapering both ends keeps a streak from looking like a bar.
                float along = saturate(input.uv.x);
                float across = abs(input.uv.y - 0.5) * 2.0;
                float taper = sin(along * 3.14159265);
                taper = pow(max(taper, 0.0), 0.65);

                float lateral = exp(
                    -(across * across) / max(taper * taper, 1e-4) * _Softness);
                float head = pow(along, 2.2);

                float energy = lateral * (0.35 + head * _CoreBoost * 0.35);
                float3 color = _Color.rgb * input.color.rgb
                    * energy
                    * input.color.a
                    * _Color.a;

                return half4(max(color, 0.0), 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
