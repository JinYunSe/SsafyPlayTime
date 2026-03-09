# Item Refactor Plan

## 목표
- 협업 시 파일 위치만 보고도 수정 범위를 예측할 수 있게 한다.
- `ItemScene` 검증 코드와 실게임 코드를 분리한다.
- namespace와 런타임 API는 유지해서 연결 오류를 막는다.

## 이번 정리에서 한 일
1. `Integration/`을 `Character/`로 이동했다.
2. `Field/`를 `World/`로 이동했다.
3. `Prototype/`을 `Dev/`로 이동했다.
4. `ItemGameplayRunner` partial 파일을 `Core / Presentation / Skills / Debug` 기준으로 다시 묶었다.
5. 문서를 `Docs/`로 분리했다.

## 유지한 원칙
- class 이름 유지
- namespace 유지
- meta 유지
- public API 유지
- 씬/프리팹 연결 경로는 건드리지 않음

## 기대 효과
- 캐릭터 담당자는 `Character/`만 보면 된다.
- 월드 드랍/획득 담당자는 `World/`만 보면 된다.
- 아이템 사용 로직 담당자는 `Runtime/Modules/`와 `Runtime/Controller/`를 보면 된다.
- `ItemScene` 디버그/회귀 테스트는 `Dev/`에서만 찾으면 된다.

## 이후 권장 작업
1. `Character/` 안에서 `Combat / Presentation / Services`까지 2차 세분화
2. `World/` 안에서 `Spawning / Interaction / Visuals` 세분화
3. `Runtime/Controller/`의 partial 파일도 역할별 이름을 더 명확히 정리
4. 아이템별 설정값을 CSV와 ScriptableObject 중 어디에 둘지 팀 기준 확정
5. `Dev/` 코드를 최종 빌드 경로에서 완전히 분리할지 결정

## 검증 체크리스트
- 컴파일 에러 0
- 기존 필드 줍기/사용/드롭 흐름 유지
- 캐릭터 손 장착 시각 유지
- `ItemScene` 테스트 루프 유지
- 아이템 모듈 등록/호출 경로 유지
