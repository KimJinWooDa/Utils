# 🛠️ TelleR Utilities

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3+-blue?logo=unity" alt="Unity">
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License">
  <img src="https://img.shields.io/badge/Version-1.3.0-orange" alt="Version">
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

특정 커밋으로 고정하려면 URL 끝에 커밋 해시를 붙이세요: `https://github.com/KimJinWooDa/Utils.git#<커밋 해시>`. 버전 태그(`#v1.2.0` 등)는 저장소에 해당 태그가 게시된 뒤에만 쓸 수 있습니다.

> 💡 설치 후 `Tools → TelleR → Tool Hub`를 열면 모든 도구의 설명과 바로가기를 한 창에서 볼 수 있습니다.

---

## 🔄 업데이트 방법

**저장소에 새 커밋이 올라가도 이미 설치한 프로젝트는 자동으로 업데이트되지 않습니다.** git URL로 설치한 패키지는 `Packages/packages-lock.json`에 설치 당시의 커밋 해시가 고정되기 때문입니다.

아래 방법 중 하나로 업데이트하세요.

| 방법 | 설명 |
|------|------|
| Package Manager | `Window → Package Manager`에서 **TelleR Utilities** 선택 → **Update** 버튼이 보이면 클릭 |
| packages-lock 항목 삭제 | Unity를 닫고 `Packages/packages-lock.json`에서 `com.teller.util` 항목을 지운 뒤 다시 열면 최신 커밋을 받음 |
| 커밋·태그 고정 | `Packages/manifest.json`의 URL 끝에 `#<커밋 해시>`를 붙여 고정하고, 업데이트할 때 해시만 바꿈 (버전 태그는 게시된 뒤에만 사용 가능) |

---

## 🧩 선택 패키지

**필수로 설치해야 하는 외부 패키지는 없습니다.** 아래 패키지는 설치되어 있으면 자동으로 감지해 해당 기능을 켜고, 없으면 그 기능만 안내 문구(HelpBox·로그)로 대신합니다. 어떤 조합이든 컴파일 에러나 셰이더 에러가 나지 않습니다.

| 패키지 | 있으면 쓸 수 있는 기능 | 없을 때 |
|--------|------------------------|---------|
| URP (`com.unity.render-pipelines.universal`) | Trail Effect의 URP 셰이더, Device Manager의 MSAA 프로필 | Trail Effect는 Built-in 셰이더로 동작, Device Manager는 경고만 남김 |
| Unity UI (`com.unity.ugui`) | UI Atlas Builder의 Canvas / Image / Button 스캔 | 스프라이트·텍스처·폴더 드롭과 SpriteRenderer 수집은 그대로 동작 |
| XR 모듈 (`com.unity.modules.xr`) | Foveation Starter (Unity 2022.2 이상 필요) | 인스펙터 HelpBox와 로그로 "적용되지 않음" 안내 |
| FBX Exporter (`com.unity.formats.fbx`) | FBX Backup의 **Create FBX Backup** | **Save as .asset** 사용 가능, **Open Package Manager** 버튼 표시 |

> 패키지가 쓰는 빌트인 모듈(animation, audio, imageconversion, imgui, jsonserialize, physics, uielements)은 `package.json`에 선언되어 있어, 해당 모듈을 꺼 둔 프로젝트에서도 자동으로 켜집니다.

---

## ✨ 기능 목록

> 💡 **모든 기능을 사용할 필요 없습니다!** 본인 상황에 맞는 기능만 사용하세요.

### 🧭 Tool Hub

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| 🧭 **Tool Hub** | 모든 도구를 카테고리별로 보여 주는 목록·검색·바로가기 | `Tools → TelleR → Tool Hub` |

### 🎨 메쉬 & 3D 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| ◈ **Mesh Pivot Tool** | 메쉬 피벗 위치·회전 수정, 프리셋/버텍스 스냅 | `MeshFilter / SkinnedMeshRenderer 우클릭 → Edit Mesh Pivot` |
| 📄 **FBX Backup** (MeshFilter FBX Generator) | 씬 전용 메쉬를 .asset 또는 FBX로 백업 | MeshFilter / SkinnedMeshRenderer Inspector 자동 표시 |
| 🧩 **Concave Mesh Collider** 🆕 | 오목한 메쉬를 볼록 조각 여러 개(Box/Sphere/Capsule/볼록 MeshCollider)로 나눈 복합 콜라이더 생성 | `Tools → TelleR → Concave Mesh Collider` / `Hierarchy 우클릭 → TelleR → Generate Concave Collider` |
| 🦴 **Skinned Mesh Collider** | SkinnedMeshRenderer → MeshCollider 변환 | `Tools → TelleR → Skinned Mesh Collider` |
| 🌾 **Grass Blade Mesh Generator** 🆕 | 로우폴리 풀잎 메쉬(Triangle / Quad / QuadCross / TriCross)를 .asset으로 생성 | `Tools → TelleR → Grass Blade Mesh Generator` |
| 💥 **Mesh Fragmenter** 🆕 | 메쉬를 보로노이 조각(볼록 MeshCollider + Rigidbody)으로 미리 잘라 두기 | `Tools → TelleR → Mesh Fragmenter` |

### 🔊 오디오 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| 🔊 **Audio Volume 3D** | 3D 사운드 볼륨 영역 시각화, 페이드·차폐 구성 | `Add Component → TelleR → Audio Volume 3D` |

