# TcpListener / volatile / JIT / CPU 메모리 계층 정리

## 목차
1. TcpListener
2. volatile
3. C# volatile vs Java volatile
4. JIT와 호이스팅
5. 레지스터 캐싱 vs CPU 캐시
6. CPU 구조와 메모리 계층
7. 최종 결론
8. 헷갈렸던 포인트 정정 모음

---

## 1. TcpListener

### 개념
- `System.Net.Sockets`의 클래스. **TCP 서버 측에서 클라이언트 연결 요청을 기다리는 클래스**.
- 내부적으로 `Socket`을 감싼 고수준 래퍼.
- `TcpListener`: 연결을 **받아들이는** 쪽 (대기 소켓)
- `TcpClient`: 실제로 **데이터를 주고받는** 연결 (accept 결과로 생성)

### 동작 원리
```
socket() → bind(IP:Port) → listen(backlog) → accept() → send/recv → close
```

| .NET 메서드 | 소켓 단계 |
|---|---|
| `new TcpListener(IPAddress.Any, 5000)` | 주소/포트 지정 |
| `Start()` | socket + bind + listen |
| `AcceptTcpClient()` | accept (연결이 올 때까지 **블로킹**) |
| `Stop()` | 대기 소켓 close |

- **대기 소켓과 통신 소켓은 분리**된다. accept될 때마다 클라이언트별 새 소켓 생성.
- 여러 클라이언트를 동시에 처리하려면 각 연결을 별도 스레드/Task로 넘겨야 함.

### 역사
- **1983**: BSD Unix 4.2 Berkeley Sockets API (`socket/bind/listen/accept` 모델의 원형)
- **1990년대 초**: Windows로 이식된 Winsock
- **2002**: .NET Framework 1.0에서 `System.Net.Sockets` 도입
- **.NET 1.x~4.0**: `BeginAcceptTcpClient/EndAcceptTcpClient` (APM)
- **.NET 4.5**: `AcceptTcpClientAsync()` (async/await)
- **.NET 6**: `AcceptTcpClientAsync(CancellationToken)` 오버로드

### 예시 코드 (volatile 플래그 방식)
```csharp
public class TcpServer
{
    private TcpListener listener;
    private volatile bool running;
    private Thread acceptThread;

    public void Start(int port)
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        running = true;                 // 스레드 시작 전에 설정

        acceptThread = new Thread(AcceptLoop) { IsBackground = true };
        acceptThread.Start();
    }

    private void AcceptLoop()
    {
        while (running)                 // volatile: 매번 최신 값을 읽음
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient(); // 블로킹
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
            catch (SocketException) when (!running) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            byte[] buffer = new byte[1024];
            int read;
            while (running && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
                stream.Write(buffer, 0, read);   // 에코
        }
    }

    public void Stop()
    {
        running = false;        // 1) 루프 종료 신호
        listener.Stop();        // 2) 블로킹 중인 Accept를 예외로 깨움
        acceptThread.Join();
    }
}
```

**Stop()에서 두 단계가 모두 필요한 이유**
1. `running = false`만 하면 `AcceptTcpClient()`가 블로킹 중이라 루프 조건을 검사할 기회가 없음
2. `listener.Stop()`만 하면 예외 발생 시 종료 의도인지 오류인지 구분 불가

`listener` 필드에는 volatile이 필요 없음 → `Thread.Start()`가 메모리 배리어 역할을 하므로.

### 더 나은 대안: CancellationToken
```csharp
private CancellationTokenSource cts;

public async Task StartAsync(int port)
{
    cts = new CancellationTokenSource();
    listener = new TcpListener(IPAddress.Any, port);
    listener.Start();

    while (!cts.IsCancellationRequested)
    {
        TcpClient client = await listener.AcceptTcpClientAsync(cts.Token); // .NET 6+
        _ = Task.Run(() => HandleClient(client));
    }
}

public void Stop() { cts.Cancel(); listener.Stop(); }
```
- 비동기 대기를 즉시 깨울 수 있어 `volatile bool` + `Stop()` 조합보다 깔끔.
- 단, 레거시 WinForms 코드나 단순 플래그 용도에서는 `volatile bool`도 여전히 올바른 선택.

---

## 2. volatile

### 개념
- 필드에 붙이는 키워드. **"다른 스레드가 언제든 바꿀 수 있으니, 컴파일러/JIT/CPU는 캐싱·재배치 같은 최적화를 함부로 하지 말라"** 는 표시.

