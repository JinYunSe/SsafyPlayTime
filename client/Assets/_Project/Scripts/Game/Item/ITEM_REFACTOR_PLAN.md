# Item 리팩토링 안전 계획

## 목적
- Item 관련 파일 정리 시 실게임 동작(획득/사용/손 시각화/버프 적용)이 깨지지 않도록 단계적으로 진행한다.
- 프로토타입 전용 경로와 프로덕션 경로를 분리해 유지보수성을 높인다.

## 진행 상태
- 1차(완료): 무호출 데드코드/미사용 이벤트 체인 제거
- 2차(완료): `Runner/`, `Test/`, `ItemFieldSystemBootstrap`를 `Prototype/` 하위로 물리 분리
- 3차(대기): Map Test Scene에서 자동 결합 기반 동작 검증
- 4차(대기): 프로토타입 최종 제거 여부 결정

## 현재 구조 요약
- 프로덕션 핵심 경로
  - `Integration/ItemCharacterAutoAttachBootstrap.cs`
  - `Runtime/Host/*`
  - `Runtime/Controller/*`
  - `Runtime/Modules/*`
  - `Integration/ItemCharacterUseInteractor.cs`
  - `Integration/ItemCharacterHeldItemPresenter.cs`
  - `Integration/ItemCharacterBuffApplier.cs`
  - `Field/ItemFieldPickupInteractor.cs`
- 프로토타입 성격이 강한 경로
  - `Prototype/Runner/ItemGameplayRunner*.cs` (ItemScene 전용)
  - `Prototype/Test/ItemFieldDropDevTestInput.cs` (개발 단축키 입력)
  - `Prototype/Field/ItemFieldSystemBootstrap.cs` (ItemScene 자동 결합 보조)

## 리스크
1. `Runner/*` 삭제 시 `ItemScene` 자동 부트스트랩이 동작하지 않음.
2. `ItemFieldDropDevTestInput.cs` 삭제 시 1키 스폰/우클릭 테스트 루프가 사라짐.
3. `ItemFieldSystemBootstrap.cs` 삭제 시 ItemScene에서 Host-Field 연결 누락 가능.
4. 실게임 씬이 `ItemFieldInteractionService`를 간접 의존 중이면 스폰 API 경로가 끊길 수 있음.

## 단계별 실행
1. 1차(완료): 무호출 데드코드/미사용 이벤트 체인 제거
2. 2차: 프로토타입 경로를 명시 분리
   - `Runner/`, `Test/`를 프로토타입 폴더로 이동(또는 네이밍으로 명확화)
   - README에 “실게임 금지 경로” 라벨 추가
3. 3차: 대체 경로 검증
   - Map Test Scene에서 자동 결합(`ItemCharacterAutoAttachBootstrap`)만으로
     획득/사용/손 시각화/버프가 동작하는지 확인
4. 4차: 프로토타입 제거
   - ItemScene 전용 테스트를 더 이상 유지하지 않기로 결정된 경우에만 삭제

## 검증 체크리스트
- 컴파일 에러 0
- 우클릭 획득 직후 손 시각화 즉시 반영
- 사용 후 상태 해제(보유 아이템/버프) 정상 반영
- 블랙홀 프리팹 스폰 및 이펙트 동작 확인
- 멀티플레이(호스트/클라)에서 상태 불일치 없음

## 운영 규칙
- 삭제는 항상 “대체 경로 검증 완료” 이후에만 수행한다.
- 한 번에 한 기능 단위로만 PR을 분리한다.
- Item 기능 추가는 `Runtime/Modules/ItemUseModule` 확장으로만 구현한다.