### 🎬 애니메이션 & 이펙트 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| 🎬 **Animation Inspector Controller** | 에디터/Play Mode 애니메이션 미리보기, 프레임 이벤트/트랜지션 | `Add Component → TelleR → Animation Inspector Controller` |
| ✨ **Trail Effect** | GPU Instancing 기반 커스텀 트레일 이펙트 (URP / Built-in) | `Add Component → TelleR → Trail Effect` |

### 🖼️ 스프라이트 & UI 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| ✂️ **Auto Sprite Slicer** | 배경 제거·트림 후 Single Sprite로 일괄 변환 | `Tools → TelleR → Auto Sprite Slicer` |
| 🗂️ **UI Atlas Builder** | SpriteAtlas(V1/V2) 빌드/업데이트 | `Tools → TelleR → UI Atlas Builder` |

### 🛠️ 프로젝트 관리 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| 🚀 **Fast Clone** | 멀티플레이어 테스트용 프로젝트 복제 | `Tools → TelleR → Fast Clone` |
| 📦 **UPM Package Creator** | UPM 패키지 생성, package.json/asmdef 자동 생성 | `Tools → TelleR → UPM Package Creator` |

### 🔺 성능 분석 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| 🔺 **Tris Profiler** 🆕 | 카메라에 보이는 메시의 삼각형·정점 수 합계와 무거운 오브젝트 순위 | `Tools → TelleR → Tris Profiler` |

### 🥽 XR (Meta Quest) 도구

| 기능 | 설명 | 사용 방법 |
|------|------|-----------|
| 📱 **Device Manager** | Quest 기종을 감지해 URP MSAA 프로필 자동 적용 | `Add Component → TelleR → Device Manager` |
| 👁️ **Foveation Starter** | 실행 시 Foveated Rendering 자동 적용 | `Add Component → TelleR → Foveation Starter` |

---

## 📖 상세 설명

<!-- ────────────────── Tool Hub ────────────────── -->

<details>
<summary>🧭 <b>Tool Hub</b> - 도구 모음 바로가기</summary>

<br>

**패키지에 어떤 도구가 있는지 한눈에 보고 바로 열 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 도구 찾기 | 카테고리 헤더와 짧은 한국어 설명, 검색창으로 16개 도구 탐색 |
| 창 도구 열기 | 버튼으로 해당 도구 창을 바로 열기 |
| 컴포넌트 추가 | **Add to Selection**으로 선택한 오브젝트에 컴포넌트 추가 (Undo 가능) |
| 요구 사항 확인 | 필요한 선택 패키지(URP, uGUI, XR, FBX Exporter)나 Unity 버전이 없으면 표시 |

</details>

---

<!-- ────────────────── 메쉬 & 3D ────────────────── -->

<details>
<summary>◈ <b>Mesh Pivot Tool</b> - 메쉬 피벗 위치·회전 수정</summary>

<br>

**메쉬의 피벗 위치나 회전을 수정해야 할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 문/창문 회전축 | 경첩 위치로 피벗 이동하여 자연스러운 회전 |
| 바닥 기준 배치 | 피벗을 Bottom으로 이동하여 정확한 지면 배치 |
| 외부 모델 피벗 수정 | FBX/OBJ 임포트 후 잘못된 피벗·축 교정 |
| 무기/아이템 그립 | 캐릭터가 잡는 위치에 피벗 맞추기 |
| 레벨 디자인 | 타일/모듈형 에셋의 피벗 통일 |

**피벗 편집 방법:**

| 방법 | 설명 |
|------|------|
| 🖱️ 핸들 드래그 | Scene 뷰에서 Position / Rotation 핸들로 자유 편집 (드래그 1회 = Undo 1단계) |
| 🎯 프리셋 버튼 | Center, Top, Bottom, Left, Right, Front, Back |
| 🧭 축 정렬 | WORLD ALIGN, ±X / ±Y / ±Z 방향 버튼 |
| 🧲 Vertex Snap | `Ctrl` + 클릭으로 가장 가까운 버텍스에 스냅 (카메라를 향한 버텍스 우선) |
| 📐 Snap 설정 | 그리드 스냅 단위 조절 |

편집 중에는 기본 Move/Rotate 기즈모가 숨겨지고 피벗 핸들만 보입니다. Scene 뷰 Gizmos를 꺼도 핸들은 표시됩니다.

**적용/취소:**
- ✅ **Apply & Remove** - 변경 적용 후 컴포넌트 제거. 메쉬를 **씬에만 저장**할지 **에셋으로 저장**(.asset)할지 선택
- ↩️ **Revert** - 원본 메쉬와 편집 전 위치·회전, 자식 위치, Box/Sphere/Capsule 콜라이더 center까지 복원 (`Ctrl+Z` 한 번으로도 취소 가능)

> ⚠️ 원본 메쉬 에셋은 변경되지 않습니다. **프리팹은 Apply & Remove에서 "에셋으로 저장"을 고른 뒤 적용·저장하세요.** 편집 도중 프리팹을 저장하거나 Apply Overrides를 하면 Missing 메쉬가 되며, 이때는 인스펙터의 Revert로 복구할 수 있습니다. 프리팹 모드에서는 에셋 저장만 허용합니다.
>
> ⚠️ Scale이 비균등하면 피벗 회전이 막힙니다. 본이 있는 스킨드 메쉬는 Scene 뷰에 변화가 보이지 않으며(인스펙터 버튼으로만 편집), 씬 핸들을 쓰지 않습니다.

