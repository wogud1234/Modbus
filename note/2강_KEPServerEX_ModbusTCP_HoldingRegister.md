# 2강 — KEPServerEX에서 Modbus TCP Holding Register 읽기/쓰기

- 영상: https://www.youtube.com/watch?v=yNtUp3HVQyg
- 원본 자막: **힌디어 (자동 생성)** → 한국어 번역
- 길이: 약 11분 30초
- 주제: Modbus TCP 슬레이브 장치(PLC/게이트웨이)의 **Holding Register**를 KEPServerEX로 읽고 쓰는 방법

> **📝 자막 오인식 정정**
> 힌디어 자동 자막에 영어 용어가 섞여 있어 많이 틀리게 적혀 있어요.
>
> | 자막 표기 | 실제 의미 |
> |---|---|
> | कैप सर्वर / कप सर्वर / एप सर्वर | **KEPServerEX** |
> | मोड स्कन | **Modscan** (Modbus 마스터 테스트 도구) |
> | मोडबस पीसीपी / टीसीटी | Modbus **TCP** |
> | ओपीसी डीए / यूए | OPC **DA** / **UA** |
> | शूटिंग पॉइंट वैल्यू | Floating Point Values (부동소수점 설정, 추정) |
> | 40000, 4002 등 일부 주소 | 문맥상 `400001`, `400002` 등으로 보임 |

---

## 한눈에 보는 작업 흐름

| 순서 | 작업 | 시간 |
|:---:|---|:---:|
| 1 | 채널 생성 (Modbus TCP/IP Ethernet, 포트 502) | 01:03 |
| 2 | 디바이스 추가 (IP 주소, Slave ID) | 03:02 |
| 3 | **Modscan으로 먼저 데이터 확인** | 04:04 |
| 4 | 태그 추가 (Holding Register 주소 지정) | 05:08 |
| 5 | Quick Client로 값 확인 | 06:02 |
| 6 | 처음부터 다시 정리(복습) | 07:04 |
| 7 | 레지스터에 값 쓰기 (Write) | 09:28 |

---

## 상세 번역 & 해설

### 🎬 도입 `00:00 ~ 00:54`

**번역**
안녕하세요, 환영합니다. 이번 영상에서는 **Modbus TCP 서버**의 **Holding Register** 데이터를 KEPServerEX에서 어떻게 읽을 수 있는지 알아보겠습니다.

예를 들어 Schneider PLC처럼 Modbus TCP로 데이터를 내주는 PLC가 있을 때, 그 데이터를 KEPServerEX를 통해 읽는 방법입니다. 또는 **Modbus 시리얼을 Modbus TCP로 변환해 주는 게이트웨이**가 있을 때, 그 게이트웨이의 Modbus TCP 데이터를 KEPServerEX에서 읽는 방법도 같습니다.

KEPServerEX를 써 보셨다면 아시겠지만, KEPServerEX가 읽은 데이터는 **OPC DA 또는 OPC UA**로 제공됩니다. 두 가지 옵션이 모두 있습니다. 이번 영상의 기본 목표는 Modbus TCP의 Holding Register 데이터를 **읽고 쓰는** 것입니다.

**💡 해설**
- 이 영상의 구도는 `[Modbus TCP 슬레이브 장치] ← KEPServerEX(마스터) → OPC DA/UA` 입니다. 앞서 정리한 "Case A(앱이 슬레이브)" 구조와 같아요.
- **내 앱이 Modbus TCP 슬레이브라면, 이 영상의 "게이트웨이/PLC" 자리에 내 앱이 들어간다**고 보면 됩니다.

---

### 🔌 1. 채널 생성 `01:03 ~ 03:02`

**번역**
먼저 KEPServerEX를 엽니다. Modbus 장치의 데이터를 읽으려면 **새 채널**을 만들어야 합니다. **Connectivity**에서 우클릭하고 **New Channel**로 갑니다.

어떤 유형의 채널을 만들지 선택하는 화면이 나옵니다. 지금은 Modbus TCP 장치의 Holding Register를 읽을 것이므로 "Modbus"를 입력해 검색합니다. 선택지로 Modbus ASCII Serial, Modbus Plus, Modbus RTU Serial, Modbus Slave RTU Serial, **Modbus TCP/IP Ethernet** 등이 나옵니다. 우리는 Modbus TCP/IP Ethernet으로 Holding Register를 읽고 쓸 것이므로 이것을 선택하고 Next를 누릅니다.

