# 🛠️ TelleR Utilities

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3+-blue?logo=unity" alt="Unity">
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License">
  <img src="https://img.shields.io/badge/Version-1.1.0-orange" alt="Version">
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

### 🎨 메쉬 & 3D 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| ◈ **Mesh Pivot Tool** | 메쉬 피벗 위치 수정, 프리셋/버텍스 스냅 | `MeshFilter 우클릭 → Edit Mesh Pivot` |
| 📄 **MeshFilter FBX Generator** | MeshFilter/SkinnedMesh의 FBX 백업 생성 | MeshFilter Inspector 자동 표시 |
| 🦴 **Skinned Mesh Collider** | SkinnedMeshRenderer → MeshCollider 변환 | `Tools → TelleR → Tool → Skinned Mesh Collider Creator` |

### 🔊 오디오 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| 🔊 **Audio Volume 3D** | 3D 사운드 볼륨 영역 시각화 | `Add Component → Audio Volume 3D` |

### 🎬 애니메이션 & 이펙트 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| 🎬 **Animation Inspector Controller** | 에디터 애니메이션 미리보기, 프레임 이벤트/트랜지션 | `Add Component → Animation Inspector Controller` |
| ✨ **Trail Effect** | GPU Instancing 기반 커스텀 트레일 이펙트 | `Add Component → Trail Effect` |

### 🖼️ 스프라이트 & UI 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| ✂️ **Auto Sprite Slicer** | 이미지를 Single Sprite로 일괄 변환 | `Tools → TelleR → Tool → Auto Sprite Slicer` |
| 🗂️ **UI Sprite Atlas Builder** | SpriteAtlas 빌드/업데이트 | `Tools → TelleR → Tool → AtlasBuilder` |

### 🛠️ 프로젝트 관리 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| 🚀 **Fast Clone** | 멀티플레이어 테스트용 프로젝트 복제 | `Tools → TelleR → Tool → Clones Manager` |
| 📦 **UPM Package Creator** | UPM 패키지 생성, package.json/asmdef 자동 생성 | `Tools → TelleR → Tool → UPM Package Creator` |

---

## 📖 상세 설명

<!-- ────────────────── 메쉬 & 3D ────────────────── -->

<details>
<summary>◈ <b>Mesh Pivot Tool</b> - 메쉬 피벗 위치 수정</summary>

<br>

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

<details>
<summary>📄 <b>MeshFilter FBX Generator</b> - 메쉬 FBX 백업 생성</summary>

<br>

**MeshFilter 또는 SkinnedMeshRenderer의 메쉬를 FBX로 백업하여 리임포트 시 편집 내용이 손실되는 것을 방지합니다.**

| 상황 | 설명 |
|------|------|
| 리임포트 손실 방지 | 메쉬 수정 후 FBX 백업을 생성하여 안전하게 보관 |
| MeshFilter 지원 | MeshFilter Inspector에 "Create FBX Backup" 버튼 자동 표시 |
| SkinnedMesh 지원 | SkinnedMeshRenderer Inspector에도 동일 기능 제공 |
| 자동 감지 | 기존 FBX 백업이 있으면 버튼 숨김 |

**사용 방법:**
- MeshFilter 또는 SkinnedMeshRenderer Inspector에서 경고 표시 확인
- **Create FBX Backup** 버튼 클릭
- `Assets/GeneratedFBX` 폴더에 자동 저장

</details>

---

<details>
<summary>🦴 <b>Skinned Mesh Collider</b> - SkinnedMesh → MeshCollider 변환</summary>

<br>

**SkinnedMeshRenderer의 현재 포즈 메쉬를 MeshCollider로 변환할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 캐릭터 콜라이더 | 현재 포즈 기준으로 정확한 MeshCollider 생성 |
| 메쉬 간소화 | Very Low ~ Very High 프리셋으로 폴리곤 감소 (최대 90%) |
| 다중 메쉬 결합 | 여러 SkinnedMeshRenderer를 하나로 합쳐서 콜라이더 생성 |
| 프리뷰 지원 | 생성 전 메쉬 미리보기 확인 |
| 커스텀 저장 | 경로/이름 지정, Convex 옵션, 기존 콜라이더 교체 |

**간소화 프리셋:**

| 프리셋 | 설명 |
|--------|------|
| Very Low | 90% 폴리곤 감소 |
| Low | 75% 감소 |
| Medium | 50% 감소 |
| High | 25% 감소 |
| Very High | 10% 감소 |
| Custom | 직접 설정 |

</details>

---

<!-- ────────────────── 오디오 ────────────────── -->

<details>
<summary>🔊 <b>Audio Volume 3D</b> - 3D 사운드 볼륨 영역 시각화</summary>

<br>

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

