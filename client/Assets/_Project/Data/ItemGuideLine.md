# 아이템 시스템 통합 가이드 (Fusion 2 기준)

## 1) 확정 사항 요약
- 아이템은 총 8종으로 운영한다.
- 아이템 분류는 `소모형`과 `장비형`으로 나눈다.
- **인벤토리 슬롯은 사용하지 않는다.**
- 모든 아이템은 월드에서 집은 뒤 **손에 장착한 상태에서만 사용**한다.
- 플레이어는 **동시에 손에 아이템 1개만** 들 수 있다.
- 손에 든 아이템은 종류와 무관하게 기절 시 **즉시 드랍**된다.
- 소모형 아이템은 손에 든 상태에서 발동하면 즉시 소모된다.
- 화염 방사기는 장비형이며, 손에 들고 **최대 5초**만 사용 가능하다.
- 통신/판정은 Photon Fusion 2 기반 `Host Authority`를 기본으로 한다.

---

## 2) 아이템 8종 정리

| 코드 | 아이템명 | 분류 | 사용 방식 | 핵심 효과 | 확정 규칙 |
| --- | --- | --- | --- | --- | --- |
| A | 블랙홀 폭탄 | 소모형 | 손에 들고 발동(1회성, 투척) | 범위 흡입 CC | 발동 시 소모, 기절 시 드랍 |
| B | 커지는 아이템 | 소모형 | 손에 들고 발동(1회성, 섭취) | 크기 증가, 이속 감소, 공격력 증가, 넉백 저항 증가 | 발동 시 소모, 기절 시 드랍 |
| C | 작아지는 아이템 | 소모형 | 손에 들고 발동(1회성, 섭취) | 크기 감소, 이속 증가, 중력 감소, 점프 증가 | 발동 시 소모, 기절 시 드랍 |
| D | 아이스 아메리카노 | 소모형 | 손에 들고 발동(1회성, 섭취) | 슈퍼아머(또는 회복+약슈아) | 발동 시 소모, 기절 시 드랍 |
| E | 화염 방사기 | 장비형 | 손에 들고 사용 | 지속 피해 + 도트 | **최대 5초 사용**, 기절 시 드랍 |
| F | 투명화 아이템 | 소모형 | 손에 들고 발동(1회성, 섭취) | 은신(반투명 권장) | 발동 시 소모, 기절 시 드랍 |
| G | 사무용 도구 | 장비형 | 손에 들고 근접/투척 | 휘두르기, 타격, 투척 | 기절 시 드랍, 재줍기 가능 |
| H | 위성 폭격 | 소모형 | 손에 들고 발동(1회성, 지정형) | 지점 광역 피해 | 발동 시 소모, 기절 시 드랍 |

---

## 3) 데이터 테이블 구조 (권장)

## 3.1 `ItemMaster.csv` (아이템 메타 테이블)
아이템의 기본 성격과 공통 동작을 정의한다.

| 필드 | 타입 | 설명 |
| --- | --- | --- |
| itemId | string | 고유 ID (예: `ITEM_BLACKHOLE_BOMB`) |
| itemName | string | 표시 이름 |
| itemType | enum | `Consumable`, `Equipment` |
| useType | enum | `Instant`, `Hold` |
| requiresHandEquip | bool | 손 장착이 필요한지 여부 (전 아이템 `true`) |
| holdSlotCount | int | 손 슬롯 점유 수 (현재 고정 1) |
| consumeOnUse | bool | 발동 시 소모 여부 |
| dropOnStun | bool | 기절 시 드랍 여부 (전 아이템 `true`) |
| cooldownSec | float | 재사용/획득 쿨타임 |
| prefabPath | string | 프리팹 경로 |
| iconPath | string | UI 아이콘 경로 |
| sfxId | string | 사운드 키 |
| vfxId | string | 이펙트 키 |
| enabled | bool | 활성화 여부 |

## 3.2 `ItemCombatProfile.csv` (전투/효과 수치 테이블)
아이템별 실제 전투 수치를 정의한다.

| 필드 | 타입 | 설명 |
| --- | --- | --- |
| itemId | string | `ItemMaster.itemId` FK |
| durationSec | float | 효과 지속시간 |
| range | float | 유효 사거리/반경 |
| baseDamage | float | 기본 피해 |
| force | float | 밀침/흡입 힘 |
| tickIntervalSec | float | 도트/주기 효과 간격 |
| stackRule | enum | `None`, `Refresh`, `Stack` |
| maxStacks | int | 최대 중첩 |
| useDelaySec | float | 발동 전 딜레이 |
| warningTimeSec | float | 사전 경고 시간(위성폭격 등) |

## 3.3 `ItemBuffProfile.csv` (버프형 전용 테이블)
버프형 아이템 전용 수치.

| 필드 | 타입 | 설명 |
| --- | --- | --- |
| itemId | string | FK |
| scaleMultiplier | float | 크기 배율 |
| moveSpeedMultiplier | float | 이동속도 배율 |
| baseDamageMultiplier | float | 공격력 배율 |
| knockbackResistMultiplier | float | 넉백 저항 배율 |
| gravityMultiplier | float | 중력 배율 |
| jumpMultiplier | float | 점프 배율 |
| superArmorLevel | int | 슈퍼아머 강도 |
| revealOnAttack | bool | 공격 시 은신 해제 여부 |

## 3.4 `ItemEquipmentProfile.csv` (장비형 전용 테이블)
손에 들고 사용하는 아이템의 사용 제약을 정의한다.

