# TcpListener / Accept / Stream 동작 원리 정리 (v2)

> 대상 환경: Windows, C#/.NET, Modbus TCP Slave 서버 구현
> 약어는 처음 등장할 때 풀네임을 함께 표기했고, 맨 아래 **부록 A**에 전체 약어표를 정리했다.

---

## 0. 핵심 요약

| 항목 | 실행 주체 |
|---|---|
| `new TcpListener(ip, port)` | 설정값만 보관 (포트 미점유, 시스템 콜 없음) |
| `Start()` (socket / bind / listen) | 호출한 스레드, 즉시 리턴 (스레드 생성 없음) |
| 패킷 수신, 3-way 핸드셰이크, accept 큐 적재 | 커널(Kernel) + NIC(Network Interface Card), 앱 개입 없음 |
| `AcceptTcpClient()` | **우리 코드가 직접 호출**. accept 큐에서 연결 1개 꺼냄. 비어 있으면 호출 스레드 블로킹 |
| `client.GetStream()` | 시스템 콜 없음. 소켓 핸들을 쓰는 .NET 래퍼(`NetworkStream`) 생성 |
| `stream.Read()` / `Write()` | 커널 버퍼 ↔ 앱 메모리 사이 **데이터 복사** (+ 데이터 없으면 대기) |
| `Stop()` | 리스너 소켓 닫기(포트 해제). 대기 중인 Accept는 예외로 깨어남 |

- `Start()`는 스레드를 만들지 않는다.
- `Start()`는 `Accept`를 호출하지 않는다. Accept 루프는 우리가 직접 만들어야 한다.
- `AcceptTcpClient()`도 스레드를 만들지 않는다. `accept()` 시스템 콜 1회 호출이며, 큐가 비면 **호출 스레드가 블로킹**된다.
- `Listen()`의 "대기 상태"는 스레드가 아니라 **소켓의 상태(LISTEN state)** 이다.

---

## 1. Start()는 스레드를 생성하는가?

**아니오.** `Start()`는 소켓을 만들어 `Bind()` → `Listen(backlog)`를 호출하고, 호출 스레드에서 동기적으로 실행된 뒤 바로 리턴한다. 이후 연결 수신은 OS(Operating System, 운영체제) 커널이 처리한다.

```csharp
listener = new TcpListener(ip, config.Port);
listener.Start();          // socket + bind + listen 만 수행

// 연결 수락은 별도로 호출해야 함
TcpClient client  = listener.AcceptTcpClient();              // 동기: 호출 스레드 블로킹
TcpClient client2 = await listener.AcceptTcpClientAsync();   // 비동기: 대기 중 스레드 점유 X (IOCP 기반)
```

- `AcceptTcpClient()`: 연결이 올 때까지 호출 스레드를 블로킹한다. UI(User Interface) 스레드에서 부르면 WinForms가 멈춘다.
- `AcceptTcpClientAsync()`: IOCP(I/O Completion Port, 입출력 완료 포트) 기반이라 대기 중 스레드를 점유하지 않고, 연결이 오면 스레드 풀 스레드에서 continuation(이어서 실행되는 코드)이 실행된다.

### 일반적인 Accept 루프 (async 버전)

```csharp
listener = new TcpListener(ip, config.Port);
listener.Start();
_ = Task.Run(AcceptLoopAsync);

private async Task AcceptLoopAsync()
{
    while (running)
    {
        try
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
        catch (ObjectDisposedException) { break; }   // Stop() 시 발생
        catch (SocketException) { /* 로깅 후 계속/종료 판단 */ }
    }
}
```

---

## 2. 생성자와 Start()의 차이

- `new TcpListener(ip, port)`: IP(Internet Protocol) 주소/포트 정보만 객체에 저장. 포트 미점유.
- `Start()`: 실제로 소켓 생성 후 `Bind()`(포트 점유) → `Listen()`(LISTEN 상태 진입).

```
new TcpListener   → 설정값만 보관 (포트 미점유)
Start()           → Bind + Listen (포트 점유, 접속 받을 수 있는 상태)
AcceptTcpClient() → 큐에서 연결 꺼내 TcpClient 획득
Stop()            → 소켓 닫기 (포트 해제)
```

### 실무 주의점

- 포트가 이미 사용 중이면 `Start()`에서 `SocketException`이 발생 → try/catch 권장.
- `Start()`만 하고 Accept를 안 하면, 클라이언트는 `connect()`가 성공한 것처럼 보이지만(backlog에 쌓이므로) 서버는 아무도 처리하지 않는다. backlog가 가득 차면 접속이 지연되거나 거부된다.