</details>

---

<details>
<summary>📄 <b>FBX Backup</b> (MeshFilter FBX Generator) - 메쉬 백업 생성</summary>

<br>

**씬에만 저장된 메쉬를 에셋으로 백업해, 씬이 손상되거나 리임포트할 때 편집 내용이 사라지는 것을 막습니다.**

| 상황 | 설명 |
|------|------|
| 씬 전용 메쉬 | 경고와 **Save as .asset** 버튼 표시. FBX Exporter가 있으면 **Create FBX Backup**, 없으면 **Open Package Manager** 버튼도 표시 |
| .asset 메쉬 | FBX Exporter가 설치된 경우에만 경고 없이 **Create FBX Backup** 버튼 표시 |
| 모델 파일·내장 메쉬 | .fbx/.obj/.blend 등 모델 파일 메쉬와 내장 메쉬에는 아무것도 표시하지 않음 |
| SkinnedMesh 지원 | SkinnedMeshRenderer Inspector에도 동일 기능 제공 (기본 인스펙터의 Edit Bounds·프리뷰는 그대로 동작) |

**사용 방법:**
- MeshFilter 또는 SkinnedMeshRenderer Inspector에서 안내 확인
- **Save as .asset** 또는 **Create FBX Backup** 버튼 클릭
- FBX는 `Assets/GeneratedFBX` 폴더에 저장

> ⚠️ FBX 백업에는 선택 패키지 **FBX Exporter**(`com.unity.formats.fbx`)가 필요합니다. 없어도 `.asset` 저장은 사용할 수 있습니다.
>
> Play 모드의 런타임 메쉬와 피벗 편집 중인 메쉬에는 경고를 표시하지 않습니다.

</details>

---

<details>
<summary>🧩 <b>Concave Mesh Collider</b> 🆕 - 오목한 메쉬용 복합 콜라이더 자동 생성</summary>

<br>

**컵·링·U자처럼 오목한 메쉬를 볼록 조각 여러 개로 나눠, Rigidbody에서도 쓸 수 있고 모양을 따라가는 콜라이더를 만듭니다.** 외부 패키지나 네이티브 플러그인이 필요 없습니다.

| 상황 | 설명 |
|------|------|
| 움직이는 오목한 오브젝트 | Rigidbody에 비볼록 MeshCollider를 쓸 수 없을 때 볼록 조각으로 대체 |
| 가벼운 콜라이더 | 꽉 맞는 조각은 Box/Sphere/Capsule, 나머지는 볼록 MeshCollider |
| 캐릭터·동물(스킨드 메쉬) | 본별 조각을 본 아래에 붙여 애니메이션을 따라감 |
| 여러 오브젝트 한 번에 | 다중 선택 일괄 생성, 한 대상이 실패해도 나머지는 계속 처리, 진행 막대에서 취소 |

**사용 방법:**
1. `Tools → TelleR → Concave Mesh Collider` 창을 열고 Hierarchy에서 오브젝트 선택 (또는 드래그 앤 드롭)
2. **Preview**로 Scene 뷰에서 조각을 미리 확인 (씬은 바뀌지 않음)
3. **Generate**로 적용 → 대상 아래 `Concave Colliders` 자식과 `Concave Collider Root` 컴포넌트 생성 (스킨드 조각은 해당 본 아래)
4. 다시 만들 때는 인스펙터의 **Rebuild**, 되돌릴 때는 **Remove Generated** / **Remove** 또는 `Ctrl+Z`

**주요 옵션:**

| 옵션 | 설명 |
|------|------|
| Auto | Balanced 품질로 고정하고, 오목한 정도에 따라 조각 수(4~12)만 자동으로 정함 (기본 ON) |
| Quality / Max Pieces | Auto를 끄면 표시. 품질(Fast / Balanced / Precise)과 최대 조각 수(1~32)를 직접 지정 |
| Advanced | 허용치, 볼록 메쉬 최대 정점 수, 프리미티브 사용, 패딩, 스킨드 옵션 |
| Output | 헐 메쉬 저장 폴더(기본 `Assets/TelleR/ConcaveColliders`), Is Trigger, Physics Material, Override Layer, 기존 콜라이더 끄기(삭제하지 않음) |

**결과 리포트:** 대상별 조각 수, **Cover**(원본 내부 중 콜라이더에 덮인 비율, 높을수록 좋음), **Out**(콜라이더 중 원본 밖으로 나온 비율, 낮을수록 좋음), 경고

**우클릭 메뉴:** MeshFilter·SkinnedMeshRenderer 컴포넌트 메뉴와 Hierarchy 우클릭(`TelleR → Generate Concave Collider`)으로 바로 생성하고, `Concave Collider Root` 컴포넌트 메뉴에서 Rebuild / Remove 할 수 있습니다.