| 필드 | 타입 | 설명 |
| --- | --- | --- |
| itemId | string | FK |
| canMelee | bool | 근접 공격 가능 여부 |
| canThrow | bool | 투척 가능 여부 |
| durability | int | 내구도 |
| stunDropEnabled | bool | 기절 드랍 강제 |
| maxActiveUseSec | float | 활성 사용 가능 시간 (`화염 방사기=5`) |
| overheatCooldownSec | float | 과열 후 복구 시간 |

## 3.5 `ItemSpawnTable.csv` (스폰 테이블)
맵별 스폰 정책을 정의한다.

| 필드 | 타입 | 설명 |
| --- | --- | --- |
| spawnId | string | 스폰 엔트리 ID |
| mapId | string | 맵 ID |
| itemId | string | 스폰 아이템 |
| spawnGroup | string | 그룹 ID |
| weight | int | 랜덤 가중치 |
| respawnSec | float | 리스폰 시간 |
| maxAlive | int | 동시 존재 최대치 |
| posX,posY,posZ | float | 월드 좌표 |
| rotY | float | 초기 회전 |

## 3.6 `ItemRuntimeState` (런타임 네트워크 상태)
CSV가 아니라 Fusion Networked State로 유지한다.

| 상태 키 | 저장 위치 | 설명 |
| --- | --- | --- |
| heldItemId | Player NetworkState | 현재 손에 든 아이템 ID |
| heldItemNetId | Player NetworkState | 손에 든 월드 아이템 NetworkId |
| isStunned | Player NetworkState | 기절 상태 |
| activeBuffMask | Player NetworkState | 활성 버프 비트마스크 |
| buffEndTick | Player NetworkState | 버프 종료 Tick |
| equipmentEndTick | Player NetworkState | 장비 사용 종료 Tick (화방 5초) |
| worldItemOwner | WorldItem NetworkState | 현재 소유자(PlayerRef) |
| worldItemState | WorldItem NetworkState | `Idle`, `Held`, `Dropped`, `Consumed` |

---

## 4) Fusion 2 통신 기능 요구사항

## 4.1 권한/판정
- 기본은 Host 권한 판정으로 통일한다.
- 피해, 흡입, 도트 Tick, 스턴, 드랍은 Host가 최종 확정한다.
- 클라이언트는 입력 전송과 연출 재생 중심으로 처리한다.

## 4.2 핵심 네트워크 기능
- `아이템 픽업`: 플레이어가 아이템을 줍는 요청, Host가 `현재 손 비어 있음`을 검사 후 승인.
- `아이템 사용`: 소모형은 손 장착 상태에서 발동 후 즉시 `Consumed`, 장비형은 `Held` 유지.
- `기절 드랍`: `isStunned=true` 진입 시 손 아이템을 강제 `Dropped`.
- `화염 방사기 5초 제한`: 사용 시작 Tick 기록 후 5초 도달 시 자동 종료.
- `광역 스킬`: 블랙홀/위성폭격은 발동 위치/반경/시작Tick 동기화.
- `도트 처리`: Host Tick 기반 주기 피해 적용.

## 4.3 RPC/이벤트 권장 목록
- `RPC_RequestPickup(itemNetId)`
- `RPC_RequestUseHeldItem(targetPos)`
- `RPC_NotifyItemDropped(itemNetId, reason)`
- `RPC_NotifyEffectSpawn(effectType, pos, radius, endTick)`
- `RPC_NotifyDamage(targetPlayerRef, amount, cause)`

---

## 5) 아이템별 구현 체크리스트

## 5.1 소모형 공통
- 손에 들고 있는 상태에서만 발동 가능한가?
- 발동 즉시 월드/손 상태가 `Consumed`로 전환되는가?
- 중복 사용 방지(입력 연타) 처리가 되는가?
- 버프 종료 시 원상 복구가 확실한가?

## 5.2 장비형 공통
- 한 번에 손에 1개만 들 수 있는가?
- 기절 시 즉시 드랍되는가?
- 드랍 후 재줍기 쿨다운/거리 제한이 있는가?

## 5.3 화염 방사기 전용
- 장착 중 발사 시작/중지 입력이 분리되는가?
- 누적 사용시간이 5초를 넘기지 않는가?
- Tick 피해가 Host 기준으로 안정적으로 적용되는가?

---

## 6) 권장 초기 밸런스 기준 (테스트 시작값)
- 블랙홀 폭탄: 지연 0.7s / 지속 3.0s / 반경 4.5 / 흡입력 16
- 위성 폭격: 경고 1.5s / 반경 5.0 / 중심 고피해 + 외곽 저피해
- 커짐 버프: 8s / 크기 1.35x / 이속 0.85x / 공격력 1.2x
- 작아짐 버프: 8s / 크기 0.7x / 이속 1.2x / 중력 0.8x / 점프 1.2x
- 아메리카노: 3s 슈퍼아머(또는 회복 20 + 슈아 1.5s)
- 투명화: 8s / 공격 시 해제 / 이동 시 발자국 이펙트
- 사무용 도구: 내구도 10 / 근접+투척 가능 / 기절 드랍
- 화염 방사기: 도트 Tick 0.3s / 최대 사용 5.0s / 과열 쿨다운 4.0s

---

## 7) 최종 원칙
- 수치는 반드시 테이블로 분리하고 하드코딩하지 않는다.
- 연출보다 판정 일관성을 우선한다.
- 모든 핵심 판정(피해/스턴/드랍/종료)은 Host가 확정한다.
- **인벤토리 없이 손 슬롯 1개만 사용**한다.
- 기절 드랍 규칙은 모든 손 아이템에 공통 적용한다.
