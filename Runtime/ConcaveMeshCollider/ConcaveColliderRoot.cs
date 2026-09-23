using System;
using System.Collections.Generic;
using UnityEngine;

namespace TelleR
{
    /// <summary>Concave Mesh Collider 생성 결과 측정값. 에디터 보고용 데이터이며 런타임에는 읽기만 한다.</summary>
    [Serializable]
    public sealed class ConcaveColliderMetrics
    {
        public int pieceCount;
        public int boxCount;
        public int sphereCount;
        public int capsuleCount;
        public int hullCount;
        public int bonePieceCount;
        public int totalHullVertices;
        public int sourceMeshCount;
        public int sourceTriangleCount;

        /// <summary>원본 내부 중 콜라이더에 덮인 비율(0~1). 측정하지 못했으면 -1. 정적+스킨드면 더 나쁜 쪽.</summary>
        public float coverage = -1f;

        /// <summary>콜라이더 내부 중 원본 밖인 비율(0~1). 측정하지 못했으면 -1. 정적+스킨드면 더 나쁜 쪽.</summary>
        public float outsideFraction = -1f;

        /// <summary>열린 메시라 측정값이 근사치인지.</summary>
        public bool metricsApproximate;

        public float seconds;
        public string generatedAt = string.Empty;
        public string[] warnings = new string[0];
    }

    /// <summary>
    /// Concave Mesh Collider가 만든 콜라이더 계층을 표시·관리하는 마커. 대상 오브젝트에 붙는다.
    /// 생성에 쓴 설정, 헐 메시 에셋, 생성한 조각(정적 조각 컨테이너 + 본 아래 조각), 끈 기존 콜라이더,
    /// 측정값을 기억해 Rebuild/Remove가 정확히 자기 것만 교체·제거하게 한다.
    /// Update가 없어 런타임 비용이 없고, 빌드에 포함돼도 데이터만 남는다.
    /// </summary>
    [AddComponentMenu("TelleR/Concave Collider Root")]
    [DisallowMultipleComponent]
    public sealed class ConcaveColliderRoot : MonoBehaviour
    {
        /// <summary>정적 조각을 담는 자식 컨테이너 이름.</summary>
        public const string ContainerName = "Concave Colliders";

        public const int CurrentDataVersion = 1;

        [SerializeField, HideInInspector] private int dataVersion = CurrentDataVersion;

        [SerializeField] private GameObject container;
        [SerializeField] private List<Collider> pieces = new List<Collider>();
        [SerializeField] private List<GameObject> bonePieces = new List<GameObject>();
        [SerializeField] private List<Collider> disabledColliders = new List<Collider>();

        [SerializeField] private Mesh hullAsset;
        [SerializeField] private string hullAssetPath = string.Empty;
        [SerializeField] private string hullAssetOwner = string.Empty;

        // 분해 설정(에디터 어셈블리의 ConcaveColliderSettings)을 JSON으로 보관한다.
        [SerializeField] private string settingsJson = string.Empty;
        [SerializeField] private bool isTrigger;
        // PhysicMaterial(Unity 6: PhysicsMaterial). 타입 이름이 버전마다 달라 Object로 보관한다.
        [SerializeField] private UnityEngine.Object physicsMaterial;
        [SerializeField] private int layer = -1;
        [SerializeField] private bool disableExistingColliders;
        [SerializeField] private string assetFolder = string.Empty;

        [SerializeField] private ConcaveColliderMetrics metrics = new ConcaveColliderMetrics();

        public int DataVersion => dataVersion;

        /// <summary>정적 조각 컨테이너("Concave Colliders"). 정적 조각이 없으면 null일 수 있다.</summary>
        public GameObject Container => container;

        /// <summary>생성된 모든 콜라이더(정적 + 본 조각).</summary>
        public IReadOnlyList<Collider> Pieces => pieces ?? (pieces = new List<Collider>());

        /// <summary>본 아래에 붙은 조각 GameObject.</summary>
        public IReadOnlyList<GameObject> BonePieces => bonePieces ?? (bonePieces = new List<GameObject>());

        /// <summary>생성 시 끈 기존 콜라이더. Remove Generated가 다시 켠다.</summary>
        public IReadOnlyList<Collider> DisabledColliders => disabledColliders ?? (disabledColliders = new List<Collider>());

        /// <summary>볼록 메시 조각을 담은 헐 메시 에셋(주 에셋). 볼록 메시 조각이 없으면 null.</summary>
        public Mesh HullAsset => hullAsset;