---

## 3. Listen()은 스레드인가?

**아니오.** "대기 상태"는 스레드가 돌며 기다린다는 뜻이 아니라 **소켓의 상태(state)** 를 의미한다.

`Listen(backlog)`는 시스템 콜 1회로 끝나며 커널에 다음을 알리고 즉시 리턴한다.

- 이 소켓은 이제 LISTEN 상태로 연결을 받는 용도이다.
- 완료된 연결을 최대 backlog개까지 큐에 보관한다.

이후 커널 내부에는 이 소켓의 상태와 큐가 존재할 뿐, 우리 프로세스의 스레드는 돌고 있지 않다.

---

## 4. Start() 이후 커널에서 일어나는 일 (원리)

### 4.1 Start()가 하는 일: 시스템 콜 3개

```
socket()  → 커널이 소켓 구조체 생성, 핸들 반환
bind()    → 소켓을 (IP, 포트)에 연결. 커널의 포트 테이블에 등록
listen()  → 소켓 상태 CLOSED → LISTEN 변경, accept 큐 생성
```

Windows에서는 `WSASocket` / `bind` / `listen`이며, Winsock(Windows Sockets) → AFD(Ancillary Function Driver, 소켓 드라이버) → tcpip.sys(TCP/IP 스택 드라이버)로 들어간다. 결과로 커널 메모리에 남는 것은 **"포트 N번, LISTEN 상태"라는 데이터 구조**뿐이다.

### 4.2 패킷 수신 경로

```
NIC가 프레임 수신
  → DMA(Direct Memory Access)로 메모리 버퍼에 복사
  → 하드웨어 인터럽트 발생
  → NIC 드라이버 ISR(Interrupt Service Routine) / DPC(Deferred Procedure Call) 실행 (커널 컨텍스트)
  → 이더넷/IP 헤더 파싱
  → TCP(Transmission Control Protocol) 스택: 목적지 포트로 소켓 테이블 조회
  → LISTEN 상태 소켓 발견
  → TCP 상태 머신 실행
```

- 이 코드는 인터럽트/DPC 컨텍스트의 **커널 코드**이며 우리 프로세스의 스레드가 아니다.
- 특정 스레드가 "기다리다 깨어나는" 구조가 아니라, 패킷 도착이라는 하드웨어 이벤트가 CPU를 끌어와 커널 코드를 실행시킨다.

### 4.3 3-way 핸드셰이크도 커널이 수행

```
클라이언트 SYN(Synchronize) 도착
  → 커널이 SYN 큐(half-open)에 항목 생성, SYN+ACK(Acknowledgment) 송신
클라이언트 ACK 도착
  → 연결 ESTABLISHED 전환
  → accept 큐(완료 연결 큐)에 추가
```

이 시점에 앱은 아무것도 모르는 상태이며, 클라이언트 입장에서는 `connect()`가 성공한다.

### 4.4 Start() 호출 여부와 수신 경로의 관계

**NIC 수신 → DMA → 인터럽트 → ISR/DPC → 헤더 파싱 → TCP 스택 진입은 Start()와 무관하게 항상 일어난다.** (프로그램이 안 떠 있어도 NIC 드라이버가 로드되어 있으면 프레임마다 실행됨)

Start()가 좌우하는 것은 마지막 **"소켓 테이블 조회" 단계의 결과**이다.

```
[항상 일어남]
NIC 프레임 수신 → DMA → 인터럽트 → ISR/DPC → 이더넷/IP 헤더 파싱 → TCP 스택 진입
  → 목적지 포트로 소켓 테이블 조회

[여기서 Start() 여부에 따라 갈림]
- Start() 했음 (포트 N에 LISTEN 소켓 등록됨)
    → LISTEN 소켓 발견 → SYN에 SYN+ACK 응답, 핸드셰이크 진행, accept 큐 적재
- Start() 안 했음 (포트 N에 등록된 소켓 없음)
    → 조회 실패 → SYN에 RST(Reset) 응답(연결 거부) 또는 폐기
```

즉 Start()는 이 과정을 "시작시키는 것"이 아니라, **소켓 테이블에 "포트 N = LISTEN" 항목을 등록해 조회 결과를 바꾸는 것**이다.

### 4.5 참고

- Windows 방화벽이 켜져 있으면 해당 포트의 인바운드 패킷을 먼저 차단할 수 있다. Start()를 해도 외부에서 접속이 안 되는 "로컬은 되는데 다른 PC에서 안 붙는" 문제의 흔한 원인이다.
- 지정한 IP가 특정 NIC의 주소면 그 주소로 오는 패킷만, `IPAddress.Any`면 모든 NIC 주소로 오는 패킷이 매칭된다.

