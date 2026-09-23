# Changelog

TelleR Utilities의 주요 변경 사항을 기록합니다. 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따릅니다.

## [1.2.0] - 2026-09-23

### Added
- **Concave Mesh Collider** (신규, `Tools → TelleR → Concave Mesh Collider`) — 오목한 메시(MeshFilter·SkinnedMeshRenderer·본 아래 강체 메시)를 볼록 MeshCollider 여러 개로 나눠 복합 콜라이더 생성. 꼭 맞는 조각은 Box/Sphere/Capsule 사용, 스킨드 조각은 해당 본 아래에 생성
  - 창: 대상 목록(Follow Selection, Add Selection, 드래그 앤 드롭), Auto/Advanced 분해 설정, 출력 옵션(Asset Folder, Is Trigger, Physics Material, Layer, 기존 콜라이더 끄기), Preview / Generate / Remove Generated, 대상별 결과 리포트(Cover / Out 열)
  - 새 컴포넌트 `TelleR/Concave Collider Root`와 인스펙터(Rebuild, Remove, Select Pieces 등), MeshFilter/SkinnedMeshRenderer 컴포넌트 메뉴와 `GameObject → TelleR → Generate Concave Collider` 메뉴
  - 생성·제거는 대상마다 Undo 1회, 진행 막대 취소 지원. 헐 메시는 대상마다 .asset 하나로 저장하고 재생성 시 같은 GUID를 갱신하며, 다른 씬·프리팹이 쓰는 에셋은 덮어쓰거나 지우지 않음
  - Auto는 Balanced 품질에 조각 수(4~12)만 자동 조정. 컵·링처럼 깊게 파인 안쪽은 일부 메워질 수 있으며, 결과의 Out이 높으면 Auto를 끄고 Precise·더 많은 Max Pieces로 다시 생성
- **Tool Hub** — 카테고리 헤더, 한국어 설명, 검색창과 함께 13개 도구 표시. 컴포넌트 도구는 `Add to Selection`(Undo 가능), 필요한 선택 패키지·Unity 버전이 없으면 요구 사항 표시
- **Trail Effect** — Built-in RP용 SubShader, 기본 머티리얼 `TrailFX_Default.mat`(에디터에서 추가 시 자동 연결), 최대 32개 MeshFilter·SkinnedMeshRenderer 파트 지원, Target 섹션(Target Mesh Filter, Search Root), `Use Unscaled Time` 옵션, 머티리얼 문제 경고와 수정 버튼(Undo 가능)
- **Trail Effect** 공개 API — `TrailMaterial`, `SetMaterial()`, `ResolvedMaterial`, `IsInitialized`, `TargetPartCount`, `RefreshTargets()`, `GetTargets()`, `TrailEffectProfile.TrailMaterial`
- **AudioVolume3D** — `SetClip(clip, play = false)`, `Auto Play On Enter`(기본 ON, 기존 동작), `Use Listener As Target`, `Use Unscaled Time` 옵션, 다중 오브젝트 편집, 모든 필드 한국어 툴팁
- **Animation Inspector Controller** — Play Mode용 `Playback (Runtime)` 패널, 단축키(Space 재생/일시정지, ←/→ 프레임 이동), 자동 전환 `Resume` 버튼, Controller 교체 시 자동 갱신과 Refresh 버튼
- **Animation Inspector Controller** — FrameEvent에 선택 직렬화 필드 `Clip` 추가(비우면 모든 클립). 기존 이벤트는 그대로 모든 클립에서 동작하고, 인스펙터에서 새로 추가하는 이벤트는 현재 클립 전용
- **Mesh Pivot Tool** — Apply & Remove에서 "씬에만 저장" / "에셋으로 저장"(.asset) 선택, 프리팹 모드·인스턴스별 경고, 작업 메시가 사라진 경우 Revert 복구 안내, 공개 속성 `MeshPivotTool.HasSourceMesh`
- **FBX Backup** — `Save as .asset` 버튼, FBX Exporter가 없으면 `Open Package Manager` 버튼
- **Auto Sprite Slicer** — `Backup Originals`(기본 ON, 프로젝트 루트 `TelleRBackups/AutoSpriteSlicer/<시각>/`), `Open Backup Folder`, `Restore Last Backup...`, `Keep Pivot Position`, `Modify Image Files`, `New Sprite PPU / Pivot`, `Include Non-Sprite Textures From Folders`, 파일별 체크박스·썸네일 목록, 체커보드 미리보기, `Reset Advanced` / `Clear Report`, 스크립트용 `AutoSpriteSlicer` 클래스(DryRun / Process)
- **UI Atlas Builder** — Sprite Atlas V2(`.spriteatlasv2`) 지원, Update Mode(Merge 기본 / Replace), Folder Entries(폴더 packable / 스프라이트 수집), Button 상태 스프라이트·SpriteRenderer 수집, 다른 아틀라스 중복 보고·건너뛰기
- **Fast Clone** — 복사 취소, Refresh 버튼, "열림"·"불완전" 배지, 원본과 공유된다는 상단 안내
- **Foveation Starter** — XR이 늦게 초기화돼도 `XR Wait Timeout`(기본 5초) 동안 재시도
- **Skinned Mesh Collider** — 저장 전 파일명·경로 검사, 같은 이름 메시는 GUID를 유지하며 교체, `MeshDecimator.Simplify(int, Func<float,bool>)`와 `WeldByPosition` 오버로드(추가형 API)
- **UPM Package Creator** — 파일 추가 시 충돌 목록과 "모두 덮어쓰기 / 충돌 건너뛰기 / 취소" 선택(GUID 유지), 복사한 텍스트 에셋의 GUID 참조 재연결, 패키지 이름 즉시 검증
- 선택 패키지(URP, uGUI, XR, FBX Exporter)를 asmdef versionDefines로 자동 감지. 없으면 해당 기능만 HelpBox·로그로 안내하고 설치를 요구하지 않음
- `package.json`에 license, keywords, description 추가
- Light/Dark 스킨 모두에서 읽히는 공용 에디터 스타일(TelleRGUI) 적용