<!-- ────────────────── 애니메이션 & 이펙트 ────────────────── -->

<details>
<summary>🎬 <b>Animation Inspector Controller</b> - 에디터 애니메이션 미리보기</summary>

<br>

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
<summary>✨ <b>Trail Effect</b> - GPU Instancing 기반 트레일 이펙트</summary>

<br>

**메쉬 기반의 고성능 트레일 이펙트를 생성할 때 사용합니다. GPU Instancing을 활용하여 GC 없이 동작합니다.**

| 상황 | 설명 |
|------|------|
| 잔상 이펙트 | 캐릭터/오브젝트의 움직임 궤적을 색상 잔상으로 표현 |
| 텍스처 스탬프 | 빌보드 텍스처를 궤적을 따라 배치 (Follow/Trail 모드) |
| 프로파일 시스템 | ScriptableObject 프로파일로 설정 재사용 및 오버라이드 |
| 멀티 메쉬 병합 | 여러 TrailEffect를 자동으로 부모-자식 병합 |
| Fresnel 효과 | 가장자리 강조 Fresnel Power/Intensity 지원 |
| 스텐실 겹침 방지 | PreventOverlap으로 잔상 간 겹침 제거 |

**Trail 모드:**

| 모드 | 설명 |
|------|------|
| 🎨 Color | 메쉬를 복제하여 색상 잔상 생성 (Scale/Fresnel 지원) |
| 🖼️ TextureStamp | 빌보드 텍스처를 궤적에 배치 (Follow Chain / Trail 스타일) |

**주요 설정:**

| 설정 | 설명 |
|------|------|
| Duration | 잔상 지속 시간 (0.05~5초) |
| Snapshots Per Second | 초당 스냅샷 캡처 횟수 (1~60) |
| Color Over Lifetime | Gradient로 수명에 따른 색상 변화 |
| Scale Start / End | 잔상 시작/종료 크기 비율 |
| Max Snapshots | 최대 스냅샷 수 (4~128) |
| Min Distance | 스냅샷 간 최소 거리 |

**프로파일 생성:**
- `Assets → Create → TelleR → Trail Profile` 로 프로파일 에셋 생성
- 컴포넌트에 프로파일 할당 후 필드별 오버라이드 가능

</details>

---

<!-- ────────────────── 스프라이트 & UI ────────────────── -->

<details>
<summary>✂️ <b>Auto Sprite Slicer</b> - 스프라이트 일괄 변환</summary>

<br>

**여러 이미지를 한 번에 Single Sprite 타입으로 변환할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 일괄 변환 | 여러 이미지를 한 번에 Sprite (Single) 타입으로 설정 |
| 드래그앤드롭 | 이미지 파일 또는 폴더를 드래그하여 추가 |
| 폴더 재귀 검색 | 폴더를 드롭하면 내부 모든 이미지를 자동 탐색 |
| 다양한 포맷 | PNG, JPG, TGA, BMP, PSD, GIF, HDR, EXR, TIF 지원 |

**자동 설정:**
- Texture Type → Sprite (2D and UI)
- Sprite Mode → Single
- Alpha Is Transparency → ✅
- Pixels Per Unit → 100
- Pivot → Center

</details>

---

<details>
<summary>🗂️ <b>UI Sprite Atlas Builder</b> - SpriteAtlas 빌드 도구</summary>

<br>

**이미지/스프라이트/텍스처/게임오브젝트를 드래그앤드롭하여 SpriteAtlas를 빌드하거나 업데이트할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 아틀라스 생성 | 새 SpriteAtlas 에셋 생성 |
| 아틀라스 업데이트 | 기존 SpriteAtlas에 항목 추가 |
| 드래그앤드롭 | 이미지, 스프라이트, 텍스처, 게임오브젝트 지원 |
| 자동 패킹 | 빌드 후 자동 Pack Preview 실행 옵션 |
| 커스텀 경로 | Atlas Asset Path 직접 지정 |

**사용 방법:**
1. `Tools → TelleR → Tool → AtlasBuilder` 열기
2. Drop Area에 소스 에셋 드래그앤드롭
3. Atlas Asset Path 확인/수정
4. **Build / Update Atlas** 클릭

</details>

---

<!-- ────────────────── 프로젝트 관리 ────────────────── -->

<details>
<summary>🚀 <b>Fast Clone</b> - 멀티플레이어 테스트용 프로젝트 복제</summary>

<br>

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
<summary>📦 <b>UPM Package Creator</b> - UPM 패키지 생성 도구</summary>

<br>

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

## 📋 요구사항

- **Unity** 2021.3 이상

---

## 📄 라이선스

[MIT License](LICENSE.md)

---

<p align="center">
  Made with ❤️ by <b>TelleR</b>
</p>