채널 이름을 묻는데, 여기서는 "Modbus"라고 하겠습니다. Next를 누릅니다.

이후 **Write Optimizations**, **Virtual Network**, **Transactions per cycle** 같은 설정은 **기본값 그대로** 둡니다. **Network Adapter**도 기본값(Default)으로 둡니다. 만약 기본값으로 데이터가 들어오지 않으면, 여기에 여러 네트워크 옵션이 있으니 올바른 네트워크를 선택할 수 있습니다.

그다음 **Optimization / Duty Cycle** 설정도 그대로, **Floating Point 값 설정**도 그대로 두고 Next를 누릅니다.

마지막으로 Modbus TCP 데이터를 어느 **포트**에서 읽을지 묻습니다. **포트는 502**, **프로토콜은 TCP/IP**로 두고 Next → **Finish**를 누릅니다. 이렇게 Modbus 채널이 생성되었습니다.

**💡 해설**
- **채널 = 통신 방식(물리 계층/프로토콜) 단위**입니다. 같은 Modbus TCP라도 NIC가 다르거나 포트가 다르면 채널을 나누기도 합니다.
- 채널 마법사 기본값 의미(참고용)
  - *Network Adapter*: PC에 NIC가 여러 개일 때 어느 NIC로 통신할지 지정. 장비가 다른 대역에 있어 통신이 안 되면 여기를 의심하세요.
  - *Optimization / Duty Cycle*: 읽기/쓰기 요청 비율 조절. 처음엔 기본값이면 충분합니다.
  - *Floating Point Values*: 비정상 부동소수점(NaN, Inf) 처리 방식.
- **포트 502**는 Modbus TCP의 표준 포트입니다. 내 슬레이브 앱이 다른 포트를 쓰면 이 값을 맞춰야 해요. (영상 `04:39` 이후에도 "장비가 503, 504 포트를 쓰면 그 포트를 입력"이라고 설명)
- 영상에서 채널 유형 목록에 "Modbus Slave"류가 보이지만, 자동 자막이라 정확한 명칭은 확인이 필요하고, 사용 가능 여부는 버전/라이선스에 따라 달라요.

---

### 🖧 2. 디바이스 추가 `03:02 ~ 04:08`

**번역**
채널이 만들어졌으니 이제 **디바이스를 추가**합니다. "Click to add a device"를 클릭합니다. 디바이스 이름은, 제 경우 Modbus 시리얼을 Modbus TCP로 변환하는 **게이트웨이**가 연결되어 있으므로 "Gateway"라고 하겠습니다. Next를 누릅니다.

모델(Model)은 "Modbus"로 합니다. Modbus에서 데이터를 읽을 것이기 때문입니다. Next를 누릅니다.

그다음 Modbus TCP 게이트웨이의 **IP 주소와 Slave ID**를 묻습니다. IP 주소는 **192.168.1.200**입니다. 이것이 제 게이트웨이의 IP 주소이고, **Slave ID는 1**입니다. Next를 누르고 이후 화면은 그대로 진행해 Finish를 누릅니다.

**💡 해설**
- **디바이스 = 실제 통신 상대(장비 한 대)** 입니다. IP 주소로 접속 대상을 정하고, Slave ID(Unit ID)로 장비 내 슬레이브를 구분합니다.
- **Slave ID(Unit Identifier)** 는 Modbus 프로토콜 프레임에 들어가는 1바이트 값입니다. 게이트웨이 뒤에 여러 시리얼 슬레이브가 있을 때 이 값으로 구분합니다.
- 내 앱이 Modbus TCP 슬레이브라면 **Unit ID를 어떻게 처리할지**(특정 값만 응답할지, 아무 값이나 받을지)를 정해 두고 Kep 설정과 맞춰야 합니다.

---

### 🔍 3. Modscan으로 먼저 확인 `04:04 ~ 05:08`

