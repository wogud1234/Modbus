# Modbus TCP: 규격이 정한 것 vs 정하지 않은 것

> 약어는 처음 등장할 때 풀네임을 병기했고, 맨 아래 **부록**에 약어표를 정리했다.

---

## 0. 핵심 요약

- "Modbus로 데이터 주고받자"고 하면 **통신의 틀(프레임 구조, 함수 코드, 바이트 순서)** 은 규격에 고정되어 있어 별도 협의가 필요 없다.
- 그러나 **그 틀 안에 담기는 데이터의 의미(어느 주소가 무엇인지, 값의 형식과 단위)** 는 규격이 정하지 않는다. 장비 제조사 문서(레지스터 맵)나 프로젝트별 협의로 맞춰야 한다.
- `400001` 같은 표기는 사람/툴용 표기이며 전선에 실리지 않는다. MES 드라이버가 함수 코드와 0-based 주소로 변환해 보내고, 서버는 변환 없이 받은 `start`를 그대로 쓴다.
- 함수 코드 번호(0x03 등)와 그 의미는 **규격이 고정**한 값이고, `4xxxx` 같은 접두어 표기는 규격이 아닌 **관례(드라이버 문법)** 이다. 접두어는 테이블만 정하고, 실제 함수 코드는 읽기/쓰기 동작에 따라 드라이버가 고른다.
- TCP(Transmission Control Protocol)와 커널은 바이트 스트림만 전달할 뿐 Modbus 형식을 모른다. 메시지 경계를 나누는 규칙은 **애플리케이션 계층 프로토콜인 Modbus**가 정하고, 양쪽 앱 코드가 그 규칙대로 바이트를 만들고 해석한다.

```
커널이 아는 것:  바이트, 바이트, 바이트, ...   (메시지 경계 없음)
Modbus 규격:     앞 7바이트는 MBAP 헤더, Length 필드가 뒤 길이를 알려줌
```

---

## 1. MBAP 헤더 파싱 코드의 의미

### 1.1 대상 코드

```csharp
var protocolId = (ushort)((header[2] << 8) | header[3]);
var length     = (ushort)((header[4] << 8) | header[5]);
var unitId     = header[6];
```

**Modbus TCP의 MBAP 헤더(Modbus Application Protocol header, 7바이트)를 파싱하는 코드이다. 바이트 배열에서 필드를 꺼내는 작업이다.**

### 1.2 MBAP 헤더 구조

| 오프셋 | 크기 | 필드 | 설명 |
|---|---|---|---|
| 0~1 | 2바이트 | Transaction ID | 요청/응답 짝을 맞추는 번호 (응답에 그대로 복사) |
| 2~3 | 2바이트 | Protocol ID | Modbus는 항상 `0x0000` |
| 4~5 | 2바이트 | Length | **뒤에 오는 바이트 수** (Unit ID 1바이트 + PDU) |
| 6 | 1바이트 | Unit ID | 슬레이브 주소 (게이트웨이 뒤의 시리얼 장치 구분용) |

이 뒤에 PDU(Protocol Data Unit: 함수 코드 + 데이터)가 이어진다.

```
|<------------- MBAP 헤더 (7B) ------------->|<------- PDU (최대 253B) ------->|
| TransID(2) | ProtoID(2) | Length(2) | UnitID(1) | 함수코드(1) | 데이터(n) |
```

### 1.3 `(header[2] << 8) | header[3]`의 의미

Modbus는 2바이트 값을 **빅 엔디안(big-endian, 상위 바이트가 먼저)** 으로 전송한다. 바이트 2개를 합쳐 16비트 정수로 복원하는 코드이다.

```
header[2] = 0x01, header[3] = 0x02 라고 하면

header[2] << 8      = 0x0100   (상위 바이트를 왼쪽으로 8비트 이동)
header[3]           = 0x0002
0x0100 | 0x0002     = 0x0102   (OR로 합침) → 258
```

- `length`도 같은 방식이고, `unitId`는 1바이트라 시프트 없이 그대로 읽는다.
- x86/x64 PC는 리틀 엔디안이라서 `BitConverter.ToUInt16(header, 2)`로 읽으면 바이트 순서가 뒤집혀 잘못된 값이 나온다. 그래서 수동으로 시프트해서 조립한다.

같은 동작을 아래처럼 쓸 수도 있다.

```csharp
using System.Buffers.Binary;

var protocolId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
var length     = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
```

### 1.4 파싱한 값의 이후 사용

```csharp
if (protocolId != 0) { /* Modbus 아님 → 연결 종료 또는 무시 */ }

int remaining = length - 1;   // Unit ID(1바이트)는 이미 읽었으므로
// remaining 바이트를 더 읽으면 PDU(함수코드 + 데이터) 전체
```

TCP는 바이트 스트림이라 **`length`가 "이 요청이 몇 바이트인지" 알려주는 유일한 단서**이다. 이 값을 보고 필요한 바이트가 다 올 때까지 `Read`를 반복하는 것이 MBAP 헤더를 먼저 파싱하는 이유이다.

### 1.5 검증 포인트

