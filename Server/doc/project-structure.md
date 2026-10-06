# ModSlave 프로젝트 구조

## 개요

KepServerEX6(Modbus 마스터) ↔ ModSlave 앱(Modbus 슬레이브) 통신 테스트 프로젝트.

```
KepServerEX6          ←Modbus TCP→         Server.exe
(마스터, OPC 서버)                           (슬레이브, 이 프로젝트)
                                            ┌──────────────────┐
                                            │  FormIndex        │
                                            │  ├ VisionMain     │
                                            │  ├ ControlMain    │
                                            │  └ MesHostMain    │
                                            └──────────────────┘
```

---

## 실행 흐름

```
Program.cs
  └─ Application.Run(FormIndex)
       ├─ [비전] 버튼 → VisionMain (Vision MMF 시뮬레이터)
       ├─ [제어] 버튼 → ControlMain (Control MMF 시뮬레이터)
       └─ [MES]  버튼 → MesHostMain (MES 호스트 + Modbus 슬레이브)
```

---

## 폴더 구조

```
Server/
├─ Program.cs                         진입점
├─ FormIndex.cs / .Designer.cs        메인 인덱스 폼 (비전/제어/MES 버튼)
│
├─ App/
│   ├─ Vision/
│   │   ├─ Vision.cs                  (미사용 — 향후 확장용)
│   │   └─ Forms/
│   │       ├─ VisionMain.cs          Vision MMF 시뮬레이터 폼 ★
│   │       └─ VisionMain.Designer.cs  버튼(자동/수동) + dgvMmf
│   │
│   ├─ Control/
│   │   ├─ Control.cs                 (미사용 — 향후 확장용)
│   │   └─ Forms/
│   │       ├─ ControlMain.cs         Control MMF 시뮬레이터 폼 ★
│   │       └─ ControlMain.Designer.cs 버튼(자동/수동) + dgvMmf
│   │
│   └─ MesHost/
│       ├─ MesApp.cs                  MES 앱 비즈니스 로직 ★
│       └─ Forms/
│           ├─ MesHostMain.cs         MES 호스트 폼 ★
│           └─ MesHostMain.Designer.cs 시작/정지 + dgvStore + 로그
│
└─ Modules/
    ├─ Assign/
    │   ├─ MesAssignS0.cs             Vision 채널 NG → Modbus 주소 매핑
    │   ├─ MesAssignTypes.cs
    │   ├─ MesCycleModels.cs
    │   └─ MesClassNameMap.cs
    │
    ├─ Config/
    │   ├─ ModbusConfig.cs            Modbus TCP 설정 (IP, Port, UnitId, WordOrder)
    │   ├─ MesHostParams.cs           MES 호스트 파라미터 (JSON 파일에서 로드)
    │   ├─ MesHostParameterLoader.cs
    │   ├─ MesBridgeOption.cs
    │   └─ MesWriterMode.cs / MesHeartbeatPeers.cs
    │
    ├─ Handshake/
    │   ├─ DataCmdHandler.cs          KepServerEX의 Data_CMD 코일 감시 → 이벤트 발생
    │   └─ WorkClearHandler.cs        WorkClear 코일 감시 → Store 초기화
    │
    ├─ Map/
    │   ├─ Addresses.cs               Modbus 0-based offset 상수 정의
    │   ├─ MesMapLayout.cs            태그 메타데이터 목록 (이름/주소/설명)
    │   ├─ MesNgPartLayout.cs
    │   └─ MesNgPartLayout.cs
    │
    ├─ Mmf/
    │   ├─ MesMmfDefine.cs            MMF 이름 상수 + 크기 상수
    │   ├─ MesMmfClear.cs             MMF 영역 0 초기화
    │   ├─ MesInterProcessLayout.cs   MMF 내 바이트 오프셋 상수 (Vision IF / Control IF)
    │   ├─ MesHeartbeatManager.cs     프로세스 간 심박(Heartbeat) MMF 관리
    │   ├─ MesInterface.cs            데이터 채널 MMF 읽기/쓰기 (Vision/Control/MES 역할별)
    │   └─ MmfBridge.cs               MMF → RegisterStore 변환 브리지 (50ms 폴링)
    │
    ├─ Modbus/
    │   └─ ModbusTcpSlave.cs          Modbus TCP 슬레이브 서버 (FC01/03/05/06/15/16)
    │
    ├─ Persist/
    │   └─ MesWorkPersist.cs          Work 카운터 파일 영속화 (mes-work-accum.bin)
    │
    └─ Register/
        └─ Store.cs                   Modbus Coil / Holding 레지스터 저장소
```

