# Audio Addition Guideline

## 목적
- 프로젝트 사운드를 `master`, `effect_sound`, `background_sound` 기준으로 일관되게 관리한다.
- 새 사운드를 추가할 때 어떤 채널로 분류하고 어떻게 연결할지 기준을 통일한다.

## 현재 볼륨 구조
- `master`
  - 전체 최종 볼륨
  - `AudioListener.volume`에 직접 반영된다.
- `effect_sound`
  - BGM을 제외한 모든 사운드
  - 아이템 사용음, UI 효과음, 피격음, 환경 효과음, 캐릭터 보이스 포함
- `background_sound`
  - 배경음 전용
  - 로비 BGM, 게임 씬 BGM 등 반복 재생되는 음악

## 기본 원칙
- BGM이 아니면 전부 `effect_sound`로 분류한다.
- 새 `AudioSource`를 명시적으로 관리할 수 있으면 `GameAudioSource`를 붙인다.
- 자동 추정에 의존하지 말고, 중요한 오디오는 가급적 직접 카테고리를 지정한다.

## 적용 스크립트
- `Assets/_Project/Scripts/Audio/GameAudioCategory.cs`
  - 오디오 카테고리 enum
- `Assets/_Project/Scripts/Audio/GameAudioSource.cs`
  - 개별 `AudioSource`의 카테고리 지정 컴포넌트
- `Assets/_Project/Scripts/Audio/GameAudioSettingsService.cs`
  - 볼륨 저장, 로드, 적용 담당
- `Assets/_Project/Scripts/Lobby/LobbyAudioSettingsModal.cs`
  - LauncherScene 설정 모달 UI

## AudioSource 추가 방법

### 1. 씬이나 프리팹에 이미 AudioSource가 있는 경우
1. 같은 GameObject에 `GameAudioSource`를 추가한다.
2. `category`를 지정한다.
   - BGM이면 `BackgroundSound`
   - 나머지는 `EffectSound`

### 2. 코드에서 AudioSource를 동적으로 만드는 경우
```csharp
var source = gameObject.AddComponent<AudioSource>();
source.clip = clip;
source.volume = 1f;

var categorized = gameObject.AddComponent<GameAudioSource>();
categorized.SetCategory(GameAudioCategory.EffectSound); // BGM이면 BackgroundSound
categorized.RefreshBaseVolumeFromCurrentSource();
```

## 권장 분류 기준

### `background_sound`
- 로비 배경음
- 게임 씬 배경음
- 반복 재생되는 음악성 트랙

### `effect_sound`
- 아이템 사용음
- 무기/스킬 발사음
- 피격음
- 폭발음
- 버튼 클릭음
- 팝업 오픈/클로즈음
- 캐릭터 보이스
- 환경 효과음

## 자동 추정 규칙
- `GameAudioSource`가 없는 `AudioSource`는 `GameAudioSettingsService`가 자동 등록한다.
- 아래 조건이면 `background_sound`로 추정한다.
  - `loop == true`
  - 이름이나 클립명에 `BGM`, `Music`, `Background` 포함
- 그 외는 `effect_sound`로 처리한다.

## 테이블 기반 사운드 추가 기준
- 아이템 사용음은 데이터 테이블로 연결한다.
- 관련 파일:
  - `Assets/_Project/Data/ItemTable.csv`
  - `Assets/_Project/Data/ItemPresentationTable.csv`
  - `Assets/_Project/Data/SoundAssetTable.csv`

### 아이템 사운드 추가 절차
1. `SoundAssetTable.csv`에 `sfxId`와 오디오 파일 경로를 추가한다.
2. `ItemPresentationTable.csv`의 `useSfxId` 또는 `hitSfxId`에 연결한다.
3. 필요하면 `ItemTable.csv`의 `sfxId`도 맞춘다.
4. 해당 사운드는 기본적으로 `effect_sound`로 취급한다.

## BGM 추가 절차
1. BGM 재생용 GameObject 또는 프리팹에 `AudioSource`를 둔다.
2. 같은 오브젝트에 `GameAudioSource`를 추가한다.
3. `category = BackgroundSound`로 설정한다.
4. `loop = true`로 설정한다.

## 주의사항
- BGM이 아닌데 `BackgroundSound`로 넣지 않는다.
- `GameAudioSource` 없이 이름 규칙만 믿고 자동 분류하는 방식은 임시 수단으로만 쓴다.
- 동적 생성 오디오는 생성 직후 `RefreshBaseVolumeFromCurrentSource()`까지 호출해야 한다.
- 볼륨 슬라이더는 `master`, `effect_sound`, `background_sound` 세 가지만 존재한다.

## 향후 확장
- 보이스를 따로 조절해야 하면 `voice_sound` 채널을 추가한다.
- 그 전까지는 보이스도 `effect_sound`로 유지한다.