- `length`는 상대가 보낸 값이므로 그대로 믿으면 안 된다. Modbus 최대 PDU가 253바이트이므로 `length`는 최대 254이다. 0이거나 이를 초과하면 버퍼 할당 전에 검증하고 연결을 끊는 것이 안전하다.
- 응답을 만들 때는 Transaction ID를 요청 값 그대로 복사하고, Length는 응답의 `Unit ID + PDU` 길이로 다시 계산해 **빅 엔디안으로** 써야 한다.

---

## 2. 규격이 정한 것 (별도 협의 불필요)

Modbus Organization이 공개한 규격(MODBUS Application Protocol Specification, MODBUS Messaging on TCP/IP Implementation Guide)에 고정되어 있다. 제조사가 달라도 같은 문서를 보고 구현했기 때문에 별도 합의 없이 통신이 된다.

### 2.1 전송 계층

| 항목 | 규격 내용 |
|---|---|
| 전송 프로토콜 | TCP |
| 기본 포트 | 502 |
| 통신 방식 | 마스터(클라이언트)가 요청 → 슬레이브(서버)가 응답 (Request / Response) |

### 2.2 프레임 구조

| 항목 | 규격 내용 |
|---|---|
| MBAP 헤더 | 7바이트, 필드 순서 Transaction ID → Protocol ID → Length → Unit ID |
| Protocol ID | 항상 `0x0000` |
| Length | Unit ID + PDU의 바이트 수 |
| PDU 최대 크기 | 253바이트 (전체 ADU(Application Data Unit)는 최대 260바이트 = MBAP 7 + PDU 253) |
| 2바이트 값의 바이트 순서 | **빅 엔디안** (주소, 수량, 레지스터 값 모두) |
| 응답의 헤더 처리 | Transaction ID, Protocol ID, Unit ID를 요청 값 그대로 복사, Length는 응답 길이로 재계산 |

### 2.3 데이터 모델 (4개의 테이블)

| 테이블 | 크기 | 접근 | 용도 예 |
|---|---|---|---|
| Coils | 1비트 | 읽기/쓰기 | 출력 ON/OFF |
| Discrete Inputs | 1비트 | 읽기 전용 | 입력 접점 상태 |
| Input Registers | 16비트 | 읽기 전용 | 센서 측정값 |
| Holding Registers | 16비트 | 읽기/쓰기 | 설정값, 지령값 |

### 2.4 표준 함수 코드와 PDU 형식

| 코드 | 이름 | 요청 PDU | 정상 응답 PDU |
|---|---|---|---|
| 0x01 | Read Coils | FC + 시작주소(2) + 수량(2) | FC + 바이트수(1) + 비트 데이터 |
| 0x02 | Read Discrete Inputs | FC + 시작주소(2) + 수량(2) | FC + 바이트수(1) + 비트 데이터 |
| 0x03 | Read Holding Registers | FC + 시작주소(2) + 수량(2) | FC + 바이트수(1) + 레지스터 값(2×N) |
| 0x04 | Read Input Registers | FC + 시작주소(2) + 수량(2) | FC + 바이트수(1) + 레지스터 값(2×N) |
| 0x05 | Write Single Coil | FC + 주소(2) + 값(2) | 요청 에코 |
| 0x06 | Write Single Register | FC + 주소(2) + 값(2) | 요청 에코 |
| 0x0F | Write Multiple Coils | FC + 시작주소(2) + 수량(2) + 바이트수(1) + 데이터 | FC + 시작주소(2) + 수량(2) |
| 0x10 | Write Multiple Registers | FC + 시작주소(2) + 수량(2) + 바이트수(1) + 데이터 | FC + 시작주소(2) + 수량(2) |

(FC = Function Code, 함수 코드)

세부 규칙:

- Write Single Coil의 값: ON = `0xFF00`, OFF = `0x0000` (그 외 값은 오류)
- 코일 비트 패킹: 첫 번째 코일이 첫 바이트의 **LSB(Least Significant Bit, 최하위 비트)** 에 들어가고, 남는 비트는 0으로 채움
- 1회 요청 최대 수량: Read Coils/Discrete Inputs 2000개, Read Registers 125개, Write Multiple Registers 123개, Write Multiple Coils 1968개
- **프로토콜 상의 주소는 0부터 시작** (PDU 안의 시작 주소 `0x0000`이 첫 번째 항목)

### 2.5 예외(에러) 응답 형식

요청을 처리할 수 없으면 슬레이브가 다음 형식으로 응답한다.

```
PDU = (함수코드 | 0x80) + 예외 코드(1)
```

| 예외 코드 | 이름 | 의미 |
|---|---|---|
| 0x01 | Illegal Function | 지원하지 않는 함수 코드 |
| 0x02 | Illegal Data Address | 존재하지 않는 주소 |
| 0x03 | Illegal Data Value | 잘못된 수량/값 |
| 0x04 | Server Device Failure | 처리 중 장치 내부 오류 |

### 2.6 실제 프레임 예

**요청**: Unit ID 1, Holding Register 시작주소 0, 수량 2개 읽기

```
00 01 | 00 00 | 00 06 | 01 | 03 | 00 00 | 00 02
TransID  ProtoID Length UnitID FC   시작주소  수량
```

- Length = 6 → 뒤에 오는 바이트: Unit ID 1 + FC 1 + 주소 2 + 수량 2 = 6

**응답**: 레지스터 값이 10, 20인 경우

```
00 01 | 00 00 | 00 07 | 01 | 03 | 04 | 00 0A | 00 14
TransID  ProtoID Length UnitID FC 바이트수  값1=10  값2=20
```