> ⚠️ 볼록 메쉬 조각의 메쉬는 대상마다 `.asset` 파일 하나(서브 에셋 Hull_00…)로 저장되며, 다시 만들면 같은 파일(GUID)을 갱신합니다. 다른 씬·프리팹이 같은 파일을 쓰고 있으면 덮어쓰지 않고 새 파일로 나눠 저장합니다.
>
> ⚠️ Remove에서 에셋 삭제를 골라도 다른 씬·프리팹이 쓰는 헐 에셋은 지우지 않습니다. 생성 시 꺼 둔 기존 콜라이더는 Remove Generated 때 다시 켜집니다.
>
> ⚠️ 컵·링 안쪽처럼 깊게 파인 부분은 일부 메워질 수 있습니다. 결과 리포트의 **Out**이 높으면 Auto를 끄고 Quality를 **Precise**로, Max Pieces를 더 크게 올려 다시 만드세요. 그래도 모양과 정확히 같지는 않을 수 있습니다.

</details>

---

<details>
<summary>🦴 <b>Skinned Mesh Collider</b> - SkinnedMesh → MeshCollider 변환</summary>

<br>

**SkinnedMeshRenderer의 현재 포즈 메쉬를 MeshCollider로 변환할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 캐릭터 콜라이더 | 현재 포즈 기준으로 정확한 MeshCollider 생성 |
| 메쉬 간소화 | Very Low ~ Very High 프리셋으로 폴리곤 감소 (최대 90%), UV·노말 이음새 용접 후 단순화 |
| 다중 메쉬 결합 | 여러 SkinnedMeshRenderer를 하나로 합쳐서 콜라이더 생성 |
| 대용량 메쉬 | 정점 65535개 초과 메쉬도 UInt32 인덱스로 처리 |
| 프리뷰 지원 | 생성 전 메쉬 미리보기 확인 (결과와 같은 좌표 기준) |
| 커스텀 저장 | 경로/이름 지정, Convex 옵션, 기존 콜라이더 교체 (같은 이름이면 GUID 유지하며 내용 교체) |

**간소화 프리셋:**

| 프리셋 | 설명 |
|--------|------|
| Very Low | 90% 폴리곤 감소 |
| Low | 75% 감소 |
| Medium | 50% 감소 |
| High | 25% 감소 |
| Very High | 10% 감소 |
| Custom | 직접 설정 |

> 💡 Convex 기본값은 **OFF**입니다. Convex MeshCollider는 255 폴리곤 제한이 있어 간소화가 의미가 없으므로, Rigidbody용 오목한 콜라이더가 필요하면 **Concave Mesh Collider**를 사용하세요.

</details>

---

<details>
<summary>🌾 <b>Grass Blade Mesh Generator</b> 🆕 - 로우폴리 풀잎 메쉬 생성</summary>

<br>

**로우폴리 풀잎 메쉬를 만들어 `.asset`으로 저장합니다.** 피벗은 풀잎 뿌리(바닥 중앙)이고 +Y가 위쪽입니다. 면이 한쪽뿐이므로 양면 렌더링(Cull Off) 셰이더와 함께 쓰세요.

| 항목 | 설명 |
|------|------|
| Shape | Triangle(삼각형 1장, 정점 3·삼각형 1) / Quad(사각형 1장, 4·2) / QuadCross(사각형 2장 90° 교차, 8·4) / TriCross(사각형 3장 60° 간격, 12·6, 기본값) |
| Width / Height | 풀잎 뿌리 폭과 높이(미터) |
| Tip Taper | 끝 폭 ÷ 뿌리 폭. 0이면 뾰족하고 1이면 직사각형입니다(Triangle에는 적용되지 않음) |
| Curve (Z lean) | 끝을 각 면의 앞(+Z)으로 기울이는 정도(높이 대비 비율). 음수면 반대쪽으로 기울어집니다 |
| Reset | 모양·크기 값(Shape·Width·Height·Tip Taper·Curve)을 기본값으로 되돌림 |
| Folder / File Name | 저장 위치(Assets 아래, 없으면 만듦). 이름을 비워 두면 `GrassBlade_<Shape>` |
| Create | 메쉬를 저장하고 Project 창에서 선택합니다. 같은 이름이 있으면 덮어쓰기(GUID 유지, Ctrl+Z 가능) 또는 새 이름으로 저장을 고릅니다 |

**사용 방법:**
1. `Tools → TelleR → Grass Blade Mesh Generator` 창 열기
2. Blade 섹션에서 Shape·크기를 정하고 풀잎 1개당 정점·삼각형 수 확인
3. Output 섹션에서 Folder / File Name을 정하고 저장 위치 미리보기 확인
4. **Create** 클릭

> 💡 기본 저장 폴더: `Assets/TelleR/Generated/GrassBlades`. 코드에서는 `TelleR.GrassBladeMeshGenerator.GenerateAndSave(...)`로도 만들 수 있습니다.

</details>

---

<details>
<summary>💥 <b>Mesh Fragmenter</b> 🆕 - 메쉬를 물리 조각으로 미리 잘라 두기</summary>

<br>

**선택한 오브젝트의 메쉬를 보로노이 셀로 잘라, 단면이 막힌 물리 조각(볼록 MeshCollider + Rigidbody)을 원본 옆에 만듭니다.** 부서지는 오브젝트를 런타임 연산 없이 미리 만들어 둘 때 씁니다. 외부 패키지가 필요 없습니다.

| 상황 | 설명 |
|------|------|
| 부서지는 소품 | 조각 루트를 꺼 두었다가 깨질 때 `SetActive(true)` 후 Kinematic 해제 |
| 여러 오브젝트 한 번에 | 다중 선택 시 대상마다 따로 자르며, 진행 막대에서 취소 가능 |
| 프리팹 편집 | 프리팹 편집 모드에서도 동작 (이때 메쉬는 항상 .asset으로 저장) |

