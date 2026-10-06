# KepServerEX6 Device 설정값 정리

KepServerEX6에서 Modbus 슬레이브 장치(Device)를 추가할 때 나타나는 설정 항목 전체 정리.

---

## Identification (식별 정보)

| 항목 | 값 | 설명 |
|---|---|---|
| Name | ModSlave | 장치 이름 |
| Description | *(비어 있음)* | 장치 설명 (선택 입력) |
| Driver | Modbus TCP/IP Ethernet | 사용하는 드라이버 종류 |
| Model | Modbus | 장치 모델 |
| Channel Assignment | Modbus | 이 장치가 속한 채널 이름 |
| ID | `<127.0.0.1>.1` | 슬레이브 장치 주소 (IP + Unit ID) |

> **ID 형식**: `<IP주소>.Unit ID`  
> 예: `<127.0.0.1>.1` → IP `127.0.0.1`, Unit ID `1`

---

## Operating Mode (동작 모드)

| 항목 | 값 | 설명 |
|---|---|---|
| Data Collection | Enable | 데이터 수집 활성화 여부 |
| Simulated | No | 시뮬레이션 모드 여부 (No = 실제 통신) |

---

## Tag Counts (태그 수)

| 항목 | 값 | 설명 |
|---|---|---|
| Static Tags | 0 | 정적으로 정의된 태그 수 |

---

## Scan Mode (스캔 모드)

| 항목 | 값 | 설명 |
|---|---|---|
| Scan Mode | Respect Client-Specified Scan Rate | 클라이언트가 요청한 스캔 주기를 따름 |
| Initial Updates from Cache | Disable | 초기 데이터를 캐시에서 가져올지 여부 |

> **Scan Mode 옵션**:
> - `Respect Client-Specified Scan Rate`: OPC 클라이언트가 설정한 스캔 주기 사용
> - `Request Data No Faster Than Scan Rate`: 스캔 주기보다 빠르게 요청하지 않음
> - `Request All Data at Scan Rate`: 스캔 주기마다 항상 모든 데이터 요청

---

## Communication Timeouts (통신 타임아웃)

| 항목 | 값 | 설명 |
|---|---|---|
| Connect Timeout (s) | 3 | TCP 연결 시도 최대 대기 시간 (초) |
| Request Timeout (ms) | 1000 | 요청 후 응답 대기 최대 시간 (밀리초) |
| Attempts Before Timeout | 3 | 타임아웃 전 재시도 횟수 |

---

## Timing (타이밍)

| 항목 | 값 | 설명 |
|---|---|---|
| Inter-Request Delay (ms) | 0 | 요청 사이의 지연 시간 (밀리초). 0 = 지연 없음 |

---

## Auto-Demotion (자동 강등)

| 항목 | 값 | 설명 |
|---|---|---|
| Demote on Failure | Disable | 통신 실패 시 장치를 비활성(Demote) 상태로 전환할지 여부 |

> 활성화 시 실패 횟수 초과 시 해당 장치 폴링을 일시 중단해 네트워크 부하를 줄임

---

## Tag Generation (태그 자동 생성)

| 항목 | 값 | 설명 |
|---|---|---|
| On Device Startup | Do Not Generate on Startup | 장치 시작 시 태그 자동 생성 안 함 |
| On Duplicate Tag | Delete on Create | 중복 태그 발생 시 기존 태그 삭제 후 새로 생성 |
| Parent Group | *(비어 있음)* | 자동 생성 태그가 들어갈 상위 그룹 |
| Allow Automatically Generated Subgroups | Enable | 자동으로 서브그룹 생성 허용 |

---

## Variable Import Settings (변수 가져오기 설정)

| 항목 | 값 | 설명 |
|---|---|---|
| Variable Import File | `*.txt` | 태그 가져오기에 사용할 파일 형식 |
| Include Descriptions | Enable | 가져오기 시 설명 포함 여부 |

---

## Error Handling (오류 처리)