- Transaction ID, Protocol ID, Unit ID는 요청과 동일
- Length = 7 → Unit ID 1 + FC 1 + 바이트수 1 + 데이터 4 = 7

---

## 3. 규격이 정하지 않은 것 (장비 문서 / 협의 필요)

프로토콜은 통신의 틀만 정하므로, 아래는 장비·프로젝트마다 다르다.

| 항목 | 규격이 정하지 않은 내용 | 왜 문제가 되나 |
|---|---|---|
| **레지스터 맵** | 어느 주소가 무엇을 의미하는지 (예: 주소 0 = 현재 위치, 주소 1 = 상태 비트) | 가장 중요. 이것이 없으면 값을 받아도 의미를 알 수 없음 |
| **다중 레지스터 값의 워드 순서** | 32비트 정수/float를 레지스터 2개로 나눌 때 상위 워드가 먼저인지 하위 워드가 먼저인지 (규격은 16비트 레지스터 내부만 빅 엔디안으로 정함) | 순서가 다르면 값이 완전히 다르게 해석됨 (Big / Little / Byte-swap / Word-swap 조합) |
| **데이터 타입** | 레지스터 값이 부호 있는 정수(int16)인지, 부호 없는 정수(uint16)인지, 비트 필드인지, 문자열인지 | 같은 `0xFFFF`가 65535 또는 -1로 해석됨 |
| **스케일과 단위** | 값에 곱할 배율 (예: 250 = 25.0 °C이면 ×0.1), 단위(mm, µm, rpm 등) | 정수만 전송되므로 소수점 표현 방식을 별도로 약속해야 함 |
| **주소 표기 방식** | 문서상 주소가 0부터인지 1부터인지, `40001` 같은 표기(Holding Register 1번) 사용 여부 | 규격의 PDU 주소는 0-based이지만 장비 매뉴얼과 Modbus 테스트 툴은 1-based 표기를 쓰기도 해서 1 차이가 나기 쉬움 |
| **Unit ID 사용 방식** | TCP 직결 장비가 Unit ID를 무시하는지, 특정 값(0x00, 0x01, 0xFF)만 허용하는지 | 제조사마다 다름. 틀리면 응답이 없거나 오류 |
| **지원 함수 코드 범위** | 장비가 표준 함수 중 어느 것을 지원하는지 | 미지원 코드를 보내면 Illegal Function 응답 |
| **제조사 확장(사용자 정의) 함수 코드** | 규격이 사용자 정의용으로 남겨둔 코드 범위(65~72, 100~110)의 동작 | 제조사 문서에만 존재 |
| **문자열 인코딩** | 레지스터에 문자열을 담을 때 문자 인코딩, 레지스터당 문자 순서 | 바이트 순서가 뒤집혀 보일 수 있음 |
| **쓰기 가능 범위와 유효 값** | 어느 주소가 쓰기 가능한지, 허용 값 범위, 쓰기 보호 조건 | 범위 밖이면 예외 응답 또는 무시(장비마다 다름) |
| **동시 접속 허용 수** | 몇 개의 TCP 연결을 동시에 받는지, 초과 시 동작 | 초과 연결이 거부되거나 기존 연결이 끊길 수 있음 |
| **응답 시간, 타임아웃, 폴링 주기** | 몇 ms 안에 응답하는지, 마스터가 얼마 간격으로 요청해도 되는지 | 너무 빠른 폴링은 장비 과부하 유발 |
| **데이터 갱신 시점과 일관성** | 여러 레지스터를 한 번에 읽을 때 같은 시점의 값이 보장되는지 | 32비트 값을 두 번에 나눠 읽으면 중간에 값이 바뀔 수 있음 |
| **Transaction ID 발급 방식** | 마스터가 어떤 규칙으로 번호를 붙이는지 (슬레이브는 그대로 돌려주기만 하면 됨) | 마스터 구현 사항 |
| **잘못된 주소/수량에 대한 세부 동작** | 예외로 응답할지, 0을 채워 줄지, 연결을 끊을지 | 규격은 예외 응답을 권장하지만 장비별 차이 존재 |

---

## 4. PDU 요청 파싱: 시작 주소와 수량 꺼내기

### 4.1 대상 코드

```csharp
var start    = (ushort)((pdu[1] << 8) | pdu[2]);
var quantity = (ushort)((pdu[3] << 8) | pdu[4]);
```

Modbus 요청의 **PDU(Protocol Data Unit)에서 "시작 주소"와 "수량"을 꺼내는 코드**이다. MBAP 헤더 파싱과 같은 방식(빅 엔디안 2바이트 조립)이고, 대상이 헤더가 아니라 PDU라는 점만 다르다.

### 4.2 읽기 요청(0x01, 0x02, 0x03, 0x04)의 PDU 구조

| 인덱스 | 크기 | 필드 | 코드 |
|---|---|---|---|
| `pdu[0]` | 1바이트 | 함수 코드 (Function Code) | `pdu[0]` |
| `pdu[1]`~`pdu[2]` | 2바이트 | **시작 주소** (Starting Address) | `start` |
| `pdu[3]`~`pdu[4]` | 2바이트 | **수량** (Quantity) | `quantity` |

### 4.3 값 계산 예