---

## 프로세스 간 MMF 통신 구조

같은 PC 내 프로세스들이 메모리 맵 파일(MMF)로 통신한다.

```
┌──────────────────┐        MMF         ┌──────────────────────────┐
│  VisionMain      │ ──Vision MES IF──▶ │                          │
│  (Vision 시뮬)   │ ◀──MES Vision IF── │        MesHostMain       │
│  heartbeat:      │                    │        (MmfBridge)       │
│  "Vision MES"    │                    │  heartbeat: "MES Vision" │
└──────────────────┘                    │                          │
                                        │                          │
┌──────────────────┐        MMF         │                          │
│  ControlMain     │ ──Control MES IF─▶ │                          │
│  (Control 시뮬)  │ ◀─MES Control IF── │  heartbeat: "MES Control"│
│  heartbeat:      │                    └──────────────────────────┘
│  "Control MES"   │                              │
└──────────────────┘                              │ Modbus TCP
                                                  ▼
                                          KepServerEX6 (마스터)
```

### MMF 채널 이름표 (MesMmfDefine.cs)

| 채널명 | 방향 | 용도 |
|---|---|---|
| `"Vision MES"` | Vision → MES | Vision 심박 |
| `"MES Vision"` | MES → Vision | MES 심박 |
| `"Vision MES IF"` | Vision → MES | Vision 검사 데이터 (128 byte) |
| `"MES Vision IF"` | MES → Vision | MES 명령/상태 (128 byte) |
| `"Control MES"` | Control → MES | Control 심박 |
| `"MES Control"` | MES → Control | MES 심박 |
| `"Control MES IF"` | Control → MES | Control 라인 상태 + 카운터 (128 byte) |
| `"MES Control IF"` | MES → Control | MES Work 스냅샷 (128 byte) |

### 규칙
- 각 프로세스는 **자기 MMF에만 Write**, 상대 MMF는 Read만.
- `CreateOrOpen` = 내 MMF(없으면 생성), `OpenExisting` = 상대 MMF(없으면 예외).
- Heartbeat가 3초 이상 멈추면 상대 프로세스 "Dead" 판단.

---

## 핵심 클래스 설명

### MesHeartbeatManager
프로세스 간 생존 신호 관리.
- `Start()` → 1초마다 자기 MMF에 카운터 + 타임스탬프 기록
- `IsPeerAlive()` → 상대 카운터가 3초 내 변경됐는지 확인
- 백오프: 상대 MMF `OpenExisting` 실패 시 1초 재시도 억제

### MesInterface
데이터 채널 MMF 읽기/쓰기.

| 역할 | 팩토리 메서드 | 자기 IF | 상대(Peer) IF |
|---|---|---|---|
| Vision 앱 | `ForVision(hb)` | `Vision MES IF` | `MES Vision IF` |
| Control 앱 | `ForControl(hb)` | `Control MES IF` | `MES Control IF` |
| MES→Vision | `ForMesTowardVision(hb)` | `MES Vision IF` | `Vision MES IF` |
| MES→Control | `ForMesTowardControl(hb)` | `MES Control IF` | `Control MES IF` |

