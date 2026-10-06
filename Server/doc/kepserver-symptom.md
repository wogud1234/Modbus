# KEPServerEX ↔ MVT 앱 Modbus 통신 증상 정리

작성일: 2026-10-06

## 1. 환경

- KEPServerEX 6.18 (데모/시간 제한 라이선스)
- 채널: `Modbus` / 디바이스: `ModSlave` (드라이버: Modbus TCP/IP Ethernet)
- 디바이스 ID: `<127.0.0.1>.1` (IP 127.0.0.1, Unit ID 1)
- 프로젝트 파일: `D:\KEPServerData\ModSlave.opf`
- 태그 41개 (Coil 11개, Holding DWord 30개), 주소는 `Addresses.cs`의 상수에 1-based 주소(+1, 400001 기준)로 대응
- 이 앱(MVT)은 Modbus 슬레이브(서버) 역할, KEPServer는 마스터(클라이언트)로 읽어야 하는 구조

## 2. 증상

- Runtime 연결 후 OPC Quick Client에서 `Modbus.ModSlave.*` 태그 전체가 Quality `Good`
- 모든 Value가 `0`, Timestamp/Update 횟수가 최초 1회 이후 변하지 않음
- **MVT 앱을 종료해도 Quality가 계속 `Good`** (정상이라면 `Bad`가 되어야 함)

## 3. 원인 추정

Runtime 이벤트 로그에 다음 내용이 있음.

- `Modbus | Starting unsolicited communication. Protocol = 'TCP', Port = 502`
- `Created memory for Modbus server device. Modbus server device ID = 1`

즉 KEPServer가 마스터로 앱에 접속하는 것이 아니라, **Unsolicited 기능으로 자기 자신이 Modbus 서버가 되어 포트 502를 열고 자체 메모리를 읽고 있는 상태**로 보임. 그래서 앱과 무관하게 `Good`, 값은 0.

이 상태에서는 앱이 포트 502를 열 때 충돌(바인딩 오류)할 수도 있음.

## 4. 조치 순서

1. `ModSlave` 디바이스 속성 → **Unsolicited** 그룹에서 Unsolicited 기능을 **Disable**
2. **Ethernet** 그룹의 Port Number를 앱이 여는 포트와 일치시킴 (기본 502)
3. **General → ID**를 앱이 있는 PC의 IP + Unit ID로 확인 (같은 PC면 `<127.0.0.1>.1`)
4. **Settings** 그룹 확인
   - 주소 표기: One-based (기본값)
   - 32비트 word 순서: First Word Low / High (앱 구현과 일치해야 함)
5. OK → 저장(`Ctrl+S`) → **Runtime → Reinitialize**
6. 앱 실행 전/후로 Quick Client 확인

## 5. 정상 판정 기준

| 상황 | 기대 결과 |
|---|---|
| 앱 종료 상태 | Quality `Bad` |
| 앱 실행 상태 | Quality `Good` |
| 검사 진행 | `HoldingTotalTestCounter`, `HoldingOkWorkCounter` 등 값 증가 |
| 쓰기 테스트 | `CoilWorkClearCmd`에 1 쓰기 → `CoilWorkClearAck` 변화 |

## 6. 참고

- 데모 라이선스는 런타임 시작 후 약 2시간 후 만료 (이번 세션 기준 오후 10:43). Runtime Service 재시작 시 다시 2시간 사용 가능
- 저장 시 암호화 창은 테스트 용도면 `No encryption` 선택 (`.opf`)