```
PDU: 03 | 00 00 | 00 02
     FC   시작주소  수량

start    = (0x00 << 8) | 0x00 = 0
quantity = (0x00 << 8) | 0x02 = 2
→ "Holding Register를 주소 0부터 2개 읽어 달라"

PDU: 03 | 01 2C | 00 0A
start    = (0x01 << 8) | 0x2C = 0x012C = 300
quantity = (0x00 << 8) | 0x0A = 10
→ "주소 300부터 10개 읽어라"
```

### 4.4 서버(슬레이브)가 이 값으로 하는 일

```csharp
// 1) 수량 검증 (규격 한도: 레지스터 읽기는 1~125)
if (quantity < 1 || quantity > 125)
    return Exception(fc, 0x03);   // Illegal Data Value

// 2) 주소 범위 검증
if (start + quantity > registers.Length)
    return Exception(fc, 0x02);   // Illegal Data Address

// 3) 응답 PDU 조립
var resp = new byte[2 + quantity * 2];
resp[0] = fc;
resp[1] = (byte)(quantity * 2);   // 바이트 수
for (int i = 0; i < quantity; i++)
{
    ushort v = registers[start + i];
    resp[2 + i * 2]     = (byte)(v >> 8);     // 상위 바이트 먼저 (빅 엔디안)
    resp[2 + i * 2 + 1] = (byte)(v & 0xFF);
}
```

### 4.5 주의할 점

- **이 형식은 읽기 계열(0x01~0x04) 기준이다.** Write Single Register(0x06)는 같은 위치에 `주소`와 `값`이 오고, Write Multiple(0x10)은 뒤에 바이트 수와 데이터가 추가된다. 함수 코드(`pdu[0]`)로 먼저 분기한 뒤에 이 코드를 써야 한다.
- **PDU 길이를 먼저 확인해야 한다.** 읽기 요청 PDU는 정확히 5바이트여야 한다. 더 짧으면 `pdu[3]`, `pdu[4]` 접근에서 `IndexOutOfRangeException`이 난다. MBAP `length`로 받은 바이트 수를 검증한 뒤 파싱한다.
- **`start`는 0-based 프로토콜 주소이다.** 장비 매뉴얼이 `40001` 같은 1-based 표기를 쓰면, 문서상 40001이 여기서는 `start = 0`에 해당한다. (5장 참고)

---

## 5. 레지스터 주소 표기(400001)와 실제 전송 주소

### 5.1 서버 코드의 주소 상수

```csharp
public const ushort HoldingTotalTestCounter = 0; // 400001
public const ushort HoldingOkWorkCounter    = 2; // 400003
public const ushort HoldingRcWorkCounter    = 4; // 400005
```

마스터가 요청 PDU의 시작 주소 자리에 **`0`, `2`, `4` 같은 프로토콜 주소(0-based)** 를 넣어 보내고, 서버의 `start`는 그 값으로 파싱된다. 주석의 `400001` 등은 사람이 보는 1-based 표기이며 전송되는 값이 아니다.

| 서버 상수 | 전송되는 `start` | 매뉴얼식 표기 (주석) |
|---|---|---|
| `HoldingTotalTestCounter` | 0 | 400001 |
| `HoldingOkWorkCounter` | 2 | 400003 |
| `HoldingRcWorkCounter` | 4 | 400005 |

주소가 0, 2, 4로 2씩 벌어진 것은 각 카운터가 레지스터 2개(32비트)를 쓰기 때문으로 보인다. 이 경우 마스터는 `quantity`를 2로 읽고, 두 워드를 합칠 때 **어느 워드가 상위인지**를 서버와 맞춰야 한다. 규격이 정하지 않은 부분이므로 레지스터 맵 문서에 함께 명시한다.

### 5.2 400001은 전선(wire)에 실리지 않는다

`400001`은 사람과 툴이 쓰는 **표기법**이다. 요청 PDU의 시작 주소는 2바이트(`ushort`, 최대 65535)라서 400001이라는 값은 담을 수도 없다.

| 표기 | 의미 |
|---|---|
| 앞자리 `4` | Holding Register 테이블을 뜻하는 접두어 (테이블 종류는 실제로는 **함수 코드**가 정함: 0x03 = Holding Register 읽기) |
| 뒤의 `00001` | 1번째 레지스터 (1-based) → 프로토콜 주소로는 `1 - 1 = 0` |

5자리 표기(`40001`)와 6자리 표기(`400001`)는 같은 의미이다. 6자리는 65536개 전체 주소를 표현하려고 쓴다.

### 5.3 누가 변환하나

**서버 앱이 변환하지 않는다. 변환은 MES(Manufacturing Execution System, 제조 실행 시스템) 쪽 Modbus 클라이언트(드라이버 / 라이브러리 / 테스트 툴)가 전송 전에 한다.**

```
MES 설정/화면:   400001  (Holding Register 1번)
        ↓ MES의 Modbus 드라이버가 변환
  - 앞자리 4 → 함수 코드 0x03(Holding Register 읽기)을 선택
  - 뒤의 00001 → 1 - 1 = 주소 0
        ↓
전송되는 PDU:   03 | 00 00 | 00 02
        ↓
서버 앱:        start = 0  (변환 없이 그대로 사용)
```

