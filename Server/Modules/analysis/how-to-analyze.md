# 코드 분석 방법론 — 효율적인 순서

> **원칙**: 전체 → 구조 → 흐름 → 세부. 코드를 처음부터 읽지 않는다.

---

## 행동 순서 (체크리스트)

### 1단계 — 파일 목록만 본다 (1분)

```
Server/Modules/Mmf/
  MesMmfDefine.cs          ← "Define" = 상수 모음
  MesInterProcessLayout.cs ← "Layout" = 메모리 구조 정의
  MesHeartbeatManager.cs   ← "Manager" = 생존 신호 관리
  MesMmfClear.cs           ← "Clear" = 초기화 유틸
  MesInterface.cs          ← "Interface" = 실제 읽기/쓰기
  MmfBridge.cs             ← 미구현 (빈 클래스)
```

**목표**: 파일명만 보고 "이 폴더가 뭘 하는 곳인지" 한 줄로 말할 수 있어야 한다.  
→ `Mmf = 프로세스 간 공유 메모리 통신 모듈`

---

### 2단계 — 상수/정의 파일부터 읽는다

`Define`, `Layout`, `Types`, `Const`, `Enum` 이름이 붙은 파일 먼저.  
이유: 이후 나오는 숫자(`0x08`, `128`)와 이름(`"Vision MES IF"`)의 의미를 미리 알아야 나머지가 읽힌다.

**`MesMmfDefine.cs` 에서 뽑아야 할 것**:
- 채널 이름 문자열 6개 (Heartbeat 4 + Interface 2쌍)
- 블록 크기: Heartbeat=16바이트, Interface=128바이트
- 기본 인터벌: 1000ms

**`MesInterProcessLayout.cs` 에서 뽑아야 할 것**:
- 메모리 맵 (오프셋표 — 아래 참고)

---

### 3단계 — 메모리 레이아웃을 표로 그린다

숫자를 외우지 않는다. 표로 그려두면 이후 코드가 한눈에 읽힌다.

#### Vision → MES IF (128바이트)

| 오프셋 | 크기 | 필드 |
|--------|------|------|
| 0x00 | uint16 | SchemaVersion (=4) |
| 0x02 | uint16 | CycleId |
| 0x04 | uint16 | PublishFlags (bit0=NewResult, bit1=AbsoluteWork) |
| 0x06 | uint16 | VisionState (0=Idle, 1=Run, 2=Alarm) |
| 0x08 | uint16 | ChannelNgBits |
| 0x0A | uint16 | SideNgMask |
| 0x0C | uint16 | ClassBitsInner1 |
| 0x0E | uint16 | ClassBitsInner2 |
| 0x10 | uint16 | ClassBitsOuter |
| 0x14~0x27 | uint16×12 | SideClassBits[0..11] |
| 0x2C | uint16 | DeltaTotal |
| 0x2E | uint16 | DeltaOk |
| 0x30 | uint16 | DeltaNg1 |
| 0x32 | uint16 | DeltaRc |
| 0x34~0x3A | uint16×4 | DeltaNg2~5 |
| 0x3C | uint16 | ApertureNgBits |

#### MES → Control IF (128바이트)

| 오프셋 | 크기 | 필드 |
|--------|------|------|
| 0x00 | uint16 | SchemaVersion |
| 0x02 | uint16 | LineState (bit: Ready/OpNormal/OpManual/Start/Stop) |
| 0x04 | uint16 | PublishFlags (bit0=Counters) |
| 0x06~0x18 | uint32×5 | Total/Ok/Rc/Ng1/Ng2 (Low+High 각 2바이트) |

#### MES → Control IF (Work 스냅샷)

| 오프셋 | 필드 |
|--------|------|
| 0x02 | MesCmd (0=Idle, 1=DataRequest) |
| 0x04 | MesStatus |
| 0x06 | SnapshotSeq / VisionAckCycleId |
| 0x08~0x27 | Total/Ok/Rc/Ng1~5 (uint32×9) |

