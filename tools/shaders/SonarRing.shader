// 음파 탐지(Sonar Pulse) 파동 고리. 내 발 높이의 얇은 평면 고리 메시에 쓴다 (C# 이 파동 반경만큼 키운다).
// 잔상과 같은 색, 가산 섞기. 벽·지형에 가려진다 (ZTest LEqual). _Fade 는 반경에 가까울수록 줄인다.
// 고리 안쪽 가장자리는 투명, 바깥쪽 가장자리가 진하다 (메시 uv.y: 안쪽 0, 바깥쪽 1).
Shader "PrestigePerks/SonarRing"
{
    Properties
    {
        _Color ("Color", Color) = (0.86, 0.88, 0.25, 1)
        _Intensity ("Intensity", Float) = 1
        _Fade ("Fade", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+90" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZTest LEqual ZWrite Off Cull Off
            Blend SrcAlpha One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Intensity;
            float _Fade;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float edge : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.edge = v.texcoord.y;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float a = i.edge * i.edge * _Color.a * _Fade;
                return float4(_Color.rgb * _Intensity, a);
            }
            ENDCG
        }
    }
}