**번역**
이제 태그를 추가해야 하는데, 그 전에 **먼저 Holding Register에 데이터가 실제로 들어오고 있는지 확인**하겠습니다. 이를 위해 **Modscan**을 엽니다.

먼저 Holding Register 값들을 **10**으로 설정해 둡니다. 그다음 Connection → Connect로 가서, 게이트웨이의 IP 주소를 입력하고 **서비스 포트는 502**로 합니다. 만약 PLC가 Modbus TCP로 데이터를 주고 있다면 여기에 PLC의 IP 주소를 입력하면 되고, 장비가 **503, 504 같은 다른 포트**를 쓴다면 그 포트를 지정해야 합니다.

OK를 누르면, 제 Holding Register **400001번, 400002번**에 데이터가 들어오는 것을 볼 수 있습니다.

**💡 해설 (실무 팁)**
- **Kep에 태그를 만들기 전에 Modscan 같은 테스트 도구로 먼저 통신을 검증**하는 순서는 매우 좋은 습관입니다. 문제가 생겼을 때 "장비 문제인지 Kep 설정 문제인지"를 분리할 수 있어요.
- **Modscan은 Modbus 마스터 도구**입니다. 즉 **내가 만든 슬레이브 앱이 제대로 응답하는지 테스트하는 용도로도 그대로 쓸 수 있어요.**
  - 개발 순서 추천: ① 앱 단독 개발 → ② Modscan(마스터)으로 레지스터 읽기/쓰기 검증 → ③ MES 또는 Kep 연동
- 자막에는 `40000`, `4002`로 나오지만 문맥상 `400001`, `400002`로 보입니다.

---

### 🏷️ 4. 태그 추가 `05:08 ~ 06:02`

**번역**
같은 데이터를 KEPServerEX에서 읽으려면, 디바이스에서 **New Tag**로 갑니다. 첫 번째 태그는 "Tag1"로 하고, **주소는 Modscan에서 보이는 주소와 똑같이** 지정합니다. 여기서는 **400001**입니다. **데이터 타입은 기본값(Default)** 으로 둡니다. 데이터가 어떤 타입인지 알고 있다면 여기서 데이터 타입을 바꿀 수 있고, 아니면 Default로 두고 OK를 누르면 됩니다.

그다음 이 태그를 복사해서 붙여넣고, 두 번째 태그의 주소를 **400002**로 바꿉니다. Modscan에서 400001, 400002에 데이터가 들어오고 있기 때문입니다.

**💡 해설 (주소 체계 — 중요)**
- Kep의 Modbus 드라이버에서 `4xxxxx` 형태는 **Holding Register(읽기/쓰기)** 영역을 뜻합니다. 대표적인 영역 접두사는 다음과 같이 알려져 있어요.

| 주소 영역 | 종류 | 용도 |
|---|---|---|
| `0xxxxx` | Coil | 비트 읽기/쓰기 |
| `1xxxxx` | Discrete Input | 비트 읽기 전용 |
| `3xxxxx` | Input Register | 16비트 읽기 전용 |
| `4xxxxx` | **Holding Register** | 16비트 읽기/쓰기 |

- `400001`은 보통 **첫 번째 Holding Register**(프로토콜 상의 주소 오프셋 0)에 해당합니다. 이 **0-based / 1-based 차이로 한 칸씩 밀려 읽히는 문제**가 흔하니, 내 앱의 레지스터 맵과 대조해서 확인하세요. 설정(5자리/6자리 표기, 베이스 오프셋)은 드라이버 버전과 디바이스 설정에 따라 다를 수 있습니다.
- **데이터 타입 Default**는 Holding Register 기본인 16비트 Word 계열로 읽습니다. `float`(2개 레지스터)나 `int32`를 쓰려면 **타입을 명시**하고, **워드/바이트 순서**도 맞춰야 합니다.

---

### 👀 5. Quick Client로 확인 `06:02 ~ 07:04`

**번역**
Modscan에서 읽고 있는 데이터를 KEPServerEX에서도 보려면 **Quick Client**를 열어야 합니다. Quick Client를 열면 채널는 Modbus, 디바이스는 Gateway입니다. Modscan에서 읽히던 값이 그대로 여기에도 나타나는 것을 볼 수 있습니다. (예: **11850**)

