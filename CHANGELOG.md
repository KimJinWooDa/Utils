# Changelog

TelleR Utilities의 주요 변경 사항을 기록합니다. 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따릅니다.

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