### Fixed
- **패키지 설치 실패** — 빌트인 모듈(animation, audio, imageconversion, imgui, jsonserialize, physics, uielements)을 꺼 둔 프로젝트에서 컴파일 에러가 나던 문제. `package.json`에 모듈 의존성을 선언
- **Trail Effect** — URP 없는 Built-in 프로젝트의 셰이더 에러, VR Single-Pass Instanced에서 왼쪽 눈에만 보이던 문제, WebGL2·GLES3 미동작(StructuredBuffer 제거), Scene 뷰·분할 화면에서 스탬프가 눕던 문제, 런타임 AddComponent 시 머티리얼 지정 불가
- **AudioVolume3D** — `Stop()` 후 범위 안에서 다시 재생되던 문제, 부모 스케일이 클 때 볼륨 안에서 소리가 안 나던 문제, 실행 중 Clip·Loop·Distance·Output Group 변경 미반영, Inner Volume Falloff 0에서 NaN
- **Animation Inspector Controller** — Play Mode에서 라벨과 실제 재생 클립이 어긋나던 문제, 블렌드 전환 직후 즉시 루프·완료되거나 초반 이벤트를 건너뛰던 문제, 일시정지 후 이벤트 재발사, 루프 wrap 구간 이벤트 누락, 틱 간격에 따라 루프 주기가 틀어지던 문제, 컴포넌트 재활성화 시 재생 상태 유실, 트랜지션 '×' 삭제 시 GUI Layout 오류
- **Mesh Pivot Tool** — Revert가 메시만 되돌리던 문제(위치·회전·자식·콜라이더 center까지 복원), 드래그 중 매 프레임 적용으로 고폴리에서 끊기고 Undo가 쪼개지던 문제, Scene 뷰 R 키 가로채기, Ctrl+D 복제본이 작업 메시를 공유하던 문제, 'Begin Pivot Edit' Undo 후 곧바로 재시작되던 문제, 회전 시 블렌드셰이프 미회전, WORLD ALIGN 반복 시 결과가 달라지던 문제
- **FBX Backup** — `.asset`·FBX·ProBuilder·임시 메시에도 뜨던 잘못된 경고, FBX Exporter 없이 보이던 동작하지 않는 버튼, 기본 인스펙터의 Edit Bounds·프리뷰가 동작하지 않던 문제, 다중 선택 SkinnedMeshRenderer의 Bounds 표시
- **Skinned Mesh Collider** — 90%·50% 프리셋이 목표 삼각형 수까지 줄지 않던 문제, UV·노말 이음새의 구멍, 작은 닫힌 조각이 납작하게 접히던 문제, 65535 정점 초과 메시 잘림, 미리보기와 결과 좌표 불일치, 미리보기 메시 메모리 누수
- **Auto Sprite Slicer** — 4K·NPOT 이미지가 임포터 최대 크기·압축 때문에 줄어들거나 손상된 채 덮어써지던 문제(원본을 디스크에서 무손실로 읽음), 트림 후 씬 오브젝트가 움직이던 문제(피벗 유지), 9-slice 테두리 어긋남, 변경이 없어도 파일을 다시 써 VCS 변경이 생기던 문제, 한 파일 실패 시 전체 중단
- **UI Atlas Builder** — uGUI 없는 프로젝트의 컴파일 에러, `x.spriteatlasv2.spriteatlas` 이중 확장자, 반복 V2 빌드 메모리 누수
- **Fast Clone** — 에디터가 잠근 Library 파일 때문에 복사가 사실상 끝없이 멈추던 문제(robocopy `/R:1 /W:1`), 도메인 리로드 후 진행 중인 복사 추적 유실, 열린 클론 재실행·삭제 허용, macOS 실행 경로와 경로 따옴표 처리(Windows 외 미검증)
- **UPM Package Creator** — package.json 저장 시 dependencies·samples 등 UI 밖 키가 사라지던 문제, `.renderTexture`·`.overrideController` 미인식, prerelease 접미사가 Bump에서 사라지던 문제, git@/ssh:// URL 미인식, manifest.json 쓰기 실패 시 반쯤 쓰인 파일이 남던 문제(원래 내용으로 복원)
- **Device Manager** — 이름을 바꾼 Quest에 잘못된 MSAA 프로필이 적용되던 문제(모델명 `SystemInfo.deviceModel` 우선 판별)
- Windows 에디터에서 네모로 깨지던 이모지를 BMP 기호로 교체

