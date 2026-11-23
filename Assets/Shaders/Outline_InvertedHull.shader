// ★ 새 파일: 상호작용 물체 외곽선용 셰이더 (URP, Inverted Hull 방식)
//
// Inverted Hull(뒤집힌 껍데기) 외곽선이란?
//  1) 물체의 모든 꼭짓점(정점)을 "법선 방향(표면이 바깥을 향하는 방향)"으로 조금 밀어서
//     원래보다 살짝 큰 껍데기를 만든다.
//  2) 그 껍데기의 "앞면은 버리고 뒷면만" 단색으로 그린다. (Cull Front)
//  3) 원래 물체가 껍데기 안쪽을 가리기 때문에, 화면에는 가장자리에 삐져나온 테두리만 보인다.
//
// 이 셰이더는 Interactor.cs가 Graphics.RenderMesh()로 "한 번 더 그릴 때"만 쓴다.
// 원래 물체의 머티리얼에는 전혀 들어가지 않는다.

// 머티리얼을 만들 때 Shader 드롭다운에 보이는 이름 (PS = 팀 이름)
Shader "PS/Outline_InvertedHull"
{
    // Properties: 머티리얼 Inspector에 보이는 칸들.
    // Interactor가 실행 중에 MaterialPropertyBlock으로 덮어쓰므로,
    // 여기 값은 "기본값" 역할만 한다.
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.85, 0.2, 1)        // 기본: 노란색
        _OutlineWidth ("Outline Width (m)", Range(0, 0.05)) = 0.01         // 기본: 1cm
    }

    SubShader
    {
        // RenderPipeline = UniversalPipeline: URP에서만 이 SubShader를 쓴다는 표시
        // Queue = Geometry: 일반 불투명 물체와 같은 순서로 그린다
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Outline"

            // LightMode = UniversalForwardOnly:
            // URP 렌더러가 Forward / Forward+ / Deferred 중 무엇이든 이 패스를 그려 준다.
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Front    // 앞면은 그리지 않고 뒷면만 그린다 → Inverted Hull의 핵심
            ZWrite On     // 깊이를 기록한다 (안 하면 나중에 그려지는 하늘(Skybox)이 외곽선을 덮어 버림)
            ZTest LEqual  // 다른 물체보다 뒤에 있으면 가려진다 (벽 너머로 보이지 않음)

            HLSLPROGRAM
            #pragma vertex vert      // 정점 셰이더 함수 이름
            #pragma fragment frag    // 픽셀(프래그먼트) 셰이더 함수 이름

            // URP 기본 함수들(TransformObjectToWorld 등)이 들어 있는 파일
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 머티리얼 속성은 UnityPerMaterial 묶음 안에 선언한다. (URP 규칙, SRP Batcher 호환)
            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            // 메시에서 읽어 오는 정점 정보
            struct Attributes
            {
                float4 positionOS : POSITION;   // OS = Object Space(물체 기준 좌표)
                float3 normalOS   : NORMAL;     // 법선(표면이 바라보는 방향)
            };

            // 정점 셰이더 → 픽셀 셰이더로 넘기는 정보
            struct Varyings
            {
                float4 positionCS : SV_POSITION; // CS = Clip Space(화면에 찍기 직전 좌표)
            };

            // 정점 셰이더: 꼭짓점마다 한 번 실행. 여기서 껍데기를 부풀린다.
            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // 1) 물체 좌표 → 월드 좌표 (위치와 법선 모두)
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS); // 길이 1로 정규화되어 나옴

                // 2) 월드 공간에서 법선 방향으로 _OutlineWidth(미터)만큼 민다.
                //    월드 공간에서 밀기 때문에, 물체의 Scale이 커도 두께는 항상 같은 미터 값이다.
                positionWS += normalWS * _OutlineWidth;

                // 3) 월드 좌표 → 화면용 좌표
                OUT.positionCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            // 픽셀 셰이더: 화면 픽셀마다 실행. 그냥 외곽선 색 하나로 칠한다. (빛 계산 없음)
            half4 frag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
