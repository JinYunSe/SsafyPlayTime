# Item 디렉토리 구조

## Common
- `IItemRuntimeBridge.cs`: 런타임 로직 -> 외부 시스템 이벤트 브리지 인터페이스
- `ItemIds.cs`: 아이템 ID 상수
- `ItemModels.cs`: 아이템 공용 모델/요청 구조체

## Catalog
- `ItemCatalog.cs`: 아이템 정의/프리젠테이션 통합 모델
- `ItemCatalogLoader.cs`: CSV 로더 통합 및 교차 참조 검증
- `ItemRuntimeFactory.cs`: 기본 런타임 컨트롤러 생성 유틸

## Data
- `ItemCsvUtility.cs`: CSV 파싱/경로/타입 변환 공통 유틸
- `ItemMasterCsvLoader.cs`: ItemMaster 로딩
- `ItemPresentationTableCsvLoader.cs`: 프리젠테이션 테이블 로딩
- `SoundAssetTableCsvLoader.cs`: 사운드 테이블 로딩
- `VfxAssetTableCsvLoader.cs`: VFX 테이블 로딩

## Runtime
- `Controller/`: 아이템 상태/쿨다운/버프/사용 로직
- `Host/`: MonoBehaviour 진입점 및 이벤트 전달

## Runner
- `ItemGameplayRunner*.cs`: ItemScene 전용 실행/입력/표현/물리 연출

## Field
- `ItemFieldDrop.cs`: 필드 아이템 엔티티(아이템 ID/획득 상태)
- `ItemFieldDropSpawner.cs`: 필드 배치/드랍 오케스트레이션
- `ItemFieldPickupInteractor.cs`: 우클릭/외부 호출 기반 획득 인터랙션
- `ItemFieldSystemBootstrap.cs`: Host-Field 컴포넌트 자동 연결
- `ItemFieldCatalogProvider.cs`: 카탈로그 캐시 로더
- `ItemFieldDropFactory.cs`: 필드 드랍 오브젝트 생성기
- `ItemFieldPrefabResolver.cs`: 프리팹 경로 해석기
- `ItemFieldPositionUtility.cs`: 링 배치/지면 위치 계산 유틸

## Integration
- `ItemCharacterAutoAttachBootstrap.cs`: 캐릭터 프리팹 수정 없이 아이템 컴포넌트 자동 결합
- `ItemCharacterUseInteractor.cs`: 캐릭터 입력 기반 아이템 사용 호출
- `ItemCharacterHeldItemPresenter.cs`: 보유 아이템 손 장착 시각화
- `ItemCharacterBuffApplier.cs`: 버프 상태를 캐릭터 시각/배율 정보로 변환
- `ItemFieldInteractionService.cs`: 실게임 적용용 필드 스폰/획득/사용 서비스 API

## Test
- `ItemFieldDropDevTestInput.cs`: 개발 테스트 입력(1키 스폰, 우클릭 상호작용, F키 수동 드랍) 전용 컨트롤러
