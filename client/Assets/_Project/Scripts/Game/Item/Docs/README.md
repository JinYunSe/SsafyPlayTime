# Item System Guide

## 목적
- 아이템 시스템의 책임을 `Character / World / Runtime / Dev / Data` 기준으로 분리한다.
- 협업 시 "어디를 수정해야 하는지"를 폴더 이름만 보고 판단할 수 있게 만든다.
- namespace와 public API는 유지해서 기존 씬/프리팹/런타임 연결을 깨지 않는다.

## 최상위 구조
- `Catalog/`
  - CSV 로더 결과를 합쳐서 실제 런타임 카탈로그를 만든다.
- `Common/`
  - 아이템 공용 모델, ID, 브리지 인터페이스를 둔다.
- `Data/`
  - CSV 로더와 파싱 유틸을 둔다.
- `Runtime/`
  - 아이템 상태, 사용, 버프, 브리지 이벤트를 처리한다.
- `Character/`
  - 캐릭터 손 장착, 사용 입력, 버프 반영, 근접 타격 등 캐릭터 결합 코드를 둔다.
- `World/`
  - 필드 드랍, 획득, 스폰, 프리팹 해석, 월드 시각 보조를 둔다.
- `Dev/`
  - `ItemScene` 전용 러너, 디버그 입력, 테스트 보조를 둔다.
- `Docs/`
  - 구조 설명과 정리 계획 문서를 둔다.

## 폴더별 책임
### `Runtime/`
- `Controller/`
  - 아이템 상태 머신과 사용 진입점.
  - 새 아이템 동작 추가 시 공통 브리지 호출은 여기서 조합한다.
- `Modules/`
  - 아이템별 사용 모듈.
  - 새 아이템은 기본적으로 이 폴더에 새 모듈을 추가한다.
- `Host/`
  - MonoBehaviour 진입점.
  - 외부 시스템과 `Controller` 사이 이벤트 허브 역할.

### `Character/`
- `ItemCharacterAutoAttachBootstrap.cs`
  - 캐릭터 프리팹을 직접 수정하지 않고도 아이템 컴포넌트를 결합한다.
- `ItemCharacterUseInteractor.cs`
  - 캐릭터 입력에서 아이템 사용 요청을 보낸다.
- `ItemCharacterHeldItemPresenter.cs`
  - 보유 아이템의 손 장착 시각을 담당한다.
- `ItemCharacterMeleeSwingHandler.cs`
  - 장착 무기의 근접 스윙 판정을 만든다.
- `ItemMeleeDamageHitbox.cs`
  - 스윙 히트박스 충돌 전달 전용 컴포넌트.
- `ItemCharacterBuffApplier.cs`
  - 아이템 버프를 캐릭터 스케일/상태에 반영한다.
- `ItemFieldInteractionService.cs`
  - 실게임에서 필드 스폰/줍기/사용/드롭 흐름을 묶는 서비스.

### `World/`
- `ItemFieldDrop.cs`
  - 월드 아이템 엔티티.
- `ItemFieldDropFactory.cs`
  - 드랍 오브젝트 생성 책임.
- `ItemFieldDropSpawner.cs`
  - 월드 배치와 런타임 스폰 책임.
- `ItemFieldPickupInteractor.cs`
  - 필드 아이템 획득 인터랙션.
- `ItemFieldPrefabResolver.cs`
  - 테이블 경로를 실제 프리팹으로 변환.
- `ItemFieldCatalogProvider.cs`
  - 카탈로그 캐시 접근용.
- `ItemFieldPositionUtility.cs`
  - 배치 좌표 계산.
- `ItemBlackholeVisualAuthoring.cs`
  - 블랙홀 월드 시각 저작 보조.

### `Dev/`
- `Bootstraps/`
  - `ItemScene` 전용 자동 결합 보조.
- `Runner/Core/`
  - `ItemGameplayRunner` 생명주기, 이벤트, 입력, 씬 게이트.
- `Runner/Presentation/`
  - 오디오, 프리젠테이션, 데미지 어댑터.
- `Runner/Skills/`
  - 블랙홀, 화염방사기, 위성폭격 같은 스킬별 실행 로직.
- `Runner/Debug/`
  - 로컬 디버그 제어.
- `Testing/`
  - 개발 단축키 입력.

## 협업 규칙
1. 새 아이템 기능은 먼저 `Runtime/Modules/`에 모듈을 추가한다.
2. 캐릭터 결합 이슈는 `Character/`에서 해결한다.
3. 필드 드랍/획득/배치는 `World/`에서 해결한다.
4. `Dev/`는 `ItemScene` 검증용으로만 사용하고, 실게임 의존을 넣지 않는다.
5. 공통 모델 변경은 `Common/`에서만 한다.
6. CSV 스키마 변경은 `Data/`와 `Catalog/`를 함께 본다.

## 새 아이템 추가 순서
1. `Data/ItemTable.csv`에 아이템 정의를 추가한다.
2. 필요 시 `Runtime/Modules/`에 아이템 모듈을 추가한다.
3. `Runtime/Modules/ItemUseModuleRegistry.cs`에 모듈을 등록한다.
4. 캐릭터 표현이 필요하면 `Character/`를 수정한다.
5. 월드 드랍 특수 처리가 필요하면 `World/`를 수정한다.
6. `Dev/`의 `ItemScene`에서 회귀 테스트를 한다.

## 주의사항
- 파일 위치는 바뀌어도 namespace는 유지한다.
- public class 이름은 바꾸지 않는다.
- 씬/프리팹 참조가 걸린 스크립트는 meta를 유지한 채 이동한다.
- 개발용 입력과 실게임 입력을 같은 파일에 섞지 않는다.