#### Heartbeat 블록 (16바이트)

| 오프셋 | 크기 | 필드 |
|--------|------|------|
| 0x00 | int64 | UtcNow.Ticks |
| 0x08 | int64 | m_counter (증가 카운터) |

---

### 4단계 — 클래스 공개 API만 읽는다 (구현 건너뜀)

구현 코드(`{` `}` 안)는 아직 읽지 않는다. public 메서드 시그니처만.

**`MesHeartbeatManager`**
```
static ForVision()               → Vision 역할로 생성
static ForControl()
static ForMesTowardVision()
static ForMesTowardControl()
Start(intervalMs)                → 타이머 시작
Stop()
IsPeerAlive(timeoutMs=3000)      → 상대 살아있나?
```

**`MesInterface`**
```
static ForVision/Control/MesTowardVision/MesTowardControl()
IsPeerAlive()
// Vision 쓰기
PublishVisionResult(MesMapS0Publish)
// Control 쓰기
PublishControlLineState(lineState)
PublishControlLineAndCounters(lineState, total, ok, rc, ng1, ng2)
// MES 쓰기
SetMesCmd(cmd)
SetMesStatus(status)
SetVisionAckCycleId(cycleId)
PublishWorkSnapshot(seq, total, ok, rc, ng1~5)
// 읽기
TryReadPeerUInt16(offset)
TryReadPeerDWord(lowOffset)
TryPeekPeerUInt16(offset)        // Alive 체크 없이 읽기
```

---

### 5단계 — 데이터 흐름을 그린다

```
[Vision 프로세스]                  [MES 프로세스]               [Control 프로세스]
HeartbeatManager.Start()
  └─ 1초마다 Ticks+counter 기록 → "Vision MES" MMF
                                   IsPeerAlive() 로 감시
                                   ← "MES Vision" MMF 로 MES도 기록

MesInterface(Vision 역할)
  └─ PublishVisionResult()
       → "Vision MES IF" MMF
            CycleId, ChannelNgBits,
            ClassBits, DeltaXxx 기록

                                   MesInterface(MesTowardControl)
                                     └─ PublishWorkSnapshot()
                                          → "MES Control IF" MMF
                                               Total/Ok/Rc/Ng1~5

                                                                   MesInterface(Control 역할)
                                                                     └─ PublishControlLineAndCounters()
                                                                          → "Control MES IF" MMF
```

---

### 6단계 — 의문이 생긴 부분만 구현을 읽는다

예: "Peer Alive 체크 없이 읽는 `TryPeek`가 왜 있지?"  
→ `MesMmfClear` 초기화 시 상대가 아직 없어도 자기 쪽 IF 버전을 먼저 써야 하기 때문.

예: "VisionAckCycleId와 SnapshotSeq가 같은 오프셋 0x06인데 충돌 안 하나?"  
→ 물리적으로 다른 MMF (`MES Vision IF` vs `MES Control IF`). 이름만 같고 별개 블록.

---

## 이 방법론의 핵심 규칙

| ❌ 하지 말 것 | ✅ 할 것 |
|---|---|
| 파일을 처음부터 끝까지 읽기 | 파일명 → 상수 → API → 흐름 순서 |
| 숫자 외우기 | 표로 그려두기 |
| 구현부터 읽기 | public 시그니처만 먼저 |
| 모든 파일 동시에 읽기 | Define → Manager → Interface 순서 |
| 이해 안 되면 멈추기 | 일단 건너뛰고 흐름 먼저 |

---

## 다음 분석 대상 추천 순서

```
Mmf (완료)
  ↓ 사용처 추적
Assign  → Mmf를 어떻게 쓰는가
  ↓
Register/Store  → Modbus 레지스터 저장소
  ↓
Handshake  → 명령 처리
  ↓
Modbus/ModbusTcpSlave  → 최종 통신 계층
```