### Changed
- **메뉴 경로 통일** — 모든 도구를 `Tools/TelleR/<Tool>` 아래로 이동: Tool Hub(맨 위), Concave Mesh Collider, Skinned Mesh Collider(이전 `Tool/Skinned Mesh Collider Creator`), Auto Sprite Slicer, UI Atlas Builder(이전 `Tool/AtlasBuilder`), Fast Clone(이전 `Clones Manager`), UPM Package Creator
- **Add Component 경로** — `TelleR/<컴포넌트 이름>`으로 통일 (Audio Volume 3D, Animation Inspector Controller, Trail Effect, Trail Debug Oscillator, Mesh Pivot Tool, Device Manager, Foveation Starter)
- **Animation Inspector Controller** — 루프가 꺼진 재생은 EndFrame(역재생이면 StartFrame)에서 멈추고 상태가 **Stopped**가 되어 `OnPlayStateChanged(Stopped)`가 발사됨(이전에는 Playing 상태로 클립 끝까지 재생). 이를 전제로 한 스크립트는 확인 필요
- **Animation Inspector Controller** — Play Mode에서 비활성 컴포넌트에 호출한 Play/Pause/Stop/JumpToFrame/ChangeClip은 기록만 하고 다시 활성화될 때 적용(이전에는 비활성 중에도 그래프를 만들어 Animator가 그 포즈에 고정됨)
- **Animation Inspector Controller** — 삭제된(Missing) 클립 전용 이벤트는 더 이상 모든 클립에서 발사되지 않음(에디터 경고 표시), 재생 속도 0은 제자리 정지, 숨긴 클립 목록은 컴포넌트 대신 에디터 환경설정에 저장(씬·프리팹 dirty 없음, 기존 값은 1회 이전), Edit Mode 미리보기가 Animation 창·Timeline 미리보기를 끄지 않음
- **Mesh Pivot Tool** — Scale이 비균등하면 피벗 회전을 막고 안내 표시, 편집 중 기본 Move/Rotate 기즈모를 숨기고 Scene 뷰 Gizmos를 꺼도 피벗 핸들 표시
- **Skinned Mesh Collider** — Convex 기본값 OFF(EditorPrefs에 기억), Convex를 켜면 Concave Mesh Collider 안내
- **Auto Sprite Slicer** — 폴더 드롭 시 기본으로 이미 Sprite인 텍스처만 추가, 노멀맵·라이트맵 등은 자동 제외, 이미 Sprite인 텍스처는 PPU·피벗 유지
- **UPM Package Creator** — 기본값에서 작성자 전용 값을 없애고 예시 placeholder 표시(Min Unity 2021.3), Dev Mode는 같은 드라이브면 상대 `file:` 경로 사용, 새 패키지 안내창 2개를 1개로 통합
- 도움말·대화상자·로그를 한국어로 통일하고 로그 접두사를 `[TelleR/<ToolName>]`로 맞춤

## [1.1.1] - 2026-08-13

### Fixed
- **비 VR 프로젝트 설치 실패** — XR 빌트인 모듈(`com.unity.modules.xr`)이 없는 프로젝트에서 `FoveationStarter`가 컴파일 에러를 일으키던 문제. asmdef versionDefines(`TELLER_XR`) 가드를 추가해 XR 모듈이 없어도 패키지 전체가 정상 컴파일됩니다
- **Unity 2022.2 미만 호환** — `XRDisplaySubsystem.foveatedRenderingLevel`은 Unity 2022.2+ 전용 API라 최소 지원 버전(2021.3)에서 컴파일이 깨지던 문제. 버전 가드를 추가하고, 미지원 환경에서는 경고 로그만 출력합니다