---

## 5. Accept()는 누가 호출하는가

**우리 코드가 직접 호출한다.** `TcpListener`에는 알아서 Accept 해주는 내부 스레드나 콜백이 없다. `AcceptTcpClient()`, `AcceptSocket()`, `AcceptTcpClientAsync()`, `AcceptSocketAsync()` 중 하나를 누군가 호출해야만 accept가 일어난다.

### accept() 시스템 콜의 동작

```
accept() 시스템 콜
  → accept 큐에 항목 있음: 꺼내서 새 소켓 핸들 반환 (즉시)
  → 비어 있음: 호출 스레드를 대기 상태로 전환(블로킹),
               연결이 큐에 들어오면 커널이 그 스레드를 깨움
```

`AcceptTcpClientAsync()`는 Windows에서 IOCP에 요청을 등록해 두고 스레드를 놓아주며, 연결이 완료되면 커널이 완료 패킷을 IOCP 큐에 넣고 스레드 풀 스레드가 continuation을 실행한다.

### 역할 분담

| 동작 | 누가 |
|---|---|
| socket / bind / listen | `Start()` (우리가 호출) |
| 핸드셰이크 완료, accept 큐 적재 | 커널 (자동) |
| accept 큐에서 꺼내기 | 우리 코드가 `Accept...()`를 호출할 때 |

---

## 6. AcceptTcpClient()가 돌려주는 것 (client 객체의 실체)

`client`는 "상대 PC 정보를 정리해 담은 데이터 객체"가 아니라, **그 연결을 가리키는 소켓 핸들을 감싼 객체**이다. 상대 정보를 만든 주체도 NIC가 아니라 **커널의 TCP 스택**이다.

### 6.1 상대 정보는 어디서 오나

NIC는 프레임을 메모리에 넣고 인터럽트를 거는 것까지만 한다. 상대의 IP와 포트는 패킷 헤더에 들어 있는 값이다.

- IP 헤더: 출발지 IP, 목적지 IP
- TCP 헤더: 출발지 포트, 목적지 포트

커널의 TCP 스택이 SYN 패킷을 파싱하면서 이 값을 읽고, 연결 하나를 식별하는 **4-튜플 `(상대 IP, 상대 포트, 내 IP, 내 포트)`** 로 **TCB(Transmission Control Block, 연결 상태 구조체)** 를 만들어 커널 메모리에 보관한다. TCB에는 시퀀스 번호, 윈도우 크기, 송수신 버퍼 등도 함께 들어 있다.

### 6.2 accept()가 돌려주는 것

`accept()` 시스템 콜은 accept 큐에서 완료된 연결 하나를 꺼내고, **그 연결에 대응하는 새 소켓 핸들**을 반환한다. .NET은 이 핸들을 `Socket` 객체로 감싸고, 다시 `TcpClient`로 감싼다.

```
TcpClient
  └─ Client (Socket)
       └─ 소켓 핸들 → 커널의 해당 연결 TCB를 가리킴
```

`client` 객체가 직접 상대 IP 문자열을 필드로 들고 있는 게 아니라, **커널에 있는 연결을 가리키는 손잡이(핸들)** 이다. 상대 정보가 필요하면 그때 커널에 물어본다.

```csharp
var ep = (IPEndPoint)client.Client.RemoteEndPoint; // getpeername() 호출 결과
Console.WriteLine($"{ep.Address}:{ep.Port}");
```

### 6.3 리스너 소켓과 클라이언트 소켓은 별개

- **리스너 소켓**: 계속 LISTEN 상태로 남아 다음 연결을 받는다.
- **accept()로 얻은 소켓**: 해당 클라이언트 전용 통신 채널이다.

그래서 같은 포트 하나로 여러 클라이언트를 동시에 받을 수 있다. (각 연결은 4-튜플로 구분된다)

| 질문 | 답 |
|---|---|
| 상대 정보를 만든 주체 | 커널 TCP 스택 (패킷 헤더 파싱) |
| `client`의 실체 | 연결 전용 소켓 핸들을 감싼 래퍼 |
| 상대 IP/포트 확인 | `client.Client.RemoteEndPoint` (필요할 때 커널에 조회) |
| 데이터 송수신 | `client.GetStream()`의 Read/Write |

---

## 7. client.GetStream()의 실체

