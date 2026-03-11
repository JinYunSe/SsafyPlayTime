# Item Scene Bootstrap Guide

## 목적
- 다른 씬에서도 `Game/Item` 시스템을 바로 사용할 수 있도록 씬 공용 부트스트랩 적용 방법을 정리한다.
- 플레이어별 아이템 상태와 씬 공용 스킬 처리 책임을 분리해서 협업 시 혼선을 줄인다.

## 핵심 개념
- 플레이어별 상태
  - `ItemRuntimeHost`
  - 현재 손에 든 아이템
  - Consumable 버프 상태
  - 좌클릭 사용 / 드랍 / 장착 표현
- 씬 공용 상태
  - 블랙홀, 위성 공격 같은 씬 레벨 스킬 처리
  - 필드 아이템 스폰/픽업 보조
  - 우클릭 픽업 입력 연결

즉, `ItemSceneBootstrap.prefab`은 씬 공용 매니저이고, 플레이어 인벤토리를 직접 들고 있지 않는다.

## 사용 대상
- `NetworkPlayer` 기반 캐릭터가 존재하는 씬
- `_Project/Prefabs/Items` 아래 아이템 프리팹을 씬에 직접 배치해서 사용하려는 경우
- `ItemScene`이 아닌 일반 게임 씬

## 사용 프리팹
- 씬 부트스트랩:
  - `Assets/_Project/Prefabs/System/ItemSceneBootstrap.prefab`
- 아이템 프리팹:
  - `Assets/_Project/Prefabs/Items/BlackholeBomb.prefab`
  - `Assets/_Project/Prefabs/Items/GrowthItem.prefab`
  - `Assets/_Project/Prefabs/Items/ShrinkItem.prefab`
  - `Assets/_Project/Prefabs/Items/InvisibilityItem.prefab`
  - `Assets/_Project/Prefabs/Items/Americano.prefab`
  - `Assets/_Project/Prefabs/Items/SatelliteStrike.prefab`
  - `Assets/_Project/Prefabs/Items/WaterMelonSword.prefab`

## 적용 방법
1. 사용할 씬을 연다.
2. `Assets/_Project/Prefabs/System/ItemSceneBootstrap.prefab`을 씬에 배치한다.
3. 테스트하거나 사용할 아이템을 `Assets/_Project/Prefabs/Items` 폴더에서 씬에 배치한다.
4. 씬에 플레이어 프리팹이 `NetworkPlayer`를 포함하고 있는지 확인한다.
5. 플레이 후 우클릭으로 픽업, 좌클릭으로 사용되는지 확인한다.

## 동작 방식
- `ItemSceneBootstrap`은 씬에서 로컬 플레이어를 찾는다.
- 찾은 플레이어의 `ItemRuntimeHost`를 `ItemGameplayRunner`에 연결한다.
- 그래서 플레이어는 기존처럼 자기 아이템 상태를 유지하고, 씬은 블랙홀/위성 같은 스킬 이벤트만 받아 처리한다.

## 입력 규칙
- 우클릭
  - 빈손일 때 가까운 필드 아이템 픽업
  - 손에 물리 오브젝트를 들고 있으면 기존 throw 입력 우선
- 좌클릭
  - 손에 든 아이템 사용
  - 아이템이 없으면 기존 공격/행동 흐름 유지
- `F`
  - 현재 들고 있는 아이템 드랍

## 아이템별 기대 동작
- `GrowthItem`
  - 픽업 후 사용 시 성장 버프 적용
- `ShrinkItem`
  - 픽업 후 사용 시 축소 버프 적용
- `InvisibilityItem`
  - 픽업 후 사용 시 본인 화면 반투명, 다른 플레이어 화면 비표시
- `Americano`
  - 픽업 후 사용 시 보호막 + 슈퍼아머 적용
- `WaterMelonSword`
  - 픽업 후 사용 시 근접 스윙 처리
- `BlackholeBomb`
  - 픽업 후 사용 시 씬에 블랙홀 스킬 실행
- `SatelliteStrike`
  - 픽업 후 사용 시 조준 지점에 위성 공격 실행

## 주의사항
- `ItemSceneBootstrap.prefab`은 씬에 1개만 두는 것을 권장한다.
- 이 프리팹에 플레이어별 `ItemRuntimeHost`를 따로 추가하지 않는다.
- 플레이어가 `NetworkPlayer`가 아니면 자동 바인딩이 실패할 수 있다.
- `Flamethrower`는 현재 작업 범위 제외이므로 이 가이드 대상에서 제외한다.
- 다른 작업자가 캐릭터 프리팹 구조를 바꿀 경우 `NetworkPlayer` 유지 여부를 먼저 확인한다.

## 권장 테스트 절차
1. 씬에 `ItemSceneBootstrap.prefab` 배치
2. 아이템 프리팹 2~3종 배치
3. 플레이어로 우클릭 픽업 확인
4. 좌클릭 사용 확인
5. `BlackholeBomb`, `SatelliteStrike` 사용 시 씬 효과 실행 확인
6. `Growth`, `Shrink`, `Invisibility`, `Americano` 사용 시 캐릭터 버프 반영 확인

## 문제 발생 시 확인 순서
1. 씬에 `ItemSceneBootstrap.prefab`이 배치되어 있는가
2. 플레이어에 `NetworkPlayer`가 붙어 있는가
3. 아이템 프리팹 루트에 `ItemFieldDrop`가 붙어 있는가
4. `ItemTable.csv`의 `prefabPath`가 유효한가
5. Unity 콘솔에 compile error가 없는가
6. 씬 안에 `ItemSceneBootstrap`가 중복 배치되지 않았는가
