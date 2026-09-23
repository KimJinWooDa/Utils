using System;
using UnityEngine;
using UnityEngine.Rendering;
#if TELLER_URP
using UnityEngine.Rendering.Universal;
#endif

namespace TelleR
{
    [AddComponentMenu("TelleR/Device Manager")]
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

        private enum QuestModel
        {
            Other,
            Quest2,
            Quest3
        }

        /// <summary>
        /// 모델명(예: "Oculus Quest 2", "Oculus Quest 3S")을 우선 판별하고, 모델명으로 알 수 없을 때만 기기 이름을 본다.
        /// Meta SDK에 의존하지 않도록 문자열만 사용한다.
        /// </summary>
        private static QuestModel Classify(string deviceModel, string deviceName)
        {
            QuestModel byModel = ClassifyString(deviceModel);
            return byModel != QuestModel.Other ? byModel : ClassifyString(deviceName);
        }

        private static QuestModel ClassifyString(string s)
        {
            if (string.IsNullOrEmpty(s)) return QuestModel.Other;
            if (s.IndexOf("Quest 2", StringComparison.OrdinalIgnoreCase) >= 0) return QuestModel.Quest2;
            if (s.IndexOf("Quest 3", StringComparison.OrdinalIgnoreCase) >= 0) return QuestModel.Quest3; // 3, 3S 포함
            return QuestModel.Other;
        }

        [Header("Quest Device Profiles")]
        [SerializeField] private DeviceProfile quest2 = new() { msaa = MsaaQuality.Disabled };
        [SerializeField] private DeviceProfile quest3 = new() { msaa = MsaaQuality._2x };
        [SerializeField] private DeviceProfile fallback = new() { msaa = MsaaQuality._2x };

        private void Start()
        {
#if !TELLER_URP
            // URP 패키지 미설치 프로젝트에서도 패키지 전체가 컴파일되도록 가드 (versionDefines로 정의됨)
            Debug.LogWarning("[TelleR/DeviceManager] URP 패키지가 없어 MSAA 프로필을 적용하지 않습니다.");
#else
            if (Application.platform != RuntimePlatform.Android) return;

            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null)
            {
                Debug.LogWarning("[TelleR/DeviceManager] 활성 렌더 파이프라인이 URP가 아니라 MSAA 프로필을 적용하지 않습니다.");
                return;
            }

            // deviceName은 사용자가 바꿀 수 있는 기기 이름이라 모델명(deviceModel)을 먼저 보고, 이름은 보조로만 쓴다.
            string deviceModel = SystemInfo.deviceModel;
            string deviceName = SystemInfo.deviceName;
            QuestModel model = Classify(deviceModel, deviceName);

            DeviceProfile profile = model == QuestModel.Quest2 ? quest2 : model == QuestModel.Quest3 ? quest3 : fallback;
            urpAsset.msaaSampleCount = (int)profile.msaa;

            string label = model == QuestModel.Quest2 ? "Quest 2" : model == QuestModel.Quest3 ? "Quest 3 시리즈" : "Quest 2/3 외 기기";
            Debug.Log($"[TelleR/DeviceManager] {label} 감지 (model: {deviceModel}, name: {deviceName}) - MSAA를 {profile.msaa}로 설정했습니다.");
#endif
        }
    }
}
