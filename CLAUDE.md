# ModSlave 프로젝트

## 프로젝트 개요

KepServerEX6(마스터)와 Modbus 프로토콜로 통신하는 **슬레이브 앱** 개발 프로젝트.

### 통신 구조

```
KepServerEX6 (마스터)  ←→  ModSlave 앱 (슬레이브)
     OPC 서버                  이 프로젝트
```

- **슬레이브(Slave)**: 이 앱 — 데이터를 보유하고 마스터 요청에 응답
- **마스터(Master)**: KepServerEX6 — 슬레이브에 읽기/쓰기 요청을 보냄
- **프로토콜**: Modbus TCP 또는 RTU

## 학습 방식

유튜브 강의를 시청하면서 질문하면 내용 정리 및 설명 제공.  
정리된 내용은 `/note` 스킬로 `D:\.net\ModSlave\Server\note\` 폴더에 마크다운으로 저장.

## 설명 지침

- 약어를 사용할 때는 반드시 옆에 풀네임도 함께 표기  
  예: `TCP (Transmission Control Protocol)`, `OPC (OLE for Process Control)`

## 노트 관리 스킬

| 명령어 | 동작 |
|---|---|
| `/note <제목>` | `D:\.net\ModSlave\Server\note\<제목>.md` 새 파일 생성 |
| `/note add <제목>` | 기존 `D:\.net\ModSlave\Server\note\<제목>.md`에 내용 추가 |

### 예시

```
/note Modbus 기초 개념
/note add Modbus 기초 개념
```
