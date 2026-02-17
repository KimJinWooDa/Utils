using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TelleR
{
    public class DeviceManager : MonoBehaviour
    {
        [Serializable]
        public class DeviceProfile
        {
            [Tooltip("MSAA Sample Count (1 = Off, 2 = 2x, 4 = 4x, 8 = 8x)")]
            public MsaaQuality msaa = MsaaQuality.Disabled;
        }

        public enum MsaaQuality
        {
            Disabled = 1,
            _2x = 2,
            _4x = 4,
            _8x = 8
        }

        [Header("Quest Device Profiles")]
        [SerializeField] private DeviceProfile quest2 = new() { msaa = MsaaQuality.Disabled };
        [SerializeField] private DeviceProfile quest3 = new() { msaa = MsaaQuality._2x };
        [SerializeField] private DeviceProfile fallback = new() { msaa = MsaaQuality._2x };

        private void Start()
        {
            if (Application.platform != RuntimePlatform.Android) return;

            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null)
            {
                Debug.LogWarning("URP RenderPipelineAsset not found. Ensure URP is active.");
                return;
            }

            string deviceName = SystemInfo.deviceName;
            if (string.IsNullOrEmpty(deviceName))
            {
                Debug.LogWarning("Device name not available.");
                return;
            }

            if (deviceName.Contains("Quest 2"))
            {
                urpAsset.msaaSampleCount = (int)quest2.msaa;
                Debug.Log($"<color=red>Quest 2 detected - MSAA set to {quest2.msaa}.</color>");
            }
            else if (deviceName.Contains("Quest 3"))
            {
                urpAsset.msaaSampleCount = (int)quest3.msaa;
                Debug.Log($"<color=red>Quest 3 series detected - MSAA set to {quest3.msaa}.</color>");
            }
            else
            {
                urpAsset.msaaSampleCount = (int)fallback.msaa;
                Debug.Log($"<color=red>Device is not Quest 2/3 series - MSAA set to {fallback.msaa}.</color>");
            }
        }
    }
}