서버 코드에는 "400001을 0으로 바꾸는 로직"이 없고 있을 필요도 없다. 서버가 받는 것은 함수 코드(0x03)와 주소(0)뿐이라, 앞자리 `4`가 있었는지도 알 수 없다.

### 5.4 설정이 어긋나면 생기는 문제

변환을 MES 쪽이 하므로, **MES의 설정이 서버 상수와 어긋나면 서버는 알아채지 못하고 엉뚱한 레지스터를 읽어 준다.**

- MES 드라이버가 0-based/1-based 옵션을 잘못 잡아 `start = 1`로 보내면, 서버는 정상 요청으로 보고 `registers[1]`부터 응답한다. Total 카운터의 하위 워드부터 읽히게 되어 값이 이상해진다.
- MES 설정에 "주소 오프셋" 또는 "Base 0/1" 옵션이 있으면 그 기준을 서버 주석(400001 = 주소 0)과 맞춘다.
- 서버 쪽에서 확인하려면 요청이 들어올 때 `fc`, `start`, `quantity`를 로그로 남기는 것이 가장 확실하다. 로그에 `start=0`이 찍히면 MES가 400001을 0으로 변환해서 보내고 있는 것이다.

### 5.5 변환 주체: Modbus 마스터 역할을 하는 드라이버

요청을 만들어 보내는 쪽이면 어디든 이 변환을 한다. OPC 서버의 Modbus 드라이버, MES 내장 드라이버, Modbus Poll 같은 테스트 툴이 모두 해당한다. MES가 OPC(Open Platform Communications)를 거친다면 `400003` 태그 주소는 OPC 서버의 드라이버에 설정된 것이고, 변환도 그 드라이버가 한다.

```
400003 (태그 주소)
  ├ 앞자리 4  → Holding Register 테이블 → 함수 코드 선택 (읽기면 0x03)
  └ 00003     → 3 - 1 = 2 → 시작 주소
          ↓
PDU: 03 | 00 02 | 00 nn      → 서버 앱은 start=2 만 받음
```

400001이 주소 0이므로 400003은 주소 2이다.

### 5.6 함수 코드 번호와 접두어 규칙은 출처가 다르다

| 항목 | 정한 곳 |
|---|---|
| `0x03` = Read Holding Registers 등 **함수 코드 번호와 의미** | **Modbus 규격**이 고정 (전선에 실리는 값) |
| `4xxxx` = Holding Register 같은 **표기 규칙** | 규격이 아니라 **관례** (옛 Modicon PLC의 주소 영역에서 유래, 드라이버들이 채택) |

- 표기 접두어는 프로토콜 바깥의 약속이라 드라이버마다 다를 수 있다. 어떤 OPC 서버는 `400003`이 아니라 `HR3`, `4:3`, `Holding.3` 같은 문법을 쓰기도 한다.
- 전선에 실리는 것은 규격이 정한 **함수 코드 + 0-based 주소**뿐이라, 표기법이 달라도 서버 앱에는 영향이 없다.

### 5.7 접두어는 테이블만 정하고, 함수 코드는 동작(읽기/쓰기)까지 봐야 정해진다

접두어 하나가 함수 코드 하나로 고정되는 것은 아니다.

| 접두어 | 테이블 | 읽기 FC | 쓰기 FC |
|---|---|---|---|
| `0xxxx` | Coils | 0x01 | 0x05 (단일), 0x0F (다중) |
| `1xxxx` | Discrete Inputs | 0x02 | 쓰기 불가 |
| `3xxxx` | Input Registers | 0x04 | 쓰기 불가 |
| `4xxxx` | Holding Registers | 0x03 | 0x06 (단일), 0x10 (다중) |

같은 `400003`이라도 MES가 **읽으면** 드라이버가 `0x03`을, **쓰면** `0x06`이나 `0x10`을 골라 보낸다. 서버 앱은 그래서 `pdu[0]`(함수 코드)으로 분기해서 읽기/쓰기와 테이블 종류를 판단한다.

```csharp
switch (pdu[0])
{
    case 0x03: /* Holding Register 읽기 */ break;
    case 0x04: /* Input Register 읽기  */ break;
    case 0x06: /* Holding Register 단일 쓰기 */ break;
    case 0x10: /* Holding Register 다중 쓰기 */ break;
    default:   /* 예외 0x01 (Illegal Function) */ break;
}
```

### 5.8 확인할 점

- OPC 서버 드라이버에 **"주소 기준(zero-based / one-based)"** 같은 옵션이 있는 경우가 많다. 이 옵션에 따라 `400003`이 `start = 2`로도, `start = 3`으로도 전송될 수 있다.
- 서버 로그에 `fc`와 `start`가 어떤 값으로 찍히는지 한 번 확인해 두면 안전하다.
- 표기법(접두어 문법)은 드라이버 매뉴얼에서, 함수 코드와 주소의 실제 전송값은 서버 로그나 패킷 캡처로 확인한다.

---

## 6. 전체 순서도: MES가 요청을 보내고 서버 앱이 데이터를 찾아 반환하기

시나리오: MES가 **OkWorkCounter(400003, 32비트 = 레지스터 2개)** 값을 읽는다.

### 6.1 한눈에 보는 시퀀스

