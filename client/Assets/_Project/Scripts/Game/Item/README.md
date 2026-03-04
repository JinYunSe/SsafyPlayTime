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