### 왜 필요한가
```csharp
while (running) { ... }
```
- `volatile`이 없으면 JIT가 "루프 안에서 `running`을 바꾸는 코드가 없다"고 판단 → 값을 **한 번만 읽고 재사용** → 다른 스레드가 `running = false`로 바꿔도 루프가 끝나지 않음.

### 보장하는 것
- **가시성**: 다른 스레드가 쓴 최신 값을 읽음
- **acquire/release 시맨틱**
  - volatile 읽기(acquire): 이후 메모리 접근이 이 읽기보다 앞으로 재배치되지 않음
  - volatile 쓰기(release): 이전 메모리 접근이 이 쓰기보다 뒤로 재배치되지 않음

### 보장하지 않는 것
- `count++` 같은 복합 연산의 **원자성** → `Interlocked` 또는 `lock` 사용
- 상호 배제

```csharp
private volatile int counter;
counter++;   // 읽기 → 증가 → 쓰기, 사이에 다른 스레드가 끼어들 수 있음 (원자적 아님)
```

### 역사
- **C# 1.0 (2002)**부터 존재. ECMA-335(CLI) 메모리 모델 기반.
- .NET 2.0부터 MS 구현이 쓰기에 release 시맨틱을 부여 → 사실상 더 강한 모델.
- x86은 메모리 순서가 강해 volatile 누락 버그가 잘 드러나지 않았으나, **ARM(약한 메모리 모델)** 보급으로 실제 버그가 나타남.
- **.NET 4.5**: `Volatile.Read()` / `Volatile.Write()` 추가 (필드 단위가 아니라 접근 단위 제어).

---

## 3. C# volatile vs Java volatile

**목적은 같지만 보장 강도가 다르다.**

| 항목 | Java `volatile` | C# `volatile` |
|---|---|---|
| 가시성 보장 | O | O |
| 재배치 규칙 | 순차적 일관성 (volatile 접근끼리 전체 순서 존재) | acquire/release만 보장 (더 약함) |
| 메모리 모델 | JMM (JSR-133, Java 5) | ECMA-335 + MS 구현 |
| `long`/`double` | 가능 (원자성도 보장) | **불가** (CS0677) |
| 대안 API | `AtomicXxx`, `VarHandle` | `Volatile.Read/Write`, `Interlocked` |

### 핵심 차이: StoreLoad 재배치
```csharp
// 스레드 1              // 스레드 2
x = 1;  // volatile     y = 1;  // volatile
r1 = y; // volatile     r2 = x; // volatile
```
- **Java**: volatile 쓰기 뒤에 StoreLoad 배리어 → `r1 == 0 && r2 == 0` **불가능**
- **C#**: 쓰기(release) 뒤의 읽기(acquire)가 앞으로 당겨질 수 있어 `r1 == 0 && r2 == 0` **가능**
- Dekker/Peterson 알고리즘을 Java 감각으로 C# volatile에 옮기면 깨질 수 있음.
- `running` 같은 **종료 플래그 용도**는 양쪽 모두 동일하게 안전.

### x86에서의 실제 비용
- C# volatile 읽기/쓰기: x86에서는 일반 `mov`와 거의 동일 (JIT 최적화 억제 정도)
- Java volatile 쓰기: `lock addl` 같은 StoreLoad 배리어가 붙어 조금 더 비쌈
- ARM: 양쪽 모두 `ldar`/`stlr` 또는 `dmb` 생성

---

## 4. JIT와 호이스팅

### JIT (Just-In-Time 컴파일러)
- **실행 도중, 필요한 시점에 IL(중간 언어)을 CPU가 실행할 수 있는 기계어로 번역하는 컴파일러**

```
C# 소스 ──(Roslyn, 빌드 시점)──▶ IL (.dll/.exe)
IL ──(JIT, 실행 시점)──▶ 기계어 (x86, x64, ARM ...)
```
- Java의 `javac → 바이트코드 → JVM JIT(HotSpot)`와 같은 구조.
- **메서드 단위**로, **처음 호출될 때** 컴파일하고 결과를 캐시.

### Tiered Compilation (.NET Core 3.0+ 기본)
| 단계 | 특징 |
|---|---|
| **Tier-0** | 최적화 거의 없이 빠르게 컴파일 → 시작 빠름 |
| **Tier-1** | 자주 호출되는(hot) 메서드만 재컴파일 → 강하게 최적화 |