**사용 방법:**
1. `Tools → TelleR → Mesh Fragmenter` 창을 열고 Hierarchy에서 MeshFilter가 있는 오브젝트 선택
2. Targets 목록의 배지 확인 — Ready / Open Mesh(닫히지 않은 메쉬, 속이 빈 조각이 될 수 있음) / No Mesh / Prefab Asset(처리 안 함)
3. **Fragment** 클릭 → 원본 옆에 `{이름}_Fragments` 루트와 조각 생성
4. 씬 변경은 `Ctrl+Z` 한 번으로 모두 되돌릴 수 있습니다 (저장한 메쉬 .asset 파일은 남습니다)

**주요 옵션:**

| 옵션 | 설명 |
|------|------|
| Fragments | 조각 수 4 / 8 / 16 / 32 |
| Seed | 조각 모양을 정하는 난수 시드 (같은 시드 = 같은 결과) |
| Interior Material | 잘린 단면 머티리얼. 비우면 원본의 첫 머티리얼 사용. FBX 내장·서브 에셋·내장 머티리얼도 정확히 기억 |
| Mass Per Fragment / Kinematic / Use Gravity / Start Inactive | 조각 Rigidbody·루트 초기 상태 |
| Source Object | 생성 후 원본 처리: Keep / Hide Renderer / Deactivate (Undo 가능) |
| Save Meshes As Asset / Asset Folder | 조각 메쉬를 `Assets/` 아래 폴더에 .asset으로 저장 (기본 `Assets/TelleR/Fragments`, 기존 파일은 덮어쓰지 않음). 끄면 씬에만 저장 |
| Reset | 설정을 기본값으로 되돌림 |

설정은 EditorPrefs에 저장되어 창을 다시 열어도 유지됩니다.

**스크립트에서 사용:** `MeshFragmentEditor.Fragment(IList<GameObject> targets, MeshFragmenterOptions options)` — 대화상자 없이 조각 루트 목록을 반환하고 결과는 `[TelleR/Mesh Fragmenter]` 로그로 남깁니다. 씬 변경은 Undo 한 그룹으로 묶입니다.

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
| Occlusion 영역 | 벽/장애물에 의한 소리 차단 영역을 수동으로 정의 (레이캐스트 아님) |
| Inner Volume | 메인 볼륨 내 세부 사운드 소스 영역 추가 (Box/Sphere 지원) |
| 높이 감쇠 | UseHeightAttenuation으로 Y축 거리에 따른 볼륨 감쇠 |
| 다중 편집 | 여러 AudioVolume3D를 한 번에 선택해 편집 |

**Inspector 탭 구성:**

| 탭 | 설명 |
|------|------|
| ⚙️ Settings | 볼륨 중심/크기, Fade 거리, 최대 볼륨 |
| 🎵 Audio | AudioClip, Mixer Group, Spatial Blend, Loop, 재생 옵션 |
| 🧱 Occlusion | 수동 차단 영역 정의 |
| 📦 Inner | 내부 세부 볼륨 영역 추가 |
| 🎨 Visuals | 기즈모 색상 및 표시 옵션 |

**재생 옵션 & API:**

| 항목 | 설명 |
|------|------|
| Auto Play On Enter | Play On Awake가 꺼져 있을 때 범위 진입 시 자동 재생 (기본 ON) |
| Use Listener As Target | 태그 대상 대신 활성 AudioListener 위치로 음량 계산 (3인칭·탑다운용, 기본 OFF) |
| Use Unscaled Time | `Time.timeScale = 0`에서도 페이드 진행 |
| `Play()` / `Stop()` | `Stop()` 후에는 `Play()`를 부를 때까지 범위 안에 있어도 다시 재생하지 않음 (Play On Awake가 켜져 있으면 오브젝트를 다시 활성화할 때 처음부터 재생) |
| `SetClip(clip, play = false)` | 재생 상태를 유지한 채 클립 교체 |

> 💡 Volume Size와 Fade Distance는 **로컬 단위**라 오브젝트 스케일이 적용됩니다. 실행 중에 Clip·Loop·Min/Max Distance·Output Group을 바꾸면 바로 반영됩니다.

</details>

---

<!-- ────────────────── 애니메이션 & 이펙트 ────────────────── -->

<details>
<summary>🎬 <b>Animation Inspector Controller</b> - 애니메이션 미리보기</summary>

<br>

**에디터와 Play Mode에서 애니메이션을 미리보고, 프레임 이벤트와 자동 트랜지션을 설정할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 에디터 애니메이션 미리보기 | Play Mode 없이 실시간 미리보기 (Reverse, Start/End Frame 구간 반영) |
| Play Mode 제어 | **Playback (Runtime)** 패널에서 재생/일시정지/정지, 현재 프레임·상태 표시, 프레임 이동 |
| 단축키 | 인스펙터 포커스 시 `Space` 재생/일시정지, `←` / `→` 한 프레임 이동 |
| 클립 빠른 전환 | 그리드 UI로 모든 클립 확인 및 원클릭 전환 (Play Mode에서는 실제 재생 클립도 교체) |
| 클립 숨기기 | 사용하지 않는 클립을 숨겨서 작업 공간 정리 (에디터 환경설정에 저장, 씬을 dirty로 만들지 않음) |
| 프레임 이벤트 | 특정 프레임에 UnityEvent 트리거 설정, 이벤트별 적용 클립 지정 가능 (비우면 모든 클립) |
| 자동 트랜지션 | 애니메이션 완료 후 지정 시간 뒤 다음 State로 자동 전환 (Pause / Resume) |
| Playback 설정 | 재생 속도(0~4x, 0은 제자리 정지), 루프, 역재생, 시작/종료 프레임 지정 |