이렇게 해서 Modbus TCP 장치의 데이터를 KEPServerEX에서 읽을 수 있습니다. 이 **OPC Quick Client는 사실 OPC DA 클라이언트**입니다. 즉 KEPServerEX는 Modbus TCP 데이터를 **OPC DA로** 줄 수 있고, **OPC UA로도** 줄 수 있습니다. OPC UA 설정을 활성화하는 방법은 다음 영상들에서 다루겠습니다.

**💡 해설**
- **Quick Client = 태그가 잘 동작하는지 바로 확인하는 내장 OPC 클라이언트**입니다. 값이 나오면 "장비 → Kep" 구간은 정상이라는 뜻이에요.
- 이후 MES/SCADA 등 외부 OPC 클라이언트는 이 태그를 `채널.디바이스.태그` 경로로 읽어갑니다. (예: `Modbus.Gateway.Tag1`)
- 값 옆의 **Quality(품질)** 가 `Good`인지 꼭 확인하세요. `Bad`면 통신 실패입니다.

---

### 🔁 6. 처음부터 다시 정리 (복습) `07:04 ~ 09:28`

**번역**
이제 영상을 정리하면서 복습해 보겠습니다. 이 채널을 삭제하고 새로 만들어 보겠습니다.

Connectivity → New Channel → **Modbus TCP/IP Ethernet 채널**을 선택하고 Next. 채널 이름은 "Modbus 채널"로 하고(이름은 바꿔도 됩니다), 이후 설정은 그대로 두고 **Finish까지** 갑니다.

그다음 디바이스를 추가합니다. 제 디바이스는 게이트웨이이므로 "Gateway"라고 적었습니다. 디바이스가 PLC나 다른 것이라면 그 이름을 적으면 됩니다. 게이트웨이가 많다면 **일정한 형식을 정해서 각각 이름을 구분**할 수도 있습니다.

모델은 Modbus이고, 게이트웨이의 **IP 주소와 Slave ID**를 정의해야 합니다. IP 주소는 192.168.1.200입니다. Modscan에서 보면 **Device ID가 1**인데, 이 Device ID가 곧 Slave ID입니다. 그러므로 Kep에도 Slave ID를 1로 넣어야 합니다. 게이트웨이나 PLC의 IP 주소가 다르면 그 주소를 입력하면 됩니다. 이후는 **Next만 눌러 Finish까지** 가고, 아무것도 바꾸지 않습니다.

그다음 태그를 추가합니다. 첫 번째 태그의 주소는 **400001**, 이를 복사·붙여넣어 만든 두 번째 태그는 **400002**, 세 번째 태그는 **400003**입니다. (자막상 `4003`이지만 문맥상 400003으로 추정) Modscan에서 해당 데이터를 읽을 수 있고, 같은 데이터를 Quick Client에서도 확인합니다. Quick Client에서 **352, 354** 같은 값이 보입니다.

**💡 해설**
- 한 번에 **채널 → 디바이스 → 태그** 3단계를 거치는 것이 Kep 프로젝트 구조의 기본입니다.

```
Channel (Modbus)               ← 통신 방식 / NIC / 포트
 └─ Device (Gateway)           ← IP 주소 + Slave ID
     ├─ Tag1  (400001)         ← 레지스터 주소
     ├─ Tag2  (400002)
     └─ Tag3  (400003)
```

- **Modscan의 Device ID = Kep의 Slave ID** 입니다. 용어가 달라서 헷갈리기 쉬워요.
- 태그를 수백 개 만들어야 한다면 복사·붙여넣기 대신 **CSV 가져오기(태그 임포트/익스포트)** 나 앞서 설치 화면에서 본 **Configuration API**로 자동화할 수 있습니다.

---

### ✍️ 7. 값 쓰기 (Write) `09:28 ~ 10:11`

**번역**
그렇다면 KEPServerEX를 통해 Modbus TCP의 Holding Register에 **데이터를 쓰려면** 어떻게 할까요? Quick Client에 **Synchronous Write** 옵션이 있습니다. 예를 들어 **43번 위치에 55**라는 값을 쓰고 싶다면, 값을 입력하고 **Apply**, 그다음 **OK**를 누릅니다. 그러면 43번에 값 **55**가 들어간 것을 확인할 수 있습니다.