- `PublishVisionResult(MesMapS0Publish)` — Vision이 검사 결과 쓰기
- `PublishControlLineAndCounters(...)` — Control이 라인 상태 + 카운터 쓰기
- `TryReadPeerUInt16(offset, out value)` — 상대 MMF 읽기 (Alive 체크 포함)

### MmfBridge
MMF를 50ms 주기로 폴링하여 `Store`(레지스터)에 반영.
- `TickVision()` → `PublishFlag_NewResult` 확인 → Delta 카운터를 `Store.IncrementDWord`
- `TickControl()` → LineState → Coil 반영, 카운터 → `Store.SetDWord`
- `NotifyWorkChanged()` — 외부(WorkClear 등)가 Store를 변경했을 때 파일 저장 + Control에 스냅샷 전송

### Store
Modbus 레지스터 저장소 (thread-safe).
- `coils[]` — Coil(bit) 1000개
- `holdings[]` — Holding Register(word) 62개  
- `accumHoldings[]` — 누적 카운터 미러 (Data_CMD 윈도우 리셋과 분리)

### MesApp (MesApp.cs)
MES 측 전체 오케스트레이션.
- `Start()` → MES 심박 시작 + `MmfBridge` 시작 + `ModbusTcpSlave` 시작
- `Stop()` / `Dispose()` → 역순 정리
- `OnDataCmdRaised()` → KepServerEX가 Data_CMD=1 쓰면 Vision/Control에 `MesCmd_DataRequest` 전송
- `OnWorkClearRaised()` → KepServerEX가 WorkClear=1 쓰면 Store 초기화

---

## Modbus 주소 맵 (Addresses.cs)

### Coil (0-based offset)

| Offset | 1-based | 태그명 | 설명 |
|---|---|---|---|
| 0 | 000001 | Ready | 운전 준비 |
| 1 | 000002 | OP_Mode_Normal | 자동 운전 |
| 2 | 000003 | OP_Mode_Manual | 수동 운전 |
| 3 | 000004 | Start | 가동 중 |
| 4 | 000005 | Stop | 정지 |
| 5 | 000006 | Data_REQ | 데이터 요구 응답 |
| 6 | 000007 | Data_CMD | 데이터 요구 (KepServer → 슬레이브, Write) |
| 10 | 000011 | WorkClear_CMD | Work 수량 초기화 명령 (Write) |
| 11 | 000012 | WorkClear_ACK | Work 초기화 완료 응답 |
| 999 | 001000 | HEARTBEAT | 통신 감시 |

### Holding Register (0-based offset → 1-based 주소 = offset + 400001)

| Offset | 주소 | 태그명 | 타입 | 설명 |
|---|---|---|---|---|
| 0 | 400001 | Total_TestCounter | DWord | 총 검사 수량 |
| 2 | 400003 | OK_WorkCounter | DWord | OK 수량 |
| 4 | 400005 | RC_WorkCounter | DWord | 재검(RC) 수량 |
| 6 | 400007 | NG_Work1_Counter | DWord | NG Group A |
| 8 | 400009 | NG_Work2_Counter | DWord | NG Group B |
| 10 | 400011 | NG_Work3_Counter | DWord | NG Group C |
| 12 | 400013 | NG_Work4_Counter | DWord | NG Group D |
| 14 | 400015 | NG_Work5_Counter | DWord | NG Group Side |
| 16 | 400017 | ngPart1 | DWord | 파열(Rupture) NG 수 |
| 18 | 400019 | ngPart2 | DWord | 총고(Height) NG 수 |
| 20 | 400021 | ngPart3 | DWord | 개구부 진원도/직경 NG |
| 22 | 400023 | ngPart4 | DWord | 개구부 결함크기 NG |
| 24~46 | 400025~400047 | ngPart5~16 | DWord | 외관(Inner1/2/Outer/Side) NG |
| 48 | 400049 | OK_Work_Percent | Word | OK 비율 (×0.01%) |
| 50 | 400051 | RC_Work_Percent | Word | RC 비율 |
| 52~60 | 400053~400061 | NG_Work1~5_Percent | Word | NG1~5 비율 |