```
[MES]                     [네트워크 / 서버 커널 + NIC]            [서버 앱]
  │                                  │                              │
  │ ① 읽을 항목 지정: 400003, 2개    │                              │
  │ ② 드라이버가 주소 변환           │                              │
  │    400003 → FC=0x03, start=2     │                              │
  │ ③ ADU 조립 (MBAP + PDU)          │                              │
  │ ④ TCP send ─────────────────────▶│                              │
  │                                  │ ⑤ NIC 수신 → DMA → 인터럽트  │
  │                                  │    → TCP 스택 → 소켓 수신    │
  │                                  │    버퍼에 적재               │
  │                                  │                              │ ⑥ stream.Read → MBAP 7바이트
  │                                  │                              │ ⑦ MBAP 파싱 (length 검증)
  │                                  │                              │ ⑧ stream.Read → PDU
  │                                  │                              │ ⑨ FC 분기, start/quantity 파싱
  │                                  │                              │ ⑩ 검증 (수량, 주소 범위)
  │                                  │                              │ ⑪ 레지스터 저장소에서 값 조회
  │                                  │                              │ ⑫ 응답 PDU + MBAP 조립
  │                                  │◀───────────────────────────── │ ⑬ stream.Write (송신 버퍼로 복사)
  │◀───────────────────────────────── │ ⑭ 커널이 패킷화 → NIC 송신   │
  │ ⑮ 응답 수신, Transaction ID 매칭 │                              │
  │ ⑯ FC 확인(오류 여부), 값 복원    │                              │
  │ ⑰ 워드 순서에 맞춰 32비트 조립   │                              │
  │    → OkWorkCounter 값 획득       │                              │
```

### 6.2 단계별 상세

**[MES 측: 요청 만들기]**

| 단계 | 동작 |
|---|---|
| ① | 사용자 설정 또는 MES 로직이 "400003부터 2개 읽기"를 결정 (레지스터 맵 문서 기준) |
| ② | MES의 Modbus 드라이버가 표기를 프로토콜 값으로 변환: 앞자리 `4` → 함수 코드 `0x03`, `400003 - 400001 = 2` → `start = 2` |
| ③ | 요청 프레임(ADU, Application Data Unit) 조립: Transaction ID 발급(예: 1), Protocol ID = 0, Length = 6, Unit ID, PDU = `03 00 02 00 02` |
| ④ | 서버와 TCP 연결(이미 있으면 재사용)을 통해 전송. 데이터는 MES 커널의 송신 버퍼로 복사되고 이후 커널이 패킷화하여 전송 |

**[네트워크 / 서버 커널: 앱과 무관하게 자동 처리]**

| 단계 | 동작 |
|---|---|
| ⑤ | 서버 NIC가 프레임 수신 → DMA로 메모리에 복사 → 인터럽트 → ISR/DPC → TCP 스택이 4-튜플로 해당 연결의 소켓을 찾아 **소켓 수신 버퍼**에 페이로드 적재, ACK 처리 |

**[서버 앱: 요청 해석과 응답]**

| 단계 | 동작 |
|---|---|
| ⑥ | 스레드 풀 워커가 `stream.Read`로 MBAP 헤더 7바이트를 읽는다 (TCP는 바이트 스트림이므로 **7바이트가 찰 때까지 반복해서** 읽어야 함) |
| ⑦ | MBAP 파싱: `transactionId`, `protocolId`(≠ 0이면 Modbus가 아니므로 연결 종료), `length`(2~254 범위 검증), `unitId` |
| ⑧ | `length - 1`바이트만큼 PDU를 추가로 읽는다 (마찬가지로 다 찰 때까지 반복) |
| ⑨ | `pdu[0]`으로 함수 코드 분기. 미지원 코드면 예외 0x01 응답. 읽기 요청이면 PDU 길이(5바이트)를 확인하고 `start`, `quantity` 파싱 |
| ⑩ | 검증: `quantity`가 1~125 범위인지(아니면 예외 0x03), `start + quantity`가 레지스터 배열 범위 안인지(아니면 예외 0x02) |
| ⑪ | 레지스터 저장소에서 `registers[start ... start+quantity-1]` 값을 읽는다. 다른 스레드가 값을 갱신 중일 수 있으므로 lock 또는 스냅샷으로 일관성 확보 |
| ⑫ | 응답 PDU 조립: `FC`, `바이트 수(quantity × 2)`, 각 레지스터 값(상위 바이트 먼저). 응답 MBAP 조립: `transactionId`·`protocolId`·`unitId`는 요청 값 복사, `length = 1 + 응답 PDU 길이` |
| ⑬ | `stream.Write`로 응답 전송. 데이터는 커널 송신 버퍼로 복사되고 즉시 리턴. 이후 다시 ⑥으로 돌아가 다음 요청 대기 |

**[네트워크 / MES: 응답 처리]**

| 단계 | 동작 |
|---|---|
| ⑭ | 서버 커널이 패킷화하여 NIC로 송신, MES 쪽 커널이 수신 버퍼에 적재 |
| ⑮ | MES 드라이버가 응답 수신, **Transaction ID로 요청과 짝을 맞춤** |
| ⑯ | 함수 코드의 최상위 비트(0x80) 확인. 켜져 있으면 예외 응답이므로 예외 코드 처리 |
| ⑰ | 바이트 수만큼 값을 복원하고, 레지스터 맵에 정의된 **워드 순서**에 따라 2개 레지스터를 32비트로 조립하여 OkWorkCounter 값 획득 |

