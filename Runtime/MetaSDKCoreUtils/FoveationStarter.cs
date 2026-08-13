using UnityEngine;
#if TELLER_XR && UNITY_2022_2_OR_NEWER
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR;
#endif

namespace TelleR
{
    public class FoveationStarter : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Foveated Rendering Level\n" +
                 "0.0 = None (전체 해상도, 성능 절약 없음)\n" +
                 "0.33 = Low (주변부 약간 낮은 해상도)\n" +
                 "0.67 = Medium (주변부 중간 수준으로 해상도 감소)\n" +
                 "1.0 = High (주변부 최대 해상도 감소, 최고 성능)")]
        private float foveatedRenderingLevel = 1.0f;

#if TELLER_XR && UNITY_2022_2_OR_NEWER
        void Start()
        {
            StartCoroutine(InitializeFoveation());
        }

        IEnumerator InitializeFoveation()
        {
            List<XRDisplaySubsystem> xrDisplays = new List<XRDisplaySubsystem>();

            yield return null;
            yield return null;

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
        }
#else
        // XR 모듈 미포함 프로젝트(비 VR)나 Foveation API가 없는 Unity 2022.2 미만에서도 컴파일되도록 가드 (TELLER_XR은 asmdef versionDefines로 정의됨)
        void Start()
        {
            Debug.LogWarning($"[FoveationStarter] XR 모듈이 없거나 Unity 2022.2 미만이라 Foveated Rendering(레벨 {foveatedRenderingLevel})을 적용하지 않습니다.");
        }
#endif
    }
}
