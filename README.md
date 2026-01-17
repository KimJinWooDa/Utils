# 🛠️ TelleR Utilities

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3+-blue?logo=unity" alt="Unity">
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License">
  <img src="https://img.shields.io/badge/Version-1.0.9-orange" alt="Version">
</p>

> Unity 개발에 자주 사용하는 유틸리티 모음 패키지  
> **필요한 기능만 골라서 사용하세요!**

---

## 📦 설치 방법

### Unity Package Manager (UPM)

1. **Package Manager** 열기 → `Window` → `Package Manager`
2. **+** 버튼 클릭 → `Add package from git URL...`
3. 아래 URL 입력 후 **Add**:

```
https://github.com/KimJinWooDa/Utils.git
```

---

## ✨ 기능 목록

> 💡 **모든 기능을 사용할 필요 없습니다!** 아래 표를 참고하여 본인 상황에 맞는 기능만 사용하세요.

| 기능 | 이런 상황에 사용하세요 | 사용 방법 |
|:---:|:---|:---|
| 🚀 **Fast Clone** | 멀티플레이어 테스트가 필요할 때 | `Tools → TelleR → Clones Manager` |
| 🔊 **Audio Volume 3D** | 3D 사운드 범위를 눈으로 확인하고 싶을 때 | GameObject에 컴포넌트 추가 |
| 📦 **UPM Package Creator** | 내 코드를 UPM 패키지로 만들고 싶을 때 | `Tools → TelleR → UPMPackageCreator` |
| 🎬 **Animation Inspector Controller** | 애니메이션 인스펙터를 더 편하게 쓰고 싶을 때 | 컴포넌트로 추가 |

---

## 🎯 상황별 가이드

### 🚀 "멀티플레이어 테스트를 빠르게 하고 싶어요"
**→ Fast Clone 사용**

```
Tools → TelleR → Clones Manager
```

- 동일 프로젝트의 복제본을 심볼릭 링크로 빠르게 생성
- 여러 Unity 에디터를 동시에 실행하여 멀티플레이 테스트 가능
- 디스크 용량 절약 (원본 에셋 공유)

---

### 🔊 "3D 사운드가 어디까지 들리는지 확인하고 싶어요"
**→ Audio Volume 3D 사용**

1. AudioSource가 있는 GameObject 선택
2. `Add Component` → `Audio Volume 3D` 검색하여 추가
3. Scene 뷰에서 오디오 범위가 시각화됨

- Min/Max Distance를 시각적으로 확인
- 사운드 디자인 작업 시 유용

---

### 📦 "내 코드를 UPM 패키지로 배포하고 싶어요"
**→ UPM Package Creator 사용**

```
Tools → TelleR → UPMPackageCreator
```

- `package.json` 자동 생성
- 폴더 구조 자동 설정
- Git URL로 바로 배포 가능한 형태로 변환

---

### 🎬 "애니메이션 작업을 더 효율적으로 하고 싶어요"
**→ Animation Inspector Controller 사용**

- 애니메이션 클립 인스펙터 기능 확장
- 더 편리한 애니메이션 미리보기 및 제어

---

## 📁 프로젝트 구조

```
Utils/
├── Editor/                          # 에디터 전용 (빌드에 포함 안됨)
│   ├── AnimationInspectorController/
│   ├── AudioVolume3D/
│   ├── FastClone/
│   └── UPMPackageCreator/
├── Runtime/                         # 런타임 (빌드에 포함됨)
│   ├── AnimationInspectorController/
│   └── AudioVolume3D/
├── package.json
├── LICENSE.md
└── README.md
```

---

## 📋 요구사항

- **Unity** 2021.3 이상

---

## ❓ FAQ

<details>
<summary><b>Q. 특정 기능만 사용하고 싶어요</b></summary>

패키지를 설치하면 모든 기능이 포함되지만, 사용하지 않는 기능은 빌드에 영향을 주지 않습니다.  
`Editor/` 폴더의 스크립트들은 빌드에 포함되지 않으며, `Runtime/` 스크립트도 사용하지 않으면 최적화 과정에서 제외됩니다.

</details>

<details>
<summary><b>Q. 업데이트는 어떻게 하나요?</b></summary>

Package Manager에서 패키지 선택 후 `Update` 버튼을 클릭하세요.

</details>

---

## 📄 라이선스

이 프로젝트는 [MIT License](LICENSE.md) 하에 배포됩니다.

---

<p align="center">
  Made with ❤️ by <b>TelleR</b>
</p>