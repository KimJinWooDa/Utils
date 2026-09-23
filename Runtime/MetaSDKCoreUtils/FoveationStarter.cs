using UnityEngine;
#if TELLER_XR && UNITY_2022_2_OR_NEWER
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR;
#endif

namespace TelleR
{
    [AddComponentMenu("TelleR/Foveation Starter")]
    public class FoveationStarter : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Foveated Rendering Level\n" +
                 "0.0 = None (전체 해상도, 성능 절약 없음)\n" +
                 "0.33 = Low (주변부 약간 낮은 해상도)\n" +
                 "0.67 = Medium (주변부 중간 수준으로 해상도 감소)\n" +
                 "1.0 = High (주변부 최대 해상도 감소, 최고 성능)")]
        private float foveatedRenderingLevel = 1.0f;

        [SerializeField, Min(0f)]
        [Tooltip("XR 디스플레이가 늦게 초기화되는 경우를 위해 이 시간(초) 동안 재시도합니다.\n시간 안에 실행 중인 XR 디스플레이를 찾지 못하면 경고를 남기고 포기합니다.")]
        private float xrWaitTimeout = 5f;

#if TELLER_XR && UNITY_2022_2_OR_NEWER
        private const float RetryInterval = 0.25f;

        void Start()
        {
            StartCoroutine(InitializeFoveation());
        }

        IEnumerator InitializeFoveation()
        {
            List<XRDisplaySubsystem> xrDisplays = new List<XRDisplaySubsystem>();

            // XR 초기화가 첫 프레임 이후에 끝나는 경우가 많아 최소 2프레임은 기다린다.
            yield return null;
            yield return null;

            // timeScale이 0이어도 진행되도록 실시간 기준으로 재시도
            float deadline = Time.realtimeSinceStartup + xrWaitTimeout;
            WaitForSecondsRealtime wait = new WaitForSecondsRealtime(RetryInterval);
            while (true)
            {
                SubsystemManager.GetSubsystems(xrDisplays);
                foreach (var subsystem in xrDisplays)
                {
                    if (subsystem.running)
                    {
                        subsystem.foveatedRenderingLevel = foveatedRenderingLevel;
                        subsystem.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
                        yield break;
                    }
                }

                if (Time.realtimeSinceStartup >= deadline) break;
                yield return wait;
            }

            Debug.LogWarning($"[TelleR/FoveationStarter] {xrWaitTimeout:0.#}초 동안 실행 중인 XR 디스플레이를 찾지 못해 Foveated Rendering(레벨 {foveatedRenderingLevel})을 적용하지 못했습니다. XR Plug-in Management에서 XR이 초기화되는지 확인하세요.");
        }
#else
        // XR 모듈 미포함 프로젝트(비 VR)나 Foveation API가 없는 Unity 2022.2 미만에서도 컴파일되도록 가드 (TELLER_XR은 asmdef versionDefines로 정의됨)
        void Start()
        {
            Debug.LogWarning($"[TelleR/FoveationStarter] XR 모듈이 없거나 Unity 2022.2 미만이라 Foveated Rendering(레벨 {foveatedRenderingLevel}, 대기 {xrWaitTimeout}초)을 적용하지 않습니다.");
        }
#endif
    }
}