| 항목 | 값 | 설명 |
|---|---|---|
| Deactivate Tags on Illegal Address | Enable | 잘못된 주소 접근 시 해당 태그 비활성화 |

---

## Ethernet (이더넷)

| 항목 | 값 | 설명 |
|---|---|---|
| Port | 502 | Modbus TCP 기본 포트 번호 |
| IP Protocol | TCP/IP | 사용 프로토콜 (Transmission Control Protocol / Internet Protocol) |

> Modbus TCP의 표준 포트는 **502**번

---

## Data Access (데이터 접근)

| 항목 | 값 | 설명 |
|---|---|---|
| Zero-Based Addressing | Enable | 주소를 0번부터 시작 (Enable = 0부터, Disable = 1부터) |
| Zero-Based Bit Addressing | Enable | 비트 주소도 0번부터 시작 |
| Holding Register Bit Writes | Enable | 홀딩 레지스터의 개별 비트 쓰기 허용 |
| Modbus Function 06 | Enable | FC06 (Write Single Register) 사용 허용 |
| Modbus Function 05 | Enable | FC05 (Write Single Coil) 사용 허용 |

> **FC (Function Code)**: Modbus 요청 종류를 구분하는 코드
> - FC05: 단일 코일 쓰기
> - FC06: 단일 레지스터 쓰기

---

## Data Encoding (데이터 인코딩)

| 항목 | 값 | 설명 |
|---|---|---|
| Modbus Byte Order | Enable | Modbus 표준 바이트 순서 사용 (Big-Endian) |
| First Word Low | Enable | 첫 번째 워드를 하위 워드로 처리 |
| First DWord Low | Enable | 첫 번째 더블워드를 하위 워드로 처리 |
| Modicon Bit Order | Disable | Modicon 방식의 비트 순서 사용 안 함 |
| Treat Longs as Decimals | Disable | Long 타입을 10진수로 처리 안 함 |

> **바이트 순서(Byte Order)**: 멀티바이트 데이터를 저장하는 순서
> - Big-Endian: 상위 바이트 먼저 (Modbus 표준)
> - Little-Endian: 하위 바이트 먼저

---

## Coils (코일, 비트 단위 데이터) — in Multiples of 8

| 항목 | 값 | 설명 |
|---|---|---|
| Output Coils | 32 | 출력 코일 수 (쓰기 가능, FC01/FC05/FC15 대상) |
| Input Coils | 32 | 입력 코일 수 (읽기 전용, FC02 대상) |

> 코일은 8의 배수로 설정됨. 32 = 4바이트 분량의 비트 공간

---

## Registers (레지스터, 16비트 단위 데이터)

| 항목 | 값 | 설명 |
|---|---|---|
| Internal Registers | 32 | 내부(입력) 레지스터 수 (읽기 전용, FC04 대상) |
| Holding Registers | 32 | 홀딩 레지스터 수 (읽기/쓰기, FC03/FC06/FC16 대상) |

> **레지스터 종류**:
> - Internal Register (입력 레지스터): 마스터가 읽기만 가능 (FC04)
> - Holding Register (홀딩 레지스터): 마스터가 읽기·쓰기 모두 가능 (FC03, FC06, FC16)

---

## Blocks (블록 처리)

| 항목 | 값 | 설명 |
|---|---|---|
| Block Read Strings | Disable | 문자열 데이터를 블록 단위로 읽기 안 함 |

---

## OPC Quality (OPC 품질)

| 항목 | 값 | 설명 |
|---|---|---|
| OPC Quality Bad until Write | Disable | 첫 쓰기 전까지 OPC 품질을 Bad로 설정하지 않음 |
| Communications Timeout (s) | 0 | 통신 타임아웃 시간. 0 = 비활성 |

> **OPC (OLE for Process Control)**: 산업 자동화에서 소프트웨어 간 데이터 교환 표준 규격  
> OPC Quality: 태그 데이터의 신뢰도를 나타내는 값 (Good / Bad / Uncertain)