반대로 Modscan에서 값을 바꾸면, KEPServerEX 쪽 값도 같이 바뀝니다. 이렇게 Modbus TCP 서버의 Holding Register 데이터를 KEPServerEX를 통해 **읽을 수도 있고, 쓸 수도 있습니다.**

**💡 해설**
- **Holding Register는 읽기/쓰기 모두 가능**한 영역이라 쓰기가 됩니다. (Input Register `3xxxxx`나 Discrete Input `1xxxxx`는 읽기 전용이라 쓰기가 안 돼요.)
- 쓰기를 하면 Kep이 슬레이브에 **Write Single/Multiple Register(FC06/FC16)** 요청을 보냅니다. **내 슬레이브 앱이 이 Function Code를 지원하고 쓰기를 처리해야** 정상 동작합니다.
- 태그의 **Client Access** 속성이 `Read Only`로 되어 있으면 쓰기가 막힙니다. 쓰기가 안 될 땐 이 속성을 확인하세요.
- 영상 자막상 "43에 55"는 주소 표기가 정확하지 않아 보여, 어떤 주소(태그)인지는 영상에서 직접 확인이 필요해요.

---

### 🧾 정리 및 마무리 `10:11 ~ 11:30`

**번역**
여기 있는 OPC Quick Client는 **OPC DA용 Quick Client**입니다. KEPServerEX의 데이터를 OPC DA 클라이언트에서 읽으면 태그가 이런 식으로 만들어집니다. KEPServerEX를 써 보셨다면 이 부분은 이미 아실 겁니다.

요약하면, 우리는 **PLC 또는 Modbus TCP 서버가 실행 중인 게이트웨이**가 있을 때, 그 Modbus TCP의 Holding Register 데이터를 KEPServerEX에서 읽었고, 그 데이터를 KEPServerEX를 통해 **OPC(DA)로 전달**할 수 있으며, Quick Client에서 OPC로 데이터가 들어오는 것도 확인했습니다. 이렇게 Modbus TCP 데이터를 KEPServerEX에서 읽을 수 있습니다.

영상에서 의문점이나 이해되지 않는 부분이 있으면 댓글을 남기거나 이메일로 문의해 주세요. 다음 영상에서 만나요. 그때까지 건강하세요. 안녕히 계세요, 감사합니다.

---

## ✅ 따라 하기 체크리스트

- [ ] **Modscan 등 마스터 도구로 먼저** 대상 장비(내 앱 포함)의 레지스터 읽기가 되는지 검증
- [ ] Kep: **채널** 생성 (Modbus TCP/IP Ethernet, 포트 502)
- [ ] Kep: **디바이스** 추가 (IP 주소, Slave ID)
- [ ] Kep: **태그** 추가 (`400001` 등, 데이터 타입 확인)
- [ ] **Quick Client**에서 값 + Quality(Good) 확인
- [ ] 쓰기 테스트 (Holding Register, Client Access 속성 확인)
- [ ] 값이 어긋나면: 주소 오프셋(0/1-based), 데이터 타입, 워드/바이트 순서 점검

## 🔗 내 앱(Modbus 슬레이브)에 적용한다면

| 영상 내용 | 내 앱 기준으로 |
|---|---|
| 게이트웨이/PLC (슬레이브) | **내 앱** (슬레이브) |
| KEPServerEX (마스터) | Kep이 내 앱을 폴링 (MES가 직접 붙으면 Kep 불필요) |
| IP 192.168.1.200, 포트 502 | 내 앱이 도는 PC의 IP, 앱이 Listen하는 포트 |
| Slave ID 1 | 앱이 응답할 Unit ID |
| Modscan | 앱 개발 중 검증용 마스터 도구로 활용 |
| 쓰기 (FC06/16) | 앱이 쓰기 요청을 처리하도록 구현 |

> ⚠️ 이 문서는 **힌디어 자동 자막**을 번역한 것이라 숫자(주소, IP 등)와 일부 용어에 오인식이 있을 수 있어요. "해설"은 제가 덧붙인 일반 설명이므로, 실제 주소 체계와 메뉴 이름은 사용하는 KEPServerEX 버전에서 확인하세요.
