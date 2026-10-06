# Modbus 주소 체계 정리

대상 파일: `app/Device/Modbus/Map/MesMapAddresses.cs`

## 1. 주소 1칸의 크기

| 영역 | 주소 1칸 | 표기(1-based) | 기능코드(읽기) |
|---|---|---|---|
| Coil | 1비트 (ON/OFF) | 0xxxxx (예: 000001) | 01 |
| Discrete Input | 1비트 | 1xxxxx | 02 |
| Input Register | 16비트 (2바이트) | 3xxxxx | 04 |
| Holding Register | 16비트 (2바이트) | 4xxxxx (예: 400001) | 03 |

- "1씩 증가 / 2씩 증가"는 **레지스터(16비트) 단위**로 본다.
  - 1칸 = 16비트 값 (`ushort`, `short`)
  - 2칸 = 32비트 값 (`int`, `uint`, `float`)
  - 4칸 = 64비트 값 (`long`, `double`)
- Coil에서 1씩 증가는 1비트씩 증가라는 뜻이다.

## 2. `ushort` 상수는 "주소 번호"를 담는다

```csharp
public const ushort CoilHeartbeat = 999;          // 999번 Coil
public const ushort HoldingOkWorkCounter = 2;     // 2번 Holding부터
```

- 변수 `ushort`(16비트, 0~65535)는 **주소 번호를 담는 타입**이다. 데이터 크기와 무관하다.
- Modbus 시작 주소 필드가 16비트 부호 없는 정수로 정해져 있어서 `ushort`를 쓴다.
- 999는 0~65535 범위 안이다. `byte`(최대 255)로는 부족하다.

| 구분 | 크기 |
|---|---|
| 주소 번호를 담는 `ushort` 변수 | 16비트 |
| Coil 주소 1칸이 가리키는 값 | 1비트 |
| Holding 주소 1칸이 가리키는 값 | 16비트 |
| OkWorkCounter 데이터 (주소 2, 3) | 32비트 (DWord) |

## 3. 32비트 값은 Holding 2칸에 걸친다

```
주소 2 (400003)  ┐
                 ├─ OkWorkCounter 값 1개 (32비트)
주소 3 (400004)  ┘
주소 4 (400005)  → 다음 변수 RcWorkCounter 시작
```

- 카운터와 ngPart는 DWord라서 주소가 2씩 증가한다. (`NgPartStride = 2`)
- 읽을 때는 **시작 주소 2, 개수 2**로 요청해야 한다. 개수를 1로 하면 값의 절반만 읽는다.
- Percent(400049~)는 Word(16비트)인데도 코드상 2칸 간격(stride 2)으로 배치되어 있다. 업체 표의 배치로 보인다.

## 4. 1-based 표기 vs 0-based offset

| 표에 적힌 값 | 통신에 실제로 쓰는 값 |
|---|---|
| 400001 | 기능코드 03 + offset 0 |
| 400003 | 기능코드 03 + offset 2 |
| 000001 (Coil) | 기능코드 01 + offset 0 |
| 001000 (Coil) | 기능코드 01 + offset 999 (`CoilHeartbeat`) |

- `400001` 같은 숫자는 **의미상 주소**다. 앞자리 `4`는 영역(Holding), 나머지는 그 영역 내 순번(1부터)이다.
- 실제 패킷에는 앞자리 `4`가 들어가지 않고 기능코드와 0-based offset만 들어간다.
- 코드의 상수는 0-based offset이고, 주석의 `// 400003`은 업체 표의 1-based 표기다.

## 5. 값은 어디에 저장되나

- Modbus는 통신 규약이라 저장 위치를 정하지 않는다. **서버(슬레이브) 구현이 정한다.**
- PLC는 내부 데이터 메모리(RAM, 일부는 배터리·플래시 백업)에, PC 프로그램은 `ushort[]` 같은 배열(RAM)에 보관한다.
- 이 프로젝트에서 NSCAN은 MES에게 값을 제공하는 **Modbus 서버** 역할로 보인다. (Modbus 서버 코드는 아직 확인하지 않았다.)

## 6. KEPServerEX 연동 (MES 업체 방문 시 사용)

KEPServerEX는 PTC(구 Kepware)의 산업용 OPC 서버다. 추정 구조는 다음과 같다.

```
NSCAN (Modbus 서버, RAM에 레지스터 보관)
   ▲  Modbus TCP 읽기 (기능코드 03)
KEPServerEX  ← Modbus 클라이언트 역할
   ▼  OPC UA/DA
MES
```

태그 설정 시 이 맵과 맞춰야 할 부분:

| 항목 | 이 맵 기준 |
|---|---|
| Holding 주소 표기 | `400001`부터 (1-based) |
| 32비트 카운터 / ngPart | Data Type **DWord** (또는 Long) |
| Percent | **Word** (주소는 2칸 간격) |
| Coil | `000001`부터, Boolean |
| DWord 워드 순서 | KEP Modbus 채널 설정의 "First DWord Low/High" 확인 |

**주의**: 워드 순서(상위/하위 워드)가 NSCAN과 KEP에서 다르면 값이 65536 배수로 커지거나 뒤바뀌어 보인다. 이상한 값이 읽히면 가장 먼저 확인한다.

## 7. 기능 코드 (Function Code) 전체 목록

Modbus 프로토콜 표준 명세(specification)에서 미리 정의해 놓은 약속.  
마스터가 슬레이브에게 요청 메시지를 보낼 때 **첫 번째 바이트**에 기능 코드를 실어 보낸다.

| 기능 코드 | 동작 | 대상 데이터 |
|---|---|---|
| **01** | Read Coils | 디지털 출력 (읽기) |
| **02** | Read Discrete Inputs | 디지털 입력 (읽기) |
| **03** | Read Holding Registers | 아날로그 출력 레지스터 (읽기) |
| **04** | Read Input Registers | 아날로그 입력 레지스터 (읽기) |
| **05** | Write Single Coil | 디지털 출력 1개 (쓰기) |
| **06** | Write Single Register | 레지스터 1개 (쓰기) |
| **0F (15)** | Write Multiple Coils | 디지털 출력 여러 개 (쓰기) |
| **10 (16)** | Write Multiple Registers | 레지스터 여러 개 (쓰기) |

- 슬레이브는 기능 코드를 보고 어떤 동작을 수행할지 판단한다.
- 전 세계 모든 Modbus 장치가 이 번호 체계를 공유하므로 제조사가 달라도 서로 통신 가능.

## 8. 확인이 필요한 항목

- [ ] NSCAN의 Modbus 서버 코드에서 32비트 값을 넣는 워드 순서
- [ ] KEPServerEX 쪽 워드 순서 설정 및 태그 Data Type
- [ ] 업체가 읽어간 값이 실제 값과 일치하는지