**GetStream()이 커널에서 새로 뭔가를 얻어 오는 것은 아니다.** 핸들(키)은 이미 `AcceptTcpClient()` 시점에 얻었고, `GetStream()`은 그 핸들을 쓰는 관리 코드(.NET) 래퍼를 만들 뿐이다.

```csharp
NetworkStream stream = client.GetStream();
```

내부 개념:

```csharp
// TcpClient.GetStream() 개념
if (_dataStream == null)
    _dataStream = new NetworkStream(Client, ownsSocket: true);
return _dataStream;
```

- **시스템 콜이 발생하지 않는다.** 순수 .NET 객체 생성이다.
- `NetworkStream`은 `client.Client`(Socket)를 필드로 들고 있을 뿐이다.
- 여러 번 호출해도 같은 `NetworkStream` 인스턴스를 반환한다.

```
TcpClient ─ Client(Socket) ─ 소켓 핸들 ─→ 커널의 연결 TCB
                  ↑
NetworkStream ────┘  (같은 Socket을 참조)
```

핸들은 하나이고, `TcpClient`, `Socket`, `NetworkStream`은 그것을 감싼 서로 다른 API(Application Programming Interface) 층이다. `client.Client.Receive(...)`를 직접 써도 결과는 같다.

### 7.1 Read (수신)

```csharp
int n = stream.Read(buffer, 0, buffer.Length);
```

`NetworkStream.Read()`는 내부에서 `Socket.Receive()`를 호출하고, Windows에서는 `recv` / `WSARecv` 시스템 콜이 된다.

- 패킷은 이미 커널이 받아서 이 연결의 **수신 버퍼(커널 메모리)** 에 쌓아 둔다. Read 호출과 무관하게 계속 일어나는 일이다.
- `Read()`는 그 버퍼에서 데이터를 **사용자 메모리(buffer)로 복사**한다.
- 수신 버퍼가 비어 있으면 호출 스레드가 블로킹되고, 데이터가 도착하면 커널이 깨운다.
- 반환값 `n`은 이번에 복사된 바이트 수이다. TCP는 **바이트 스트림**이라 상대가 보낸 메시지 단위와 무관하다. Modbus처럼 길이가 정해진 프레임은 필요한 바이트 수만큼 반복해서 읽어야 한다.
- 상대가 연결을 정상 종료(FIN, Finish)하면 `Read()`는 0을 반환한다.

### 7.2 Write (송신)

```csharp
stream.Write(buffer, 0, len);
```

- `Write()`는 `send` / `WSASend` 시스템 콜로 데이터를 **커널의 송신 버퍼로 복사**하고 보통 즉시 리턴한다.
- 실제 패킷 분할(MSS, Maximum Segment Size), 시퀀스 번호 부여, 재전송, NIC 전송은 커널 TCP 스택이 처리한다.
- 따라서 `Write()`가 리턴했다고 상대가 받았다는 뜻은 아니다. 송신 버퍼가 가득 찬 경우에만 블로킹된다.

### 7.3 단계별 커널 개입 정리

| 단계 | 커널 개입 | 하는 일 |
|---|---|---|
| `AcceptTcpClient()` | O (`accept`) | 연결 전용 소켓 핸들 획득 |
| `GetStream()` | X | 그 핸들을 쓰는 NetworkStream 래퍼 생성 |
| `stream.Read()` | O (`recv`) | 커널 수신 버퍼 → 사용자 버퍼 복사 |
| `stream.Write()` | O (`send`) | 사용자 버퍼 → 커널 송신 버퍼 복사 |

---

## 8. 전체 흐름: listener 생성부터 Read/Write까지 (시간 순서)

### Phase 0. 앱 실행 전부터 항상 돌고 있는 것

```
- NIC 드라이버가 로드되어 있음 (NDIS(Network Driver Interface Specification) 미니포트)
- NIC는 수신 링 버퍼(DMA 영역)를 미리 확보해 둠
- tcpip.sys(TCP/IP 스택)가 커널에 상주, 소켓 테이블은 비어 있음
```

이 시점에 SYN이 들어오면 소켓 테이블 조회가 실패해서 RST로 거부된다.

### Phase 1. 서버 앱: listener 생성과 Start()

```
[앱 스레드 A]
① new TcpListener(ip, port)
   → .NET 객체 생성, ip/port를 필드에 저장 (시스템 콜 없음)

② listener.Start()
   → Socket 객체 생성 → WSASocket()
       Winsock DLL(Dynamic-Link Library) → AFD.sys(소켓 드라이버) → tcpip.sys
       커널에 소켓 객체 생성, 핸들 반환 (LISTEN 전이므로 CLOSED 상태)
   → bind(ip, port)
       tcpip.sys가 (ip, port)를 로컬 주소 테이블에 등록
       (이미 사용 중이면 WSAEADDRINUSE → SocketException)
   → listen(backlog)
       소켓 상태 CLOSED → LISTEN
       accept 큐(완료 연결 대기)와 SYN 처리용 구조 준비
   → 시스템 콜 3개가 끝나고 즉시 리턴
```

