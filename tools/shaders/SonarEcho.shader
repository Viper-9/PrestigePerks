// 음파 탐지(Sonar Pulse) 잔상 셰이더. 파동이 닿은 순간 대상의 자세를 본뜬 메시(BakeMesh)에 쓴다 (시험 셰이더 PulseTest/Echo 를 옮김).
// 잔상은 세상 안의 보통 물체라 메인 카메라와 PiP 조준경 카메라가 각자 그린다.
// 벽 너머로 보이게 ZTest Always. 면이 시선과 나란한 가장자리일수록 진하게 = 테두리처럼 보인다.
// 메인 카메라에서만 (_SonarUseDepth, _SonarLens 는 카메라마다 C# 이 전역 값으로 넣음):
//   - 가까운 물체(내 총·조준경 몸통, _SonarNearCut m 안) 뒤에서는 그리지 않는다
//   - PiP 렌즈 영역(_SonarLens = 뷰포트 타원 중심 xy, 반지름 zw)에서는 그리지 않는다. 렌즈 안은 조준경 카메라가 확대된 잔상을 그린다
Shader "PrestigePerks/SonarEcho"
{
    Properties
    {
        _Color ("Rim color", Color) = (0.86, 0.88, 0.25, 1)
        _FillAlpha ("Fill alpha", Float) = 0.045
        _RimPower ("Rim power", Float) = 4
        _Intensity ("Intensity", Float) = 1
        _Fade ("Fade", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZTest Always ZWrite Off Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _Color;
            float _FillAlpha;
            float _RimPower;
            float _Intensity;
            float _Fade;
            float _SonarUseDepth;
            float _SonarNearCut;
            float _SonarLensFlip;
            float4 _SonarLens;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 n : TEXCOORD0;
                float3 v : TEXCOORD1;
                float4 sp : TEXCOORD2;
                float z : TEXCOORD3;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = _WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz;
                o.sp = ComputeScreenPos(o.pos);
                o.z = -UnityObjectToViewPos(v.vertex).z;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.sp.xy / i.sp.w;
                if (_SonarLens.z > 0)
                {
                    float2 q = float2(uv.x, _SonarLensFlip > 0.5 ? 1 - uv.y : uv.y);
                    float2 d = (q - _SonarLens.xy) / _SonarLens.zw;
                    if (dot(d, d) < 1) discard;
                }
                if (_SonarUseDepth > 0.5)
                {
                    // 분기 안이라 기울기 없는 LOD 읽기
                    float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0)));
                    if (scene < _SonarNearCut && scene < i.z) discard;
                }
                float rim = pow(1 - saturate(abs(dot(normalize(i.n), normalize(i.v)))), _RimPower);
                float a = lerp(_FillAlpha, _Color.a, rim) * _Fade;
                return float4(_Color.rgb * _Intensity, a);
            }
            ENDCG
        }
    }
}