**탭 구성:**

| 탭 | 설명 |
|------|------|
| 🔄 Workspace → Transitions | 애니메이션 종료 시 자동 전환될 State 설정 (Tag 필터, Delay, Blend 지원) |
| ⚡ Workspace → Frame Events | 특정 프레임에 UnityEvent 바인딩 |
| ⚙️ Settings | Playback 속도, 루프, 역재생, 프레임 범위, 초기 프레임 설정 |

> ⚠️ 루프가 꺼진 재생은 EndFrame(역재생이면 StartFrame)에서 멈추고 상태가 **Stopped**가 됩니다.
>
> Animation 창이나 Timeline이 미리보기/녹화 중이면 끄지 않고 경고만 표시합니다.

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
| 멀티 파트 | 대상 아래의 MeshFilter·SkinnedMeshRenderer를 최대 32개 파트까지 잔상으로 그림 (Skinned는 현재 포즈 반영) |
| 프로파일 시스템 | ScriptableObject 프로파일로 설정 재사용 및 오버라이드 |
| 멀티 메쉬 병합 | 여러 TrailEffect를 자동으로 부모-자식 병합 |
| Fresnel 효과 | 가장자리 강조 Fresnel Power/Intensity 지원 |
| 스텐실 겹침 방지 | PreventOverlap으로 잔상 간 겹침 제거 |
| VR | Single-Pass Instanced에서 양쪽 눈에 모두 그림 |

**Trail 모드:**

| 모드 | 설명 |
|------|------|
| 🎨 Color | 메쉬를 복제하여 색상 잔상 생성 (Scale/Fresnel 지원) |
| 🖼️ TextureStamp | 빌보드 텍스처를 궤적에 배치 (Follow Chain / Trail 스타일) |

**주요 설정:**

| 설정 | 설명 |
|------|------|
| Target Mesh Filter / Search Root | 잔상으로 그릴 대상과 탐색 루트 |
| Duration | 잔상 지속 시간 (0.05~5초) |
| Snapshots Per Second | 초당 스냅샷 캡처 횟수 (1~60) |
| Color Over Lifetime | Gradient로 수명에 따른 색상 변화 |
| Scale Start / End | 잔상 시작/종료 크기 비율 |
| Max Snapshots | 최대 스냅샷 수 (4~128) |
| Min Distance | 스냅샷 간 최소 거리 |
| Use Unscaled Time | `Time.timeScale = 0`에서도 잔상 진행 (기본 OFF) |

**머티리얼:**
- 에디터에서 컴포넌트를 추가하면 기본 머티리얼 `Runtime/TrailEffect/Materials/TrailFX_Default.mat`이 자동 연결됩니다
- 런타임에 `AddComponent`로 붙였다면 `TrailMaterial` 프로퍼티, `SetMaterial()` 또는 프로파일의 Trail Material로 지정하세요
- 머티리얼이 없거나 셰이더가 `TelleR/Trail`이 아니거나 GPU Instancing이 꺼져 있으면 인스펙터에 경고와 수정 버튼이 표시됩니다

**프로파일 생성:**
- `Assets → Create → TelleR → Trail Profile` 로 프로파일 에셋 생성
- 컴포넌트에 프로파일 할당 후 필드별 오버라이드 가능

> ⚠️ `TelleR/Trail` 셰이더에는 **URP용과 Built-in RP용 SubShader가 모두** 들어 있습니다. URP SubShader는 URP 패키지가 설치된 경우에만 컴파일되므로 Built-in 프로젝트에서도 셰이더 에러가 나지 않습니다.
>
> GPU Instancing이 필요합니다(컴퓨트 셰이더는 쓰지 않아 WebGL2·GLES3에서도 동작). 지원하지 않는 기기에서는 경고를 한 번 남기고 효과를 끕니다.

</details>

---

<!-- ────────────────── 스프라이트 & UI ────────────────── -->

<details>
<summary>✂️ <b>Auto Sprite Slicer</b> - 스프라이트 일괄 변환</summary>

<br>

**여러 이미지의 배경을 제거·트림하고 한 번에 Single Sprite 타입으로 변환할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 일괄 변환 | 여러 이미지를 한 번에 Sprite (Single) 타입으로 설정 |
| 배경 제거·트림 | 배경색 제거와 투명 여백 트림 (이미 투명 픽셀이 있으면 배경색 제거는 건너뜀) |
| 드래그앤드롭 | 이미지 파일 또는 폴더를 드래그하여 추가, 파일별 체크박스·썸네일·미리보기 |
| 폴더 드롭 | 기본으로 이미 Sprite인 텍스처만 추가 (`Include Non-Sprite Textures From Folders`로 전체 추가) |
| 다양한 포맷 | PNG, JPG, TGA, BMP, PSD, GIF, HDR, EXR, TIF 지원 |