## [1.1.0] - 2026-07-20

### Added
- **Tool Hub** (`Tools → TelleR → Tool Hub`) — 패키지의 모든 도구 목록·설명·바로가기를 한 창에 모음
- **UPM Package Creator** — Deploy 전환용 Git URL 입력 필드, 전환 전 체크리스트 다이얼로그, packages-lock 자동 갱신(전환 후 최신 커밋 반영)
- **UI Atlas Builder** — 창 내 사용법 안내, 빌드 결과 리포트, 기존 packables 교체 전 확인
- **AudioVolume3D** — 공개 `Play()`/`Stop()` API, 늦게 스폰되는 Player 자동 재탐색
- Package Manager에 Documentation / Changelog 링크 표시

### Fixed
- **Auto Sprite Slicer** — EXR 원본이 8bit로 파괴되던 문제(덮어쓰기 대상에서 제외), Multiple 스프라이트시트의 수작업 슬라이스가 삭제되던 문제(자동 보호 스킵), 투명 배경 이미지에서 오브젝트 픽셀이 침식되던 문제, Linear 색공간에서 재저장 색 왜곡
- **UPM Package Creator** — 기능명 "Resources" 삭제 시 리소스 루트 전체가 지워지던 문제, 파일 이동 시 무확인 덮어쓰기, 따옴표 입력 시 package.json 파손, 빈 dependencies에서 manifest.json 파손, 사용자가 수정한 asmdef가 템플릿으로 초기화되던 문제
- **Mesh Pivot Tool** — 핸들 드래그 시 메시가 폭주하던 문제, 피벗 대신 메시가 월드에서 움직이던 문제(transform 보정), 회전 시 셰이딩 파괴(노말 이중 회전), 콜라이더 center 미보정으로 콜라이더가 어긋나던 문제, Undo 미기록으로 원본 링크가 유실되던 문제
- **Skinned Mesh Collider** — 스케일된 캐릭터에서 콜라이더가 제곱 크기가 되던 문제(BakeMesh useScale), 단일 메시 경로의 공간 불일치, 덮어쓰기 시 GUID가 바뀌어 기존 참조가 깨지던 문제(내용 교체로 변경), CharacterController까지 삭제되던 교체 옵션
- **FBX Backup** — 다중 메시 계층에서 엉뚱한 메시로 교체되던 문제(이름 매칭), SkinnedMeshRenderer 파손 방지(백업 파일만 생성), 교체 전 확인 다이얼로그
- **Animation Inspector Controller** — Transitions 탭을 열기만 해도 TargetState가 덮어써지던 문제, Synced Layer에서 인스펙터가 죽던 문제, "+ Add Event"가 이전 이벤트 리스너를 복제하던 문제, 기본 설정에서 루프가 동작하지 않던 문제, 프레임 이벤트 오발사·2바퀴째 침묵·시작 프레임 미발사, 빌드에서 자동 전환이 실패하던 문제 보완(클립명 폴백)
- **Trail Effect** — 3개 이상일 때 일부 트레일이 그려지지 않던 문제(병합 체인), MaxSnapshots < StampCount일 때 매 프레임 예외, 런타임 생성 시 그라디언트 NRE
- **AudioVolume3D** — 씬 시작 시 원거리 볼륨이 최대 음량으로 터지던 문제, PlayOnAwake를 끄면 영구 무음이 되던 문제
- **Fast Clone** — 열려 있는 클론을 삭제하면 잔해가 무보고로 남던 문제, 클론 생성 중복 실행, "Read-Only" 오해 문구(실제로는 원본과 공유됨을 명시)
- 신규 설치 시 컴파일 실패(Editor asmdef의 UnityEngine.UI 참조 누락), URP 없는 프로젝트에서 설치 실패(versionDefines 가드)

### Changed
- 애니메이션 클립 선택 시 자동 재생 제거 — 선택은 0프레임 미리보기만, 재생은 Play 버튼
- SkinnedMesh 피벗 편집은 bindpose 보정 방식으로 변경 (씬 핸들 대신 프리셋·버튼·버텍스 스냅)
- SkinnedMeshCollider의 "기존 콜라이더 교체"는 MeshCollider만 대상

## [1.0.25] 이전
- 변경 기록 없음 (git 커밋 히스토리 참조)