### 6.3 실제 바이트 예 (OkWorkCounter = 70000, 상위 워드 우선 가정)

70000 = `0x00011170`이므로 상위 워드 `0x0001`, 하위 워드 `0x1170`이다.

```
[요청: MES → 서버]
00 01 | 00 00 | 00 06 | 01 | 03 | 00 02 | 00 02
TransID ProtoID Length UnitID FC  start=2  quantity=2

[응답: 서버 → MES]
00 01 | 00 00 | 00 07 | 01 | 03 | 04 | 00 01 | 11 70
TransID ProtoID Length UnitID FC 바이트수 registers[2]  registers[3]

MES 조립: (0x0001 << 16) | 0x1170 = 70000
```

- 응답의 Transaction ID(`00 01`), Protocol ID, Unit ID는 요청과 동일하다.
- 응답 Length = Unit ID 1 + FC 1 + 바이트수 1 + 데이터 4 = 7
- **상위 워드가 먼저인지 하위 워드가 먼저인지는 규격이 정하지 않았다.** 서버 저장 방식과 MES 조립 방식이 반대면 `0x11700001`(= 292,552,705)처럼 전혀 다른 값이 된다. 레지스터 맵 문서에 반드시 명시한다.

### 6.4 서버 앱 처리 루프 코드 골격

```csharp
private void HandleClient(object state)
{
    using var client = (TcpClient)state;
    var stream = client.GetStream();
    var header = new byte[7];

    while (running)
    {
        // ⑥ MBAP 헤더 7바이트 (다 찰 때까지 반복)
        if (!ReadExactly(stream, header, 7)) break;   // 0 반환 = 상대가 연결 종료

        // ⑦ MBAP 파싱
        var transactionId = (ushort)((header[0] << 8) | header[1]);
        var protocolId    = (ushort)((header[2] << 8) | header[3]);
        var length        = (ushort)((header[4] << 8) | header[5]);
        var unitId        = header[6];

        if (protocolId != 0 || length < 2 || length > 254) break;   // 잘못된 프레임 → 연결 종료

        // ⑧ PDU 읽기
        var pdu = new byte[length - 1];
        if (!ReadExactly(stream, pdu, pdu.Length)) break;

        // ⑨~⑫ 함수 코드 분기, start/quantity 파싱, 검증, 조회, 응답 PDU 조립
        byte[] respPdu = Process(pdu);

        // ⑫ 응답 MBAP 조립 (빅 엔디안)
        var resp = new byte[7 + respPdu.Length];
        resp[0] = (byte)(transactionId >> 8); resp[1] = (byte)transactionId;
        resp[2] = 0; resp[3] = 0;                                  // Protocol ID
        int respLen = 1 + respPdu.Length;                          // Unit ID + PDU
        resp[4] = (byte)(respLen >> 8); resp[5] = (byte)respLen;
        resp[6] = unitId;
        Buffer.BlockCopy(respPdu, 0, resp, 7, respPdu.Length);

        // ⑬ 전송
        stream.Write(resp, 0, resp.Length);
    }
}

private static bool ReadExactly(NetworkStream s, byte[] buf, int count)
{
    int off = 0;
    while (off < count)
    {
        int n = s.Read(buf, off, count - off);
        if (n == 0) return false;     // 연결 종료
        off += n;
    }
    return true;
}
```

`ReadExactly`가 필요한 이유는 TCP가 바이트 스트림이기 때문이다. `Read`는 요청한 바이트 수보다 적게 반환할 수 있으므로, 필요한 바이트가 다 찰 때까지 반복해야 한다.

### 6.5 오류 경로

| 상황 | 서버 동작 |
|---|---|
| Protocol ID ≠ 0, Length 범위 초과 | Modbus 프레임이 아니거나 손상된 스트림. 경계를 복구할 수 없으므로 **연결 종료** |
| 미지원 함수 코드 | 예외 응답 `FC \| 0x80`, 예외 코드 `0x01` (Illegal Function) |
| 수량이 0 또는 한도 초과 | 예외 코드 `0x03` (Illegal Data Value) |
| 주소 범위 초과 | 예외 코드 `0x02` (Illegal Data Address) |
| 서버 내부 처리 오류 | 예외 코드 `0x04` (Server Device Failure) |
| 상대가 연결 종료 (`Read`가 0 반환) | 루프 탈출, `client` 닫기 |

예외 응답 PDU는 `(FC | 0x80)`, 예외 코드 2바이트이며, MBAP 헤더는 정상 응답과 같은 규칙(Transaction ID 복사, Length 재계산)으로 만든다.

### 6.6 역할 분담 정리

| 구간 | 담당 |
|---|---|
| 표기(400003) → 프로토콜 값(FC 0x03, start 2) 변환 | **MES 드라이버** |
| ADU 조립, Transaction ID 발급 | MES 드라이버 |
| 패킷화, 전송, 재전송, 수신 버퍼 적재, ACK | 양쪽 커널 + NIC |
| MBAP/PDU 파싱, 검증, 레지스터 조회, 응답 조립 | **서버 앱** |
| 응답 매칭, 예외 처리, 32비트 조립 | MES 드라이버 / MES 로직 |
| 레지스터 맵(주소 = 의미), 워드 순서, 스케일 | **양측 협의 사항 (규격에 없음)** |

---

## 7. 한눈에 보는 구분

