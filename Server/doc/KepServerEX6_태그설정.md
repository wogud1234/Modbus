# KepServerEX6 태그 설정

> **기준 파일**: `Server/Modules/Map/Addresses.cs`  
> **통신 방향**: KepServerEX6 (마스터) ↔ ModSlave 앱 (슬레이브)

---

## 주소 체계

| 구분 | KepServer 표기 (1-based) | Modbus 패킷 (0-based offset) | 변환 공식 |
|------|--------------------------|------------------------------|-----------|
| Coil | 000001 ~ | 0 ~ | KepServer 표기 - 1 |
| Holding | 400001 ~ | 0 ~ | KepServer 표기 - 400001 |

---

## Coil (000001번대) — Boolean

| Name | Description | Address | Data Type | Client Access |
|------|-------------|---------|-----------|---------------|
| CoilReady | 슬레이브 준비 완료 상태 | 000001 | Boolean | Read Only |
| CoilOpModeNormal | 운전 모드 — 자동 (Normal) | 000002 | Boolean | Read Only |
| CoilOpModeManual | 운전 모드 — 수동 (Manual) | 000003 | Boolean | Read Only |
| CoilStart | 시작 명령 | 000004 | Boolean | Read Only |
| CoilStop | 정지 명령 | 000005 | Boolean | Read Only |
| CoilDataReq | 데이터 요청 응답 (슬레이브 → 마스터) | 000006 | Boolean | Read Only |
| CoilDataCmd | 데이터 명령 (마스터 → 슬레이브) | 000007 | Boolean | Read/Write |
| CoilWorkClearCmd | Work 수량 초기화 명령 (마스터 → 슬레이브) | 000011 | Boolean | Read/Write |
| CoilWorkClearAck | Work 수량 초기화 완료 응답 (ACK) | 000012 | Boolean | Read Only |
| CoilHeartbeat | 하트비트 (생존 신호) | 001000 | Boolean | Read Only |

> **Client Access 기준**: `AllowWriteCoilOffsets = { CoilDataCmd, CoilWorkClearCmd }` — 슬레이브 앱이 허용한 쓰기 코일만 Read/Write, 나머지는 슬레이브가 직접 세팅하므로 Read Only

---

## Holding Register — 작업 카운터 (400001번대)

각 카운터는 32-bit (레지스터 2개 점유), 오프셋 간격 = 2.

| Name | Description | Address | Data Type | Client Access |
|------|-------------|---------|-----------|---------------|
| HoldingTotalTestCounter | 전체 검사 수량 누계 | 400001 | DWord | Read Only |
| HoldingOkWorkCounter | OK 판정 수량 누계 | 400003 | DWord | Read Only |
| HoldingRcWorkCounter | RC (재검) 판정 수량 누계 | 400005 | DWord | Read Only |
| HoldingNgWork1Counter | NG Group A 수량 누계 | 400007 | DWord | Read Only |
| HoldingNgWork2Counter | NG Group B 수량 누계 | 400009 | DWord | Read Only |
| HoldingNgWork3Counter | NG Group C 수량 누계 | 400011 | DWord | Read Only |
| HoldingNgWork4Counter | NG Group D 수량 누계 | 400013 | DWord | Read Only |
| HoldingNgWork5Counter | NG Group Side 수량 누계 | 400015 | DWord | Read Only |

---

## Holding Register — NG 파트별 카운터 (400017번대)

각 파트 카운터는 32-bit (레지스터 2개 점유), 오프셋 간격 = 2.

| Name | Description | Address | Data Type | Client Access |
|------|-------------|---------|-----------|---------------|
| HoldingNgPart1 | Rupture (파열) NG 수량 | 400017 | DWord | Read Only |
| HoldingNgPart2 | Height (높이) NG 수량 | 400019 | DWord | Read Only |
| HoldingNgPart3 | Aperture (구멍) 직경/진원도 NG 수량 | 400021 | DWord | Read Only |
| HoldingNgPart4 | Aperture 결함 크기 NG 수량 | 400023 | DWord | Read Only |
| HoldingNgPart5 | Inner1 얼룩 NG 수량 | 400025 | DWord | Read Only |
| HoldingNgPart6 | Inner1 긁힘 NG 수량 | 400027 | DWord | Read Only |
| HoldingNgPart7 | Inner1 찍힘 NG 수량 | 400029 | DWord | Read Only |
| HoldingNgPart8 | Inner2 얼룩 NG 수량 | 400031 | DWord | Read Only |
| HoldingNgPart9 | Inner2 긁힘 NG 수량 | 400033 | DWord | Read Only |
| HoldingNgPart10 | Inner2 찍힘 NG 수량 | 400035 | DWord | Read Only |
| HoldingNgPart11 | Outer 얼룩 NG 수량 | 400037 | DWord | Read Only |
| HoldingNgPart12 | Outer 긁힘 NG 수량 | 400039 | DWord | Read Only |
| HoldingNgPart13 | Outer 찍힘 NG 수량 | 400041 | DWord | Read Only |
| HoldingNgPart14 | Side 얼룩 NG 수량 | 400043 | DWord | Read Only |
| HoldingNgPart15 | Side 긁힘 NG 수량 | 400045 | DWord | Read Only |
| HoldingNgPart16 | Side 찍힘 NG 수량 | 400047 | DWord | Read Only |

---

## Holding Register — 판정 비율 (400049번대)

각 퍼센트 값은 32-bit (레지스터 2개 점유), 오프셋 간격 = 2.

| Name | Description | Address | Data Type | Client Access |
|------|-------------|---------|-----------|---------------|
| HoldingOkWorkPercent | OK 판정 비율 (%) | 400049 | DWord | Read Only |
| HoldingRcWorkPercent | RC (재검) 판정 비율 (%) | 400051 | DWord | Read Only |
| HoldingNgWork1Percent | NG Group A 비율 (%) | 400053 | DWord | Read Only |
| HoldingNgWork2Percent | NG Group B 비율 (%) | 400055 | DWord | Read Only |
| HoldingNgWork3Percent | NG Group C 비율 (%) | 400057 | DWord | Read Only |
| HoldingNgWork4Percent | NG Group D 비율 (%) | 400059 | DWord | Read Only |
| HoldingNgWork5Percent | NG Group Side 비율 (%) | 400061 | DWord | Read Only |

---

## 요약

| 구분 | 레지스터 수 | 주소 범위 |
|------|-------------|-----------|
| Coil | 1000개 (1-bit) | 000001 ~ 001000 |
| Holding | 62개 Word (31개 DWord) | 400001 ~ 400062 |