- 호이스팅 같은 최적화는 주로 **Tier-1**에서 적용 → 프로그램이 좀 돌고 난 뒤에야 버그가 나타나기도 함.

### JIT의 역사
- 1960년대: LISP 등에서 동적 컴파일 아이디어
- 1980~90년대: Smalltalk 계열에서 실용화
- 1990년대 후반: Java HotSpot(1999년경)이 대중화
- 2002: .NET Framework 1.0 CLR에 탑재
- .NET Core 3.0 (2019): Tiered Compilation 기본
- .NET 8 (2023): Dynamic PGO 기본

### JIT vs AOT
| 항목 | JIT | AOT |
|---|---|---|
| 컴파일 시점 | 실행 중 | 빌드 시 |
| 시작 속도 | 상대적으로 느림 | 빠름 |
| 최종 성능 | 런타임 정보로 더 최적화 가능 | 고정된 코드 |
| 이식성 | 하나의 IL로 여러 플랫폼 | 플랫폼별 빌드 |
| .NET 도구 | 기본 CLR | ReadyToRun, NativeAOT |

### 실무 팁 (머신 비전 관점)
- 첫 호출이 느린 이유 = JIT 컴파일 시간. 검사 사이클 타임이 중요하면 시작 직후 **워밍업**(첫 검사를 미리 1회 실행).
- `PublishReadyToRun`으로 시작 시간 단축 가능.
- Debug는 JIT 최적화 OFF, Release는 ON → volatile 누락 버그가 **Release에서만** 나타나는 이유.

### 호이스팅 (Loop-Invariant Code Motion)
JIT는 **"이 루프 안에서 이 값이 변하지 않는다"고 증명할 수 있을 때만** 읽기를 루프 밖으로 뺀다.

JIT의 추론 (단일 스레드 기준):
1. 루프 안에 그 변수에 쓰는 코드가 없다.
2. 루프 안에 알 수 없는 메서드 호출이 없다.
3. 동기화 수단(lock, volatile, Interlocked)이 없다.
4. → 다른 스레드가 바꿀 가능성은 고려하지 않는다.

```csharp
// 원래 코드
while (running) { count++; }

// JIT 변환 결과 (개념적)
bool tmp = running;      // 딱 한 번 읽음
while (tmp) { count++; }
```
- **`true`를 박는 게 아니라 "처음 읽은 값"을 쓰는 것.**
- 처음 읽은 값이 `true`면 결과적으로 `while (true)`, `false`면 루프를 안 돎.

### 허용되는 이유
- C# 메모리 모델은 "단일 스레드에서 관찰 가능한 동작이 같다면 최적화 가능"이라고 규정.
- 다른 스레드의 변경은 volatile/lock/Interlocked로 **명시해야** 고려됨. 명시 없으면 데이터 레이스로 보고 자유롭게 최적화.

### 이 버그가 악명 높은 이유
| 상황 | 동작 |
|---|---|
| Debug 빌드 | 최적화 꺼짐 → 정상 |
| Release + 빈 루프/짧은 루프 | 최적화 적용 → **무한 루프** |
| 루프 안에 알 수 없는 호출(`Console.WriteLine` 등) | 정상 (우연히) |
| 코드 수정/인라이닝 변화 | 갑자기 깨질 수 있음 |

### 재현 코드
```csharp
class Program
{
    static bool running = true;          // volatile 없음

    static void Main()
    {
        var t = new Thread(() =>
        {
            int count = 0;
            while (running) { count++; }
            Console.WriteLine("종료됨");
        });
        t.Start();

        Thread.Sleep(1000);
        running = false;
        t.Join();                         // Release 빌드에서 여기서 멈출 수 있음
    }
}
```
- Debug: 정상 종료 / Release: `t.Join()`에서 멈출 수 있음 / `static volatile bool`로 바꾸면 둘 다 정상.
- JIT 버전, CPU 아키텍처에 따라 다를 수 있어 "항상 재현"은 아니지만, **명세상 허용되는 최적화**이므로 volatile 없이는 안전하지 않음.

---

## 5. 레지스터 캐싱 vs CPU 캐시

### 레지스터 캐싱이란
- 메모리에서 읽은 값을 **CPU 레지스터에 담아 두고 재사용**하는 것 (읽기 명령을 다시 만들지 않음).

**캐싱 안 한 경우**
```asm
loop:
    mov  eax, [running]    ; 메모리에서 읽기 (매 반복)
    test eax, eax
    jz   end
    inc  ecx
    jmp  loop
```