이 시점부터 커널 소켓 테이블에 "포트 N = LISTEN"이 등록된다. 앱 쪽에서 돌아가는 코드는 없다.

### Phase 2. Accept 스레드 시작 및 블로킹

```
[Accept 스레드 B]
③ listener.AcceptTcpClient() 호출
   → accept() 시스템 콜 진입
   → accept 큐 확인 → 비어 있음
   → 커널이 스레드 B를 대기(Wait) 상태로 전환
      (스케줄러가 B를 CPU(Central Processing Unit)에서 내리고 대기 객체에 연결)
   → B는 여기서 멈춤, CPU를 쓰지 않음
```

### Phase 3. 상대 PC가 접속: TCP 3-way 핸드셰이크

```
[상대 PC] connect(서버IP, N)
   → 상대 커널이 SYN 패킷 생성, 자기 NIC로 송신

[서버 하드웨어/커널: 앱 스레드 개입 없음]
④ 서버 NIC가 프레임 수신
   → NIC가 DMA로 수신 링 버퍼(메모리)에 프레임 복사
   → 하드웨어 인터럽트(또는 MSI-X(Message Signaled Interrupts Extended)) 발생
   → CPU가 하던 일을 중단하고 NIC 드라이버 ISR 실행 (짧게)
   → ISR이 DPC를 예약, DPC에서 NDIS가 패킷을 tcpip.sys로 전달

⑤ tcpip.sys 수신 경로
   → 이더넷 헤더 확인(목적지 MAC(Media Access Control) 주소), IP 헤더 파싱(목적지 IP 매칭, 체크섬)
   → TCP 헤더 파싱 (목적지 포트 = N, SYN 플래그)
   → 소켓 테이블 조회 → LISTEN 소켓 발견
   → 커널이 half-open 항목 생성:
      (상대IP, 상대포트, 내IP, 내포트) + 초기 시퀀스 번호 등
   → SYN+ACK 패킷 생성 → NIC 송신 경로로 전달

[상대 PC] SYN+ACK 수신 → ACK 송신, connect() 성공 리턴

⑥ 서버 NIC가 ACK 수신 → ④~⑤와 같은 경로로 tcpip.sys 도달
   → half-open 항목을 ESTABLISHED로 전환
      TCB 완성: 시퀀스 번호, 윈도우, 송수신 버퍼 등
   → 완료된 연결을 LISTEN 소켓의 accept 큐에 추가
   → 대기 중인 스레드 B를 깨움(Wake)
```

이 시점에 상대 PC의 `connect()`는 이미 성공했다. 서버 앱은 아직 아무것도 모른다.

### Phase 4. Accept 스레드가 깨어나 연결을 받음

```
[Accept 스레드 B]
⑦ 스케줄러가 B를 다시 실행 → accept() 시스템 콜 재개
   → accept 큐에서 연결 1개 dequeue
   → 그 연결(TCB)에 대응하는 "새 소켓 객체/핸들" 생성
   → 핸들 반환

⑧ .NET: 핸들을 Socket으로 감싸고 TcpClient로 감싸 리턴
   var client = ... ← 이 시점에 client 획득

⑨ ThreadPool.QueueUserWorkItem(HandleClient, client)
   → 스레드 풀 작업 큐에 등록, B는 즉시 다음 AcceptTcpClient()로 복귀 (다시 블로킹)
```

### Phase 5. 스레드 풀 워커: GetStream()

```
[스레드 풀 워커 C]
⑩ HandleClient(client) 실행 시작
⑪ var stream = client.GetStream();
   → 시스템 콜 없음. new NetworkStream(client.Client) 만 수행
   → 같은 소켓 핸들을 참조하는 래퍼 생성
⑫ stream.Read(buffer, 0, len) 호출
   → 내부에서 Socket.Receive → recv/WSARecv 시스템 콜
   → 이 연결의 수신 버퍼 확인 → 비어 있음
   → 워커 C가 대기 상태로 전환 (블로킹)
```

### Phase 6. 상대 PC가 요청 데이터 전송 (Modbus 요청)