        public string HullAssetPath => hullAssetPath ?? string.Empty;

        /// <summary>헐 에셋 파일 이름을 정한 대상 식별자(복제본이 원본 에셋을 덮어쓰지 않게 비교한다).</summary>
        public string HullAssetOwner => hullAssetOwner ?? string.Empty;

        public string SettingsJson => settingsJson ?? string.Empty;
        public bool IsTrigger => isTrigger;
        /// <summary>생성한 콜라이더에 지정한 물리 재질(PhysicMaterial / Unity 6: PhysicsMaterial). 없으면 null.</summary>
        public UnityEngine.Object PhysicsMaterial => physicsMaterial;

        /// <summary>조각 레이어. -1이면 대상 오브젝트 레이어를 따른다.</summary>
        public int Layer => layer;

        public bool DisableExistingColliders => disableExistingColliders;
        public string AssetFolder => assetFolder ?? string.Empty;
        public ConcaveColliderMetrics Metrics => metrics ?? (metrics = new ConcaveColliderMetrics());

        /// <summary>생성된 콜라이더가 하나라도 남아 있는지.</summary>
        public bool HasGenerated
        {
            get
            {
                if (container != null) return true;
                IReadOnlyList<GameObject> bones = BonePieces;
                for (int i = 0; i < bones.Count; i++)
                {
                    if (bones[i] != null) return true;
                }

                return false;
            }
        }

        /// <summary>이 마커가 만든 콜라이더인지(컨테이너 또는 본 조각 아래).</summary>
        public bool Owns(Collider collider)
        {
            if (collider == null) return false;
            Transform t = collider.transform;
            if (container != null && t.IsChildOf(container.transform)) return true;
            IReadOnlyList<GameObject> bones = BonePieces;
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i] != null && t.IsChildOf(bones[i].transform)) return true;
            }

            return false;
        }

        /// <summary>이 마커가 만든 GameObject인지(컨테이너 자체·그 자식·본 조각).</summary>
        public bool OwnsObject(Transform t)
        {
            if (t == null) return false;
            if (container != null && t.IsChildOf(container.transform)) return true;
            IReadOnlyList<GameObject> bones = BonePieces;
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i] != null && t.IsChildOf(bones[i].transform)) return true;
            }

            return false;
        }

#if UNITY_EDITOR
        // ─── 에디터 전용 쓰기 API (Concave Mesh Collider 도구가 Undo.RecordObject 후 호출) ───

        /// <summary>생성 결과를 기록한다.</summary>
        public void EditorSetGenerated(GameObject newContainer, IEnumerable<Collider> newPieces, IEnumerable<GameObject> newBonePieces,
            IEnumerable<Collider> newDisabledColliders, ConcaveColliderMetrics newMetrics)
        {
            dataVersion = CurrentDataVersion;
            container = newContainer;
            Fill(pieces ?? (pieces = new List<Collider>()), newPieces);
            Fill(bonePieces ?? (bonePieces = new List<GameObject>()), newBonePieces);
            Fill(disabledColliders ?? (disabledColliders = new List<Collider>()), newDisabledColliders);
            metrics = newMetrics ?? new ConcaveColliderMetrics();
        }

        /// <summary>헐 메시 에셋 정보를 기록한다. 볼록 메시 조각이 없으면 asset = null, path = 빈 문자열도 허용한다.</summary>
        public void EditorSetHullAsset(Mesh asset, string path, string owner)
        {
            hullAsset = asset;
            hullAssetPath = path ?? string.Empty;
            hullAssetOwner = owner ?? string.Empty;
        }

        /// <summary>생성에 쓴 설정과 출력 옵션을 기록한다.</summary>
        public void EditorSetOptions(string newSettingsJson, bool newIsTrigger, UnityEngine.Object newMaterial, int newLayer,
            bool newDisableExisting, string newAssetFolder)
        {
            settingsJson = newSettingsJson ?? string.Empty;
            isTrigger = newIsTrigger;
            physicsMaterial = newMaterial;
            layer = newLayer;
            disableExistingColliders = newDisableExisting;
            assetFolder = newAssetFolder ?? string.Empty;
        }

        private static void Fill<T>(List<T> list, IEnumerable<T> source) where T : UnityEngine.Object
        {
            list.Clear();
            if (source == null) return;
            foreach (T item in source)
            {
                if (item != null && !list.Contains(item)) list.Add(item);
            }
        }
#endif
    }
}