**자동 설정:**
- Texture Type → Sprite (2D and UI)
- Sprite Mode → Single
- Alpha Is Transparency → ✅
- 새로 Sprite로 바뀌는 텍스처에만 **New Sprite PPU / New Sprite Pivot** 적용
- 이미 Sprite인 텍스처는 PPU·피벗 유지 (`Keep Pivot Position`이 켜져 있으면 트림 후에도 같은 픽셀 위치의 Custom 피벗으로 이동)
- 9-slice 테두리는 잘린 만큼 보정

> ⚠️ **원본 파일을 덮어씁니다.** `Modify Image Files`가 켜져 있으면 PNG/JPG/TGA 원본 이미지를 배경 제거·트림 결과로 직접 덮어씁니다(JPG는 재압축). 끄면 임포터 설정만 바꿉니다.
>
> ⚠️ 기본으로 켜진 `Backup Originals`가 덮어쓰기 전에 원본과 .meta를 **프로젝트 루트의 `TelleRBackups/AutoSpriteSlicer/<시각>/`** 에 복사하며, **Restore Last Backup...** 으로 되돌릴 수 있습니다. 이 폴더는 Assets 밖에 있고 자체 `.gitignore`가 들어 있으며, 필요 없으면 지워도 됩니다.

노멀맵·라이트맵·큐브맵, Multiple / Polygon 스프라이트, ScriptedImporter를 쓰는 파일(PSD Importer로 가져온 .psb 포함)은 자동으로 건너뛰고 사유를 보고서에 표시합니다. 스크립트에서는 `AutoSpriteSlicer` 클래스(DryRun / Process)로 같은 처리를 할 수 있습니다.

</details>

---

<details>
<summary>🗂️ <b>UI Atlas Builder</b> - SpriteAtlas 빌드 도구</summary>

<br>

**Canvas/프리팹/스프라이트/텍스처/폴더를 드래그앤드롭하여 SpriteAtlas를 빌드하거나 업데이트할 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 아틀라스 생성 | 새 SpriteAtlas 에셋 생성 (Sprite Atlas V1 `.spriteatlas` / V2 `.spriteatlasv2` 모두 지원) |
| 아틀라스 업데이트 | **Update Mode** — Merge(기본, 기존 packable 유지하고 새 항목만 추가) / Replace(제거될 항목을 확인한 뒤 교체) |
| 폴더 처리 | **Folder Entries** — 폴더를 packable로 등록(AddFolderAsPackable) 또는 안의 스프라이트를 개별 수집(CollectSprites) |
| 스프라이트 수집 | Image, Button 등 Selectable의 상태 스프라이트, SpriteRenderer 스프라이트 수집 |
| 중복 방지 | **Skip Sprites In Other Atlases** — 다른 아틀라스에 이미 있는 스프라이트는 건너뜀 |
| 자동 패킹 | **Pack After Build** 옵션 |

**사용 방법:**
1. `Tools → TelleR → UI Atlas Builder` 열기
2. 드롭 영역에 Canvas / 프리팹 / 스프라이트 / 텍스처 / 폴더 드래그앤드롭
3. Atlas Asset Path 확인/수정
4. **Build / Update Atlas** 클릭

> 💡 Assets/ 밖의 스프라이트(내장 UISprite, 패키지 스프라이트)는 자동으로 제외됩니다. uGUI 패키지가 없으면 Canvas / Image 스캔은 쓸 수 없고, 스프라이트·텍스처·폴더 처리는 그대로 동작합니다.

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
| 디스크 용량 절약 | Assets, Packages, ProjectSettings를 심볼릭 링크/정션으로 공유 (Library만 복사) |
| 빠른 복제 생성 | robocopy(Windows) / rsync(Mac)로 Library 폴더 고속 복사, 진행 표시줄에서 취소 가능 |
| 클론 관리 | 최대 10개 클론 생성/삭제/열기, 열려 있는 클론과 불완전한 클론은 배지로 표시 |

**주요 기능:**
- 🏷️ 클론 프로젝트에서는 "CLONE PROJECT" 표시로 구분
- 🖥️ Scene 뷰에 "Running as CLONE Mode" 오버레이 표시
- 🔒 원본 프로젝트에서만 클론 관리 가능

> ⚠️ **클론은 원본과 Assets, Packages, ProjectSettings를 공유합니다.** 클론에서 에셋이나 설정을 수정·삭제하면 원본에도 즉시 반영되며, 읽기 전용 보호는 없습니다. 클론을 삭제하면 링크만 해제되고 원본은 보존됩니다.
>
> 복사 중에는 클론 폴더에 작업 파일(`.clone_pending`, `.clone_exitcode`, `.clone_copy.log`, `.clone_copy.cmd`)이 생겼다가 완료 시 지워집니다.

</details>

---

<details>
<summary>📦 <b>UPM Package Creator</b> - UPM 패키지 생성 도구</summary>

<br>