```
[규격이 정함]                                  [장비/프로젝트가 정함]
 - TCP, 포트 502                                - 레지스터 맵 (주소 = 의미)
 - MBAP 7바이트 구조                            - 32비트/float 워드 순서
 - 16비트 필드 빅 엔디안                        - 부호/타입/스케일/단위
 - 함수 코드와 PDU 형식                         - Unit ID 사용 방식
 - 예외 응답 형식                               - 지원 함수 코드 범위
 - 4개 데이터 테이블                            - 주소 표기(0-based / 1-based)
 - 1회 요청 최대 수량                           - 동시 접속 수, 타임아웃
        ↓                                               ↓
   "통신이 되게 하는 것"                      "값의 의미를 알게 하는 것"
```

"Modbus TCP를 지원한다"는 정보만으로는 왼쪽만 보장된다. 실제 데이터를 주고받으려면 상대 장비의 **레지스터 맵 문서**가 필요하다.

---

## 8. 장비 연동 시 확인 체크리스트

상대 장비와 Modbus TCP로 연결하기 전에 문서나 담당자에게 확인할 항목이다.

- [ ] IP 주소와 포트 (기본 502인지)
- [ ] Unit ID (무시하는지, 고정값이 있는지)
- [ ] 사용할 함수 코드 (읽기 0x03/0x04, 쓰기 0x06/0x10 등)
- [ ] 레지스터 맵 (주소, 이름, 타입, 스케일, 단위, 읽기/쓰기 구분)
- [ ] 주소 표기 기준 (0-based인지 1-based인지, 40001 표기인지)
- [ ] MES 드라이버의 주소 기준 옵션(Base 0/1, 오프셋)과 서버 주소 상수의 일치 여부
- [ ] 드라이버(OPC 등)의 주소 표기 문법(`400003`, `HR3` 등)과 읽기/쓰기 시 사용하는 함수 코드
- [ ] 서버 로그로 실제 수신되는 `fc`, `start`, `quantity` 값 확인
- [ ] 32비트/float의 워드 순서와 바이트 순서
- [ ] 동시 접속 허용 수
- [ ] 권장 폴링 주기와 응답 타임아웃
- [ ] 예외 응답 종류와 쓰기 제한 조건

---

## 부록. 약어 / 용어 풀네임

| 약어 | 풀네임 | 설명 |
|---|---|---|
| MBAP | Modbus Application Protocol (header) | Modbus TCP 프레임 앞 7바이트 헤더 |
| PDU | Protocol Data Unit | 함수 코드 + 데이터 (전송 계층과 무관한 Modbus 핵심 부분) |
| ADU | Application Data Unit | MBAP 헤더 + PDU, Modbus TCP에서 실제 전송되는 전체 프레임 |
| FC | Function Code | 함수 코드. 요청의 종류를 나타내는 1바이트 |
| TCP | Transmission Control Protocol | 연결 지향, 신뢰성 있는 바이트 스트림 프로토콜 |
| PLC | Programmable Logic Controller | 산업용 제어기 |
| LSB | Least Significant Bit | 최하위 비트 |
| Coil | 코일 | 1비트 읽기/쓰기 출력 (PLC의 출력 접점에서 유래) |
| Holding Register | 홀딩 레지스터 | 16비트 읽기/쓰기 레지스터 |
| Input Register | 입력 레지스터 | 16비트 읽기 전용 레지스터 |
| Big-endian | 빅 엔디안 | 상위 바이트를 먼저 저장/전송하는 방식 (Modbus 표준) |
| Little-endian | 리틀 엔디안 | 하위 바이트를 먼저 저장하는 방식 (x86/x64 CPU 내부 표현) |
| Unit ID | 유닛 식별자 | 슬레이브 주소. TCP 직결 장비에서는 무시되거나 고정값을 쓰는 경우가 많음 |
| Transaction ID | 트랜잭션 식별자 | 요청/응답을 짝짓기 위한 번호 (마스터가 부여, 슬레이브는 그대로 복사) |
| MES | Manufacturing Execution System | 제조 실행 시스템. 여기서는 Modbus 마스터(클라이언트) 역할 |
| ushort | unsigned short | 부호 없는 16비트 정수 (0~65535) |
| Base 0 / Base 1 | 주소 기준 | 첫 번째 레지스터를 0으로 셀지 1로 셀지의 기준. 프로토콜은 0-based |
| 워드(Word) | 16비트 단위 | Modbus 레지스터 1개 = 1워드. 32비트 값은 2워드로 구성 |
| ISR / DPC | Interrupt Service Routine / Deferred Procedure Call | 인터럽트 처리 루틴 / 지연 프로시저 호출 |
| NIC | Network Interface Card | 랜카드 |
| DMA | Direct Memory Access | 장치가 CPU 개입 없이 메모리에 직접 접근하는 방식 |
| ACK | Acknowledgment | TCP 수신 확인 |
| OPC | Open Platform Communications (구 OLE for Process Control) | 산업 장비 데이터를 표준 방식으로 주고받는 통신 규격. OPC 서버가 Modbus 드라이버를 내장하는 경우가 많음 |
| Modicon | Modicon (Modbus를 만든 PLC 제조사, 현 Schneider Electric) | `0xxxx`/`1xxxx`/`3xxxx`/`4xxxx` 주소 영역 표기의 유래 |