**레지스터 캐싱한 경우**
```asm
    mov  eax, [running]    ; 루프 밖에서 딱 한 번
loop:
    test eax, eax          ; 레지스터만 검사 (메모리 접근 없음)
    jz   end
    inc  ecx
    jmp  loop
```
- eax의 값은 처음 읽은 시점의 **복사본**이라, 메모리가 바뀌어도 알 수 없음.

### 구분

| 구분 | 누가 관리 | volatile과의 관계 |
|---|---|---|
| **레지스터 캐싱** | 컴파일러/JIT (소프트웨어 결정) | volatile이 **막음** |
| **CPU 캐시 (L1~L3)** | 하드웨어 (일관성 프로토콜, MESI 등) | 무관, 그대로 사용 |

### 호이스팅과 레지스터 캐싱의 관계
호이스팅과 레지스터 캐싱은 별개가 아니라 **같은 현상의 두 측면**이다.

```
volatile 없음 + 루프 안에서 변경 없다고 판단
        ↓
읽기를 루프 밖으로 호이스팅 (딱 1번 읽음)
        ↓
읽은 값이 레지스터에 담김
        ↓
루프 안에서는 그 레지스터만 검사 (= 레지스터 캐싱)
        ↓
다른 스레드가 바꿔도 모름 → 무한 루프
```

| | volatile 없음 (변경 없다고 판단) | volatile 있음 |
|---|---|---|
| 호이스팅 | **함** | 안 함 |
| 레지스터 캐싱(재사용) | **함** | 안 함 |
| 루프 안 메모리 읽기 명령 | 없음 | 매 반복 |
| 다른 스레드의 변경 | 못 봄 | 봄 |

### "매번 RAM에서 읽는다"는 오해
- volatile을 붙여도 **RAM까지 가는 경우는 거의 없음.** 읽기 명령이 매번 생성되고, 보통 **L1 캐시에서 처리**됨.
- CPU 캐시는 **OS가 아니라 CPU 하드웨어**가 관리하며, 다른 코어가 값을 바꾸면 캐시 라인이 무효화되어 다음 읽기에서 새 값을 가져옴.

| | 호이스팅 없음 (volatile) | 호이스팅됨 (volatile 없음) |
|---|---|---|
| 루프 안 읽기 명령 | **매 반복 실행** | **없음** (루프 밖에서 1번) |
| 값이 오는 곳 | 메모리 (보통 L1 히트) | **레지스터** (복사본) |
| 결정 주체 | JIT가 명령 생성 | JIT가 명령 생략 |
| 다른 스레드의 변경 | 하드웨어가 반영 → 보임 | 메모리를 안 보므로 → 못 봄 |

> **"재사용"을 결정하는 주체는 OS도 CPU 캐시도 아닌 JIT다.**
> - JIT가 읽기 명령을 매번 만들면 → 이후는 하드웨어(캐시 일관성)가 처리
> - JIT가 읽기 명령을 한 번만 만들면 → 하드웨어가 도울 방법이 없음

### volatile이 실제로 막는 것

| 문제 | 발생 위치 | volatile의 역할 |
|---|---|---|
| 레지스터 캐싱(재사용) | 컴파일러/JIT | 매번 메모리 접근 명령을 생성하게 강제 |
| 명령어 재배치 | 컴파일러/JIT/CPU | acquire/release 배리어로 순서 제한 |
| 스토어 버퍼 지연 | CPU | 필요한 곳에 배리어 삽입 |

---

## 6. CPU 구조와 메모리 계층

```
┌──────────────────────── CPU 패키지 ────────────────────────┐
│  ┌─────────── 코어 0 ───────────┐  ┌────── 코어 1 ──────┐   │
│  │  제어 유닛 (명령어 해석/스케줄) │  │        ...         │   │
│  │  ALU / FPU (연산 장치)        │  │                    │   │
│  │  레지스터                     │  │                    │   │
│  │  L1 캐시 (명령어용 + 데이터용) │  │  L1                │   │
│  │  L2 캐시                      │  │  L2                │   │
│  └───────────────────────────────┘  └────────────────────┘   │
│                    L3 캐시 (모든 코어가 공유)                  │
│                    메모리 컨트롤러                             │
└─────────────────────────────────────────────────────────────┘
                              │
                        RAM (CPU 밖, 메인보드)
```