```
[상대 PC] 앱이 Write → 상대 커널이 TCP 세그먼트 생성 → 상대 NIC 송신

[서버 하드웨어/커널]
⑬ NIC 수신 → DMA → 인터럽트 → ISR → DPC → tcpip.sys (④와 동일 경로)

⑭ tcpip.sys TCP 처리
   → 4-튜플(상대IP, 상대포트, 내IP, 내포트)로 해당 연결의 TCB를 조회
   → 시퀀스 번호가 기대 값과 맞는지 검사, 체크섬 검증
   → 페이로드(Modbus 요청 바이트)를 이 연결의 수신 버퍼에 append
   → ACK 송신 예약 (즉시 또는 지연 ACK)
   → 수신 버퍼에 데이터가 생겼으므로 대기 중인 워커 C를 깨움
```

### Phase 7. Read가 데이터를 가져감

```
[워커 C]
⑮ recv 시스템 콜 재개
   → 커널 수신 버퍼 → 사용자 메모리(buffer)로 복사
   → 복사한 바이트 수 n 리턴 (요청 크기와 같다는 보장 없음, 일부만 올 수 있음)
   → 복사된 만큼 수신 윈도우가 다시 열림

⑯ 앱 로직: Modbus 프레임 파싱
   → MBAP(Modbus Application Protocol) 헤더의 길이 필드를 보고 필요한 바이트가 다 왔는지 확인
   → 부족하면 Read를 반복
   → 함수 코드 해석, 레지스터 값 읽기/쓰기 등 응답 프레임 생성
```

### Phase 8. Write로 응답 송신

```
[워커 C]
⑰ stream.Write(response, 0, len)
   → send/WSASend 시스템 콜
   → 사용자 버퍼 → 커널 송신 버퍼로 복사
   → 보통 여기서 즉시 리턴 (송신 버퍼가 가득 찬 경우에만 블로킹)

[tcpip.sys, 앱과 비동기로 진행]
⑱ 송신 버퍼 데이터를 MSS 단위로 세그먼트화
   → TCP 헤더(시퀀스/ACK 번호, 윈도우) + IP 헤더 붙임, 체크섬 계산
   → NDIS → NIC 드라이버 → 송신 디스크립터 링에 등록
   → NIC가 DMA로 메모리에서 프레임을 읽어 회선으로 송출
   → 송신 완료 인터럽트로 버퍼 해제

⑲ 상대 NIC 수신 → 상대 커널이 수신 버퍼에 적재 → 상대 앱 Read 리턴
   상대 커널이 ACK를 보내면 서버 tcpip.sys가 송신 버퍼의 해당 데이터를 해제
   (ACK가 안 오면 커널이 자동 재전송)
```

`Write()`가 리턴한 시점은 상대가 받은 시점이 아니라 **커널 송신 버퍼에 복사된 시점**이다.

### Phase 9. 이후 반복과 종료

```
⑳ 워커 C는 ⑫~⑰ 반복 (Read 블로킹 → 요청 처리 → Write)

종료:
- 상대가 close → FIN 수신 → 커널이 소켓을 CLOSE_WAIT로 전환
  → 대기 중인 Read가 0을 리턴 → 앱이 client.Close()로 소켓 닫기
  → 커널이 FIN 송신 → LAST_ACK → CLOSED, 핸들/TCB 정리

- 서버 종료 시 listener.Stop()
  → LISTEN 소켓 close, 포트 해제
  → 블로킹 중이던 AcceptTcpClient()가 SocketException으로 깨어남
```

### 실행 주체 요약

| 구간 | 실행 주체 |
|---|---|
| socket/bind/listen | 앱 스레드 A (시스템 콜 3번 후 리턴) |
| accept 대기 | 스레드 B (커널 대기 상태, CPU 미사용) |
| 프레임 수신, DMA, 인터럽트, 핸드셰이크, 수신 버퍼 적재, ACK, 재전송 | NIC 하드웨어 + 커널 (앱 스레드 개입 없음) |
| accept 큐에서 꺼내기 | 스레드 B가 깨어나 수행 |
| GetStream | 워커 C, 시스템 콜 없음 |
| Read (버퍼 → 앱 복사), Write (앱 → 버퍼 복사) | 워커 C |
| Write 이후 실제 패킷 송출 | 커널 + NIC (앱과 비동기) |

**핵심**: 패킷 수신·핸드셰이크·버퍼링·재전송은 전부 커널과 NIC가 앱과 무관하게 처리하고, 앱 스레드는 `accept`, `recv`, `send`로 커널 버퍼와 앱 메모리 사이의 **데이터 복사와 대기**만 한다.

---

