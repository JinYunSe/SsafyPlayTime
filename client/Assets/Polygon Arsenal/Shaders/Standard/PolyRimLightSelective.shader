Shader "PolygonArsenal/PolyRimLightSelective"
{
    Properties
    {
        _InnerColor ("Inner Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _RimColor ("Rim Color", Color) = (0.26, 0.19, 0.16, 0.0)
        _RimWidth ("Rim Width", Range(0.2, 20.0)) = 3.0
        _RimGlow ("Rim Glow Multiplier", Range(0.0, 9.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _InnerColor;
                float4 _RimColor;
                float _RimWidth;
                float _RimGlow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float4 vertexColor : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(positionWS);
                OUT.vertexColor = IN.color;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);

                float rim = 1.0 - saturate(dot(viewDirWS, normalWS));
                float rimTerm = pow(rim, _RimWidth);

                float3 baseColor = IN.vertexColor.rgb;
                float3 rimColor = _RimColor.rgb * _RimGlow * rimTerm;
                float3 finalColor = baseColor + rimColor;

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
