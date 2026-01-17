# 🛠️ TelleR Utilities

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3+-blue?logo=unity" alt="Unity">
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License">
  <img src="https://img.shields.io/badge/Version-1.0.9-orange" alt="Version">
</p>

<p align="center">
  <b>Unity 개발에 자주 사용하는 유틸리티 모음 패키지</b><br>
  필요한 기능만 골라서 사용하세요!
</p>

---

## 📦 설치 방법

```
https://github.com/KimJinWooDa/Utils.git
```

1. `Window` → `Package Manager` 열기
2. **+** 버튼 → `Add package from git URL...`
3. 위 URL 입력 후 **Add**

---

## ✨ 기능 목록

> 💡 **모든 기능을 사용할 필요 없습니다!** 본인 상황에 맞는 기능만 사용하세요.

| 기능 | 이런 상황에 사용하세요 | 사용 방법 |
|------|------------------------|-----------|
| 🚀 **[Fast Clone](#1--fast-clone)** | 멀티플레이어 테스트, 다중 에디터 실행, 디스크 용량 절약 | `Tools → TelleR → Tool → Clones Manager` |
| 🔊 **[Audio Volume 3D](#2--audio-volume-3d)** | 3D 사운드 볼륨 영역 시각화, Fade/Occlusion 설정 | `Add Component → Audio Volume 3D` |
| 📦 **[UPM Package Creator](#3--upm-package-creator)** | UPM 패키지 생성, package.json/asmdef 자동 생성 | `Tools → TelleR → Tool → UPM Package Creator` |
| 🎬 **[Animation Inspector Controller](#4--animation-inspector-controller)** | 에디터 애니메이션 미리보기, 프레임 이벤트/트랜지션 | `Add Component → Animation Inspector Controller` |
| ◈ **[Mesh Pivot Tool](#5--mesh-pivot-tool)** | 메쉬 피벗 위치 수정, 프리셋/버텍스 스냅 | `MeshFilter 우클릭 → Edit Mesh Pivot` |

---

## 📖 상세 설명

<details>
<summary><h3>1. 🚀 Fast Clone</h3></summary>

**멀티플레이어 테스트를 위해 여러 Unity 에디터를 동시에 실행해야 할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 멀티플레이어 네트워크 테스트 | 호스트/클라이언트를 동시에 실행하여 실시간 테스트 |
| 디스크 용량 절약 | Assets, Packages, ProjectSettings를 심볼릭 링크로 공유 (Library만 복사) |
| 빠른 복제 생성 | robocopy(Windows) / rsync(Mac)로 Library 폴더 고속 복사 |
| 클론 관리 | 최대 10개 클론 생성/삭제/열기 지원 |

**주요 기능:**
- 🏷️ 클론 프로젝트에서는 "CLONE PROJECT" 표시로 구분
- 🖥️ Scene 뷰에 "Running as CLONE Mode" 오버레이 표시
- 🔒 원본 프로젝트에서만 클론 관리 가능

</details>

---

<details>
<summary><h3>2. 🔊 Audio Volume 3D</h3></summary>

**AudioSource의 3D 사운드 영역을 시각화하고 복잡한 볼륨 존을 구성할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 3D 사운드 영역 확인 | Main Volume 박스 영역을 Scene 뷰에서 시각적으로 확인 |
| Fade 영역 설정 | 볼륨이 점진적으로 감소하는 FadeDistance 설정 및 시각화 |
| Occlusion 영역 | 벽/장애물에 의한 소리 차단 영역을 수동으로 정의 |
| Inner Volume | 메인 볼륨 내 세부 사운드 소스 영역 추가 (Box/Sphere 지원) |
| 높이 감쇠 | UseHeightAttenuation으로 Y축 거리에 따른 볼륨 감쇠 |

**Inspector 탭 구성:**

| 탭 | 설명 |
|------|------|
| ⚙️ Settings | 볼륨 중심/크기, Fade 거리, 최대 볼륨 |
| 🎵 Audio | AudioClip, Mixer Group, Spatial Blend, Loop 설정 |
| 🧱 Occlusion | 수동 차단 영역 정의 |
| 📦 Inner | 내부 세부 볼륨 영역 추가 |
| 🎨 Visuals | 기즈모 색상 및 표시 옵션 |

</details>

---

<details>
<summary><h3>3. 📦 UPM Package Creator</h3></summary>

**자신의 코드를 Unity Package Manager 패키지로 만들어 Git URL로 배포하고 싶을 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 새 패키지 생성 | package.json, README.md, LICENSE.md, .gitignore 자동 생성 |
| 기존 패키지 불러오기 | 이미 만든 패키지를 불러와서 수정 |
| 기능 폴더 추가 | Editor/Runtime 폴더 구조 및 .asmdef 자동 생성 |
| 스크립트 추가 | 드래그앤드롭으로 기존 스크립트를 패키지에 복사 (Editor/Runtime 자동 분류) |
| 네임스페이스 자동 변환 | com.teller.util → TelleR.Util 형태로 자동 변환 |

**3단계 워크플로우:**

```
STEP 1 → 패키지 메타 정보 입력 (이름, 버전, 작성자 등)
STEP 2 → 기능 폴더 추가 및 스크립트 드래그앤드롭
STEP 3 → 버전 업데이트 및 개발 모드 전환
```

</details>

---

<details>
<summary><h3>4. 🎬 Animation Inspector Controller</h3></summary>

**에디터에서 애니메이션을 미리보고, 프레임 이벤트와 자동 트랜지션을 설정할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 에디터 애니메이션 미리보기 | Play Mode 없이 AnimationMode로 실시간 미리보기 |
| 프레임 단위 제어 | 재생/일시정지/정지, 프레임 슬라이더로 정밀 탐색 |
| 클립 빠른 전환 | 그리드 UI로 모든 클립 확인 및 원클릭 전환 |
| 클립 숨기기 | 사용하지 않는 클립을 숨겨서 작업 공간 정리 |
| 프레임 이벤트 | 특정 프레임에 UnityEvent 트리거 설정 |
| 자동 트랜지션 | 애니메이션 완료 후 지정 시간 뒤 다음 State로 자동 전환 |
| Playback 설정 | 재생 속도(0~4x), 루프, 역재생, 시작/종료 프레임 지정 |

**탭 구성:**

| 탭 | 설명 |
|------|------|
| 🔄 Transitions | 애니메이션 종료 시 자동 전환될 State 설정 (Tag 필터, Delay, Blend 지원) |
| ⚡ Frame Events | 특정 프레임에 UnityEvent 바인딩 |
| ⚙️ Settings | Playback 속도, 루프, 역재생, 프레임 범위, 초기 프레임 설정 |

</details>

---

<details>
<summary><h3>5. ◈ Mesh Pivot Tool</h3></summary>

**메쉬의 피벗 위치를 수정해야 할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 문/창문 회전축 | 경첩 위치로 피벗 이동하여 자연스러운 회전 |
| 바닥 기준 배치 | 피벗을 Bottom으로 이동하여 정확한 지면 배치 |
| 외부 모델 피벗 수정 | FBX/OBJ 임포트 후 잘못된 피벗 교정 |
| 무기/아이템 그립 | 캐릭터가 잡는 위치에 피벗 맞추기 |
| 레벨 디자인 | 타일/모듈형 에셋의 피벗 통일 |

**피벗 이동 방법:**

| 방법 | 설명 |
|------|------|
| 🖱️ 핸들 드래그 | Scene 뷰에서 위치 핸들로 자유 이동 |
| 🎯 프리셋 버튼 | Center, Top, Bottom, Left, Right, Front, Back |
| 🧲 Vertex Snap | `Ctrl` + 클릭으로 가장 가까운 버텍스에 스냅 |
| 📐 Snap 설정 | 슬라이더로 그리드 스냅 단위 조절 |

**적용/취소:**
- ✅ **Apply & Remove** - 변경 적용 후 컴포넌트 제거
- ↩️ **Revert** - 원래 메쉬로 복원

> ⚠️ 원본 메쉬 에셋은 변경되지 않으며, 수정된 메쉬는 씬에 저장됩니다.

</details>

---

## 📋 요구사항

- **Unity** 2021.3 이상

---

## 📄 라이선스

[MIT License](LICENSE.md)

---

<p align="center">
  Made with ❤️ by <b>TelleR</b>
</p>