## 9. Modbus TCP Slave 코드 분석

### 9.1 대상 코드

```csharp
accept = new Thread(AcceptLoop)
{
    IsBackground = true,
    Name = "ModbusTcpSlave.Accept"
};

private void AcceptLoop()
{
    while (running)
    {
        try
        {
            var client = listener.AcceptTcpClient();
            ThreadPool.QueueUserWorkItem(HandleClient, client);
        }
        catch (SocketException)
        {
            if (!running)
                break;
        }
        catch (ObjectDisposedException)
        {
            break;
        }
        catch (Exception e)
        {
            Log?.Invoke($"Accept error: {e.Message}");
        }
    }
}
```

### 9.2 AcceptTcpClient()는 스레드를 만들지 않는다

내부적으로 `accept()` 시스템 콜 하나를 호출할 뿐이다.

- **큐에 완료된 연결이 있으면**: 하나 꺼내서 새 소켓을 만들어 `TcpClient`로 감싸 즉시 리턴
- **큐가 비어 있으면**: 호출한 스레드가 대기 상태로 전환(블로킹), 연결이 큐에 들어오면 깨어나 하나 꺼내 리턴

한 번 호출에 연결 **하나만** 꺼내므로 `while` 루프로 반복 호출해야 한다.

### 9.3 왜 별도 스레드에서 호출하나

큐가 비어 있으면 **호출한 스레드가 블로킹**되기 때문이다. 연결이 언제 올지 모르므로 메인/UI 스레드에서 호출하면 프로그램이 멈춘다. 그래서 "accept 대기만 전담하는 스레드"를 따로 만든다.

- `IsBackground = true`: 프로세스 종료 시 이 스레드가 블로킹 중이어도 종료를 막지 않도록 하는 설정.

### 9.4 ThreadPool.QueueUserWorkItem(HandleClient, client)

.NET 스레드 풀의 작업 큐에 `HandleClient(client)` 호출을 등록한다. 풀의 놀고 있는 워커 스레드가 꺼내 실행하며, 워커가 없으면 풀이 필요에 따라 새로 만들기도 한다.

```
Accept 스레드: AcceptTcpClient() → client 획득 → 풀에 작업 등록 → 즉시 다음 Accept로 복귀
스레드 풀 워커: HandleClient(client) 실행 (Modbus 요청 읽기/응답 쓰기)
```

**분리하는 이유**: Accept 스레드가 클라이언트 처리 때문에 막히지 않게 하기 위해서다. `HandleClient`를 Accept 스레드에서 직접 호출하면 통신하는 동안 다른 클라이언트의 accept가 멈춘다. `HandleClient` 안에서는 보통 `stream.Read()`로 블로킹되므로 워커 스레드가 클라이언트 하나당 오래 점유될 수 있다.

### 9.5 예외 처리의 의미

- `Stop()`에서 `listener.Stop()`을 하면 블로킹 중이던 `AcceptTcpClient()`가 `SocketException`으로 깨어난다. `running`이 false면 정상 종료로 보고 루프를 빠져나간다.
- `ObjectDisposedException`도 같은 종료 경로이다.
- 그 외 예외는 로그만 남기고 루프를 계속 돈다. 같은 예외가 즉시 반복되면 CPU를 태우는 루프가 될 수 있으므로 필요하면 짧은 `Thread.Sleep`을 넣는다.

---

## 10. 클라이언트가 하나뿐이면 스레드 풀로 넘길 필요가 없는가

클라이언트가 딱 하나만 접속한다고 **보장**된다면 필요는 없다. 하지만 "필요 없다"와 "안 하는 게 낫다"는 별개이다.

### 10.1 Accept 스레드에서 직접 처리하는 경우

```csharp
private void AcceptLoop()
{
    while (running)
    {
        try
        {
            var client = listener.AcceptTcpClient();
            HandleClient(client);   // 통신이 끝날 때까지 여기서 블로킹
        }
        catch ...
    }
}
```

`HandleClient`가 리턴할 때까지(연결이 끊길 때까지) 다음 `AcceptTcpClient()`로 돌아가지 못한다. 스레드가 1개로 끝나 구조는 단순하다.

### 10.2 동작 차이

첫 번째 클라이언트가 붙어 있는 동안 두 번째 클라이언트가 접속하면:

- 커널은 핸드셰이크를 완료해 accept 큐에 쌓아 둔다. 두 번째 클라이언트는 `connect()`가 성공한 것처럼 보인다.
- 그러나 서버가 accept를 호출하지 않으므로 요청을 읽지도 응답하지도 않는다. 두 번째 클라이언트는 타임아웃을 겪는다.
- 첫 번째 연결이 끊겨야 두 번째가 처리된다.