| 구성 요소 | 역할 |
|---|---|
| 제어 유닛 | 명령어를 가져와 해석, 실행 장치에 지시 |
| ALU | 정수 연산, 비교, 논리 연산 |
| FPU | 부동소수점 연산 |
| 레지스터 | 연산에 바로 쓰이는 초고속 저장 공간 |
| L1/L2/L3 캐시 | RAM보다 빠른 중간 저장소 |

### 저장 계층
```
레지스터 → L1 → L2 → L3 → RAM → SSD/HDD
(빠름/작음)                        (느림/큼)
```

| 계층 | 속도 | 크기 | 범위 |
|---|---|---|---|
| 레지스터 | 1사이클 이내 | 수십 개, 각 8바이트 정도 | 코어 전용 |
| L1 | 약 4사이클 | 수십 KB | 코어 전용 |
| L2 | 빠름 | 수백 KB ~ | 코어 전용(일부 아키텍처는 클러스터 공유) |
| L3 | 빠름 | 수십 MB | **모든 코어 공유** |
| RAM | 수백 사이클 | GB 단위 | 시스템 전체 |

### 캐시 일관성과 레지스터
- 코어마다 전용 L1/L2가 있어서 **캐시 일관성 프로토콜(MESI 등)** 이 필요. 코어 0이 `running = false`를 쓰면 코어 1의 해당 캐시 라인이 무효화되어 다음 읽기 때 최신 값을 가져옴.
- **레지스터는 이 프로토콜의 대상이 아님.** 레지스터에 복사된 값은 메모리와 연결이 끊긴 사본이라 하드웨어가 갱신해 줄 수 없음. → JIT가 레지스터에 붙들어 두는 것이 문제.

---

## 7. 최종 결론

> JIT가 while 조건 변수를 루프 안에서 바뀌지 않는다고 판단하면, 그 읽기를 루프 밖으로 **호이스팅**하여 한 번만 읽고 **레지스터의 값을 계속 재사용**한다. 그 결과 다른 스레드가 값을 바꿔도 보지 못해 사실상 `while (true)`가 될 수 있다.
>
> `volatile`은 이 호이스팅/레지스터 재사용을 금지하여 **접근할 때마다 실제 메모리 읽기/쓰기 명령이 생성**되게 한다. 그 뒤의 최신 값 전달은 하드웨어(캐시 일관성)가 맡는다.
> 추가로 acquire/release 순서 제한도 보장한다.

- `volatile`의 전형적 사용처: **한 스레드가 쓰고 다른 스레드가 읽는 단순 플래그** (`running`, `stopRequested` 등)
- 호이스팅 방지 ≠ 스레드 안전. 복합 연산(`++`, check-then-act)은 `Interlocked` / `lock` 필요.
- 신규 코드는 `CancellationToken` 권장.

---

## 8. 헷갈렸던 포인트 정정 모음

| 잘못된 이해 | 올바른 이해 |
|---|---|
| volatile은 Java와 완전히 같다 | 가시성은 같지만 재배치 규칙이 다름 (Java: 순차적 일관성, C#: acquire/release). `long`/`double`도 C#은 불가 |
| volatile을 붙이면 CPU 캐시를 우회해 매번 RAM에서 읽는다 | CPU 캐시는 그대로 사용. 하드웨어가 일관성 유지. 막는 것은 JIT의 레지스터 재사용 |
| 컴파일러가 조건 변수를 `true`로 치환한다 | 상수 치환이 아니라 **읽기를 1회로 줄이는 것** (호이스팅). 처음 읽은 값이 true면 결과적으로 무한 루프 |
| 호이스팅되면 레지스터 캐싱은 안 한다 | 호이스팅 = 레지스터 캐싱. 같은 현상의 두 측면 |
| 호이스팅되면 OS가 램에서 꺼내 CPU 캐시에 담는다 | OS는 관여 안 함. 호이스팅되면 CPU 캐시도 안 보고 **레지스터**만 사용. 결정 주체는 JIT |
| volatile은 레지스터에 넣지 못하게 막는다 | 읽은 값은 어차피 레지스터에 들어옴. 막는 것은 **다음 반복에서 재사용하는 것** |
| 루프에 변수가 있으면 항상 호이스팅된다 | 루프 안에 알 수 없는 메서드 호출/동기화 수단이 있으면 JIT가 보수적으로 가정해 호이스팅 못 함 (그래서 "가끔만" 버그) |
| volatile이면 스레드 안전하다 | 가시성과 제한적 순서만 보장. `counter++` 같은 복합 연산은 원자적이지 않음 |