> DWord는 Little-Endian Word 순서로 두 레지스터에 저장 (WordOrder 설정에 따라 변환).

---

## 테스트 폼 동작

### VisionMain (비전 MMF 시뮬레이터)

| 동작 | 설명 |
|---|---|
| 폼 오픈 | Vision MMF(`Vision MES IF`) 생성, Vision 심박 시작 |
| **자동** 버튼 | 500ms 주기로 랜덤 CycleId/NG bits 생성 → MMF에 Write + 그리드 갱신 |
| **수동** 버튼 | 자동 정지, 그리드 편집 활성화 |
| 셀 편집 후 Enter | 그리드 값을 읽어 즉시 MMF에 Write |
| 폼 닫기 | 타이머 정지, 심박 정지, MMF 해제 |

**표시 필드:** CycleId, VisionState, ChannelNgBits, SideNgMask, DeltaTotal, DeltaOk, DeltaNg1~5, DeltaRc, ApertureNgBits

### ControlMain (제어 MMF 시뮬레이터)

| 동작 | 설명 |
|---|---|
| 폼 오픈 | Control MMF(`Control MES IF`) 생성, Control 심박 시작 |
| **자동** 버튼 | 1초마다 카운터 증가 (70% OK / 30% NG1 or RC) → MMF Write |
| **수동** 버튼 | 자동 정지, 그리드 편집 활성화 |
| 셀 편집 후 Enter | 그리드 값을 즉시 MMF에 Write |

**표시 필드:** LineState (hex), Total, OK, RC, NG1, NG2

> LineState 비트: Ready=0x01, OpNormal=0x02, OpManual=0x04, Start=0x08, Stop=0x10

### MesHostMain (MES 호스트)

| 동작 | 설명 |
|---|---|
| 폼 오픈 | `MesApp` 인스턴스 생성 (파라미터 파일 로드) |
| **시작** 버튼 | MES 심박 시작 + MmfBridge 폴링(50ms) 시작 + ModbusTcpSlave Listen |
| **정지** 버튼 | Bridge + 심박 + 슬레이브 정지 |
| 비전/제어 상태 | 녹색 `●연결` / 회색 `○미연결` (500ms 갱신) |
| dgvStore | Holding 레지스터 전체 실시간 표시 (500ms 갱신) |
| 로그창 | MmfBridge, Modbus 이벤트 타임스탬프 포함 출력 |

---

## 파라미터 파일

위치: `setup/Parameter/Mes.Host.params.json`  
실행 시 탐색 순서: `bin/Debug/Parameter/` → 상위 폴더 순으로 `setup/Parameter/` 탐색 (최대 6단계).

주요 항목:
- `Modbus.ListenIp`, `Modbus.Port` — Modbus TCP 리슨 주소 (기본: `127.0.0.1:8010`)
- `Modbus.UnitId` — Modbus 장치 ID (기본: 1)
- `Modbus.WordOrder` — DWord 저장 순서 (`ABCD` / `CDAB`)
- `Bridge.PollMs` — MMF 폴링 주기 (기본: 50ms)

---

## 버그 수정 이력 (초기 구현 시)

| 파일 | 위치 | 내용 |
|---|---|---|
| `MmfBridge.cs` | 생성자 | `store = store` → `this.store = store` (self-assignment) |
| `MmfBridge.cs` | 생성자 | `ngParts = ngParts ?? ...` → `this.ngParts = ...` |
| `MmfBridge.cs` | 생성자 | `persist = persist` → `this.persist = persist` |
| `MesHeartbeatManager.cs` | 생성자 | `peerName = peerName` → `this.peerName = peerName` |
| `MesInterface.cs` | 생성자 | `heartbeat = heartbeat` → `this.heartbeat = heartbeat` |
| `MesApp.cs` | `Dispose()` | `NotImplementedException` → 정상 구현 |