**자신의 코드를 Unity Package Manager 패키지로 만들어 Git URL로 배포하고 싶을 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 새 패키지 생성 | package.json, README.md, LICENSE.md, .gitignore 자동 생성 |
| 기존 패키지 불러오기 | 이미 만든 패키지를 불러와서 수정 (UI가 다루지 않는 package.json 키와 순서는 그대로 보존) |
| 기능 폴더 추가 | Editor/Runtime 폴더 구조 및 .asmdef 자동 생성 (상위에 asmdef가 있으면 만들지 않음) |
| 스크립트·에셋 추가 | 드래그앤드롭으로 기존 파일을 패키지에 복사 (Editor/Runtime 자동 분류, 같은 이름은 덮어쓰기/건너뛰기 선택) |
| GUID 참조 유지 | 복사한 .mat/.asset/.controller/.prefab 등의 GUID 참조를 새 GUID로 다시 연결 |
| 네임스페이스 자동 변환 | com.company.package → Company.Package 형태로 자동 변환 |

**3단계 워크플로우:**

```
STEP 1 → 패키지 메타 정보 입력 (이름, 버전, 작성자 등)
STEP 2 → 기능 폴더 추가 및 스크립트 드래그앤드롭
STEP 3 → 버전 업데이트 및 개발 모드 전환
```

> ⚠️ **Dev Mode**는 `Packages/manifest.json`을 로컬 폴더(`file:`) 경로로 바꿉니다. 같은 드라이브면 상대 경로를 씁니다. 이 상태의 manifest.json은 커밋하지 말고, 배포 전에 Deploy(git URL)로 되돌리세요.

</details>

---

<!-- ────────────────── 성능 분석 ────────────────── -->

<details>
<summary>🔺 <b>Tris Profiler</b> 🆕 - 카메라에 보이는 삼각형·정점 수 측정</summary>

<br>

**지금 카메라에 실제로 그려지는 메시가 무엇이고, 어떤 오브젝트가 삼각형을 가장 많이 쓰는지 볼 때 사용합니다.**

| 상황 | 설명 |
|------|------|
| 폴리곤 예산 확인 | 카메라 절두체 안 MeshRenderer·SkinnedMeshRenderer의 삼각형·정점 합계와 오브젝트 수 표시 |
| 무거운 오브젝트 찾기 | 삼각형 수 내림차순 표(상위 5~50행), 비율(%), 정점, LOD 단계, 카메라 거리 |
| 같은 메시 묶어 보기 | **Group by Mesh**로 같은 메시를 쓰는 렌더러를 한 줄로 합산 |
| 바로 선택 | 행 클릭으로 선택, Ctrl(⌘)+클릭으로 추가·제외, **Select Top**으로 상위 행 전체 선택 |
| 기준 카메라 | 비워 두면 MainCamera, Camera 칸에 직접 지정, 또는 **Scene View** 카메라 기준(프리팹 모드에서는 프리팹 내용만) |

**정확도:**

| 항목 | 처리 |
|------|------|
| 머티리얼 슬롯 | 실제로 그려지는 서브메시만 계산(머티리얼이 더 많으면 마지막 서브메시를 다시 계산), 쿼드는 삼각형 2개 |
| LODGroup | 카메라 거리·LOD Bias·Maximum LOD Level로 활성 단계 추정, 컬링된 LOD는 제외 |
| 제외 대상 | 비활성·꺼진 렌더러, forceRenderingOff, 카메라 cullingMask 밖 레이어 |
| 반영 안 함 | 가림(Occlusion) 컬링, 레이어별 컬링 거리, Terrain·파티클·UI·스프라이트·라인 |

**갱신:** **Auto**를 켜 두면 창이 보이는 동안만 자동으로 다시 셉니다. 씬이 크면 간격이 0.5~5초 사이에서 자동으로 늘어납니다. 다른 탭 뒤에 가려진 창은 스크립트 리컴파일이나 에디터 재시작 뒤에도 씬을 훑지 않습니다. Auto가 꺼져 있어도 창을 열 때 한 번은 세고, 이후에는 **Refresh**로 다시 셉니다.

**메뉴:** `Tools → TelleR → Tris Profiler`

</details>

---

<!-- ────────────────── XR ────────────────── -->

<details>
<summary>🥽 <b>Device Manager / Foveation Starter</b> - Meta Quest 성능 설정</summary>

<br>

**Meta Quest 빌드에서 기종별 렌더링 설정과 Foveated Rendering을 자동으로 적용할 때 사용합니다.** Meta/Oculus SDK에는 의존하지 않습니다.

| 컴포넌트 | 설명 | 필요 조건 |
|----------|------|-----------|
| 📱 **Device Manager** | 기기 모델명(`SystemInfo.deviceModel`)으로 Quest 2 / Quest 3(3S 포함)을 판별해 기종별 URP MSAA 프로필 적용 (모델명으로 알 수 없을 때만 기기 이름을 보조로 사용) | URP, Android |
| 👁️ **Foveation Starter** | 실행 시 Foveated Rendering 레벨 적용. XR이 늦게 초기화되면 **XR Wait Timeout**(기본 5초) 동안 재시도하고, 끝내 실패하면 경고 로그 | XR 모듈, Unity 2022.2 이상 |

> 💡 필요 조건이 맞지 않는 환경에서는 인스펙터 HelpBox와 로그로 "적용되지 않음"을 알려 주며, 컴파일 에러는 나지 않습니다.

</details>

---

## 📋 요구사항

- **Unity** 2021.3 LTS ~ Unity 6.x
- 필수 외부 패키지 없음 (URP, uGUI, XR, FBX Exporter는 [선택 패키지](#-선택-패키지))

---

## 📄 라이선스

[MIT License](LICENSE.md)

---

<p align="center">
  Made with ❤️ by <b>TelleR</b>
</p>