### 10.3 판단 기준

Modbus TCP 슬레이브에서 흔한 상황:

- 마스터(PLC(Programmable Logic Controller)/상위 시스템)가 재시작하거나 네트워크가 끊겼다 복구되면 **이전 연결이 서버 쪽에서는 아직 살아있는 상태(half-open)** 에서 새 연결이 들어올 수 있다. 직접 처리 방식이면 이전 연결의 `Read`가 끊김을 감지할 때까지 새 연결이 막힌다.
- Modbus Poll 같은 진단 도구를 잠깐 붙여 보는 경우가 있다.

따라서 "정말 하나만"이 확실하고 단순함이 중요하면 직접 처리도 가능하지만, 현재 코드처럼 풀로 넘기는 구조가 재접속 상황에서 더 안전하다. 비용 차이도 거의 없다(연결 수가 적으면 풀 워커 1~2개 사용 수준).

"하나만 허용"을 의도적으로 강제하고 싶다면, 풀로 넘기되 **새 연결이 들어왔을 때 기존 클라이언트를 닫고 교체**하는 방식이 재접속 문제를 피하면서 단일 연결 정책도 지킨다.

---

## 부록 A. 약어 / 용어 풀네임

| 약어 | 풀네임 | 설명 |
|---|---|---|
| NIC | Network Interface Card | 랜카드. 프레임을 송수신하는 하드웨어 |
| DMA | Direct Memory Access | CPU 개입 없이 장치가 메모리에 직접 읽고 쓰는 방식 |
| ISR | Interrupt Service Routine | 인터럽트 발생 시 가장 먼저 실행되는 짧은 처리 루틴 |
| DPC | Deferred Procedure Call | ISR에서 미룬 나머지 처리를 커널이 나중에 실행하는 Windows 메커니즘 |
| NDIS | Network Driver Interface Specification | Windows의 네트워크 드라이버 표준 인터페이스 |
| AFD | Ancillary Function Driver | Windows 소켓 API 요청을 tcpip.sys로 연결하는 커널 드라이버 (AFD.sys) |
| tcpip.sys | TCP/IP 스택 드라이버 | Windows 커널의 TCP/IP 프로토콜 구현체 |
| Winsock | Windows Sockets | Windows의 소켓 API 라이브러리 |
| DLL | Dynamic-Link Library | 동적 링크 라이브러리 |
| IOCP | I/O Completion Port | 비동기 I/O 완료 통지를 처리하는 Windows 메커니즘 |
| TCP | Transmission Control Protocol | 연결 지향, 신뢰성 있는 바이트 스트림 프로토콜 |
| IP | Internet Protocol | 패킷 주소 지정 및 전달 프로토콜 |
| TCB | Transmission Control Block | 커널이 TCP 연결 하나마다 유지하는 상태 구조체 |
| SYN | Synchronize | 연결 요청 플래그 (3-way 핸드셰이크 1단계) |
| ACK | Acknowledgment | 수신 확인 플래그 |
| FIN | Finish | 연결 종료 플래그 |
| RST | Reset | 연결 강제 종료/거부 플래그 |
| MSS | Maximum Segment Size | TCP 세그먼트 하나에 담을 수 있는 최대 데이터 크기 |
| MAC | Media Access Control | 이더넷 하드웨어 주소 |
| MSI-X | Message Signaled Interrupts Extended | 인터럽트를 메모리 쓰기 메시지로 전달하는 방식 (다중 큐 지원) |
| MBAP | Modbus Application Protocol (header) | Modbus TCP 프레임 앞부분의 헤더 (Transaction ID, Protocol ID, Length, Unit ID) |
| PLC | Programmable Logic Controller | 산업용 제어기 |
| CPU | Central Processing Unit | 중앙 처리 장치 |
| OS | Operating System | 운영체제 |
| UI | User Interface | 사용자 인터페이스 |
| API | Application Programming Interface | 응용 프로그램 인터페이스 |
| 4-튜플 | (상대 IP, 상대 포트, 내 IP, 내 포트) | TCP 연결 하나를 유일하게 식별하는 4개 값의 조합 |
| backlog | 대기 큐 크기 | `Listen(backlog)`에서 커널이 보관해 주는 완료 연결의 최대 개수 |
| half-open | 반쯤 열린 연결 | 서버가 SYN+ACK를 보내고 최종 ACK를 기다리는 상태, 또는 한쪽만 연결이 끊긴 것을 모르는 상태 |
