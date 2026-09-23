using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TelleR.ConcaveCollider
{
    /// <summary>생성할 콜라이더의 출력 옵션(분해 알고리즘과 무관한 항목).</summary>
    [Serializable]
    public sealed class ConcaveColliderOutputOptions
    {
        public const string DefaultAssetFolder = "Assets/TelleR/ConcaveColliders";

        [Tooltip("볼록 메시 조각의 헐 메시를 저장할 폴더(Assets 아래). 대상마다 .asset 파일 하나를 만들고, 다시 만들 때는 같은 파일을 갱신합니다.")]
        public string assetFolder = DefaultAssetFolder;

        [Tooltip("생성할 콜라이더를 Trigger로 만듭니다.")]
        public bool isTrigger;

        /// <summary>물리 재질(PhysicMaterial / Unity 6: PhysicsMaterial). 다른 타입이면 무시한다.</summary>
        [Tooltip("생성할 콜라이더에 지정할 물리 재질(비우면 기본값).")]
        public Object physicsMaterial;

        [Tooltip("생성할 조각의 레이어. -1이면 대상 오브젝트의 레이어를 따릅니다.")]
        public int layer = -1;

        [Tooltip("대상에 켜져 있는 기존 콜라이더(Trigger 제외)를 삭제하지 않고 끕니다. Remove Generated로 다시 켜집니다.")]
        public bool disableExistingColliders;

        public ConcaveColliderOutputOptions Clone()
        {
            return (ConcaveColliderOutputOptions)MemberwiseClone();
        }

        /// <summary>폴더가 Assets 아래의 올바른 경로인지 확인하고 'Assets/...' 형태로 정규화한다.</summary>
        public static bool TryNormalizeAssetFolder(string folder, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(folder)) return false;
            string path = folder.Trim().Replace('\\', '/');
            while (path.EndsWith("/", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 1);
            if (path != "Assets" && !path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            string[] parts = path.Split('/');
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(invalid) >= 0) return false;
                if (part != part.Trim() || part.EndsWith(".", StringComparison.Ordinal)) return false;
            }

            normalized = path;
            return true;
        }
    }

    /// <summary>
    /// 물리 재질 타입 이름이 Unity 6에서 PhysicMaterial → PhysicsMaterial로 바뀌어(옛 이름은 오류) 타입을 직접 쓰지 않고
    /// Collider.sharedMaterial 속성의 타입으로 다룬다.
    /// </summary>
    public static class ConcavePhysicsMaterials
    {
        private static readonly PropertyInfo SharedMaterial = typeof(Collider).GetProperty("sharedMaterial", BindingFlags.Public | BindingFlags.Instance);

        /// <summary>현재 Unity의 물리 재질 타입(ObjectField용).</summary>
        public static Type MaterialType => SharedMaterial != null ? SharedMaterial.PropertyType : typeof(Object);

        /// <summary>물리 재질이 아니면 null.</summary>
        public static Object Filter(Object value)
        {
            return value != null && MaterialType.IsInstanceOfType(value) ? value : null;
        }

        public static void Assign(Collider collider, Object material)
        {
            if (collider == null || SharedMaterial == null) return;
            SharedMaterial.SetValue(collider, Filter(material));
        }
    }

    /// <summary>창·컨텍스트 메뉴가 공유하는 "마지막 사용 설정"(EditorPrefs).</summary>
    public static class ConcaveColliderPrefs
    {
        private const string SettingsKey = "TelleR.ConcaveMeshCollider.Settings";
        private const string OptionsKey = "TelleR.ConcaveMeshCollider.Output";

        [Serializable]
        private sealed class OptionsData
        {
            public string assetFolder = ConcaveColliderOutputOptions.DefaultAssetFolder;
            public bool isTrigger;
            public string materialId = string.Empty;
            public int layer = -1;
            public bool disableExistingColliders;
        }

        /// <summary>마지막으로 쓴 분해 설정. 저장된 값이 없거나 깨졌으면 기본값(Auto).</summary>
        public static ConcaveColliderSettings LoadSettings()
        {
            ConcaveColliderSettings settings = SettingsFromJson(EditorPrefs.GetString(SettingsKey, string.Empty));
            return settings ?? new ConcaveColliderSettings();
        }

        public static void SaveSettings(ConcaveColliderSettings settings)
        {
            if (settings == null) return;
            EditorPrefs.SetString(SettingsKey, SettingsToJson(settings));
        }

        public static ConcaveColliderOutputOptions LoadOptions()
        {
            var options = new ConcaveColliderOutputOptions();
            string json = EditorPrefs.GetString(OptionsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return options;
            try
            {
                var data = JsonUtility.FromJson<OptionsData>(json);
                if (data == null) return options;
                options.assetFolder = ConcaveColliderOutputOptions.TryNormalizeAssetFolder(data.assetFolder, out string folder)
                    ? folder
                    : ConcaveColliderOutputOptions.DefaultAssetFolder;
                options.isTrigger = data.isTrigger;
                options.layer = data.layer >= 0 && data.layer < 32 ? data.layer : -1;
                options.disableExistingColliders = data.disableExistingColliders;
                if (!string.IsNullOrEmpty(data.materialId) && GlobalObjectId.TryParse(data.materialId, out GlobalObjectId id))
                {
                    options.physicsMaterial = ConcavePhysicsMaterials.Filter(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id));
                }
            }
            catch (Exception)
            {
                // 깨진 값은 기본값으로 대체한다.
            }

            return options;
        }

        public static void SaveOptions(ConcaveColliderOutputOptions options)
        {
            if (options == null) return;
            var data = new OptionsData
            {
                assetFolder = options.assetFolder ?? ConcaveColliderOutputOptions.DefaultAssetFolder,
                isTrigger = options.isTrigger,
                layer = options.layer,
                disableExistingColliders = options.disableExistingColliders,
                materialId = options.physicsMaterial != null && EditorUtility.IsPersistent(options.physicsMaterial)
                    ? GlobalObjectId.GetGlobalObjectIdSlow(options.physicsMaterial).ToString()
                    : string.Empty
            };
            EditorPrefs.SetString(OptionsKey, JsonUtility.ToJson(data));
        }

        public static string SettingsToJson(ConcaveColliderSettings settings)
        {
            return settings != null ? JsonUtility.ToJson(settings) : string.Empty;
        }

        /// <summary>JSON에서 설정을 읽는다. 비었거나 읽을 수 없으면 null.</summary>
        public static ConcaveColliderSettings SettingsFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var settings = new ConcaveColliderSettings();
                JsonUtility.FromJsonOverwrite(json, settings);
                settings.Validate();
                return settings;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>마커에 기록된 출력 옵션. 폴더가 비었으면 마지막 사용 폴더를 쓴다.</summary>
        public static ConcaveColliderOutputOptions OptionsFromRoot(ConcaveColliderRoot root)
        {
            ConcaveColliderOutputOptions fallback = LoadOptions();
            if (root == null || string.IsNullOrEmpty(root.SettingsJson)) return fallback;
            return new ConcaveColliderOutputOptions
            {
                assetFolder = ConcaveColliderOutputOptions.TryNormalizeAssetFolder(root.AssetFolder, out string folder) ? folder : fallback.assetFolder,
                isTrigger = root.IsTrigger,
                physicsMaterial = ConcavePhysicsMaterials.Filter(root.PhysicsMaterial),
                layer = root.Layer >= 0 && root.Layer < 32 ? root.Layer : -1,
                disableExistingColliders = root.DisableExistingColliders
            };
        }

        /// <summary>마커에 기록된 분해 설정. 없으면 마지막 사용 설정.</summary>
        public static ConcaveColliderSettings SettingsFromRoot(ConcaveColliderRoot root)
        {
            return (root != null ? SettingsFromJson(root.SettingsJson) : null) ?? LoadSettings();
        }
    }
}
