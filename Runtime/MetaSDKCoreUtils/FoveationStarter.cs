using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

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
    }
}
