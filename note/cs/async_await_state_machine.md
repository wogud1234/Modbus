# Task / async-await 동작 원리 정리

원본 코드:

```csharp
Task taskLoad   = Task.CompletedTask;
taskLoad   = Task.Run(() => objTestLoad.DryRun_Loading(cts.Token, true));
```

---

## 1. `Task.CompletedTask`는 뭐가 생성돼?

- 이미 완료된(`RanToCompletion`) 상태의 **캐싱된 싱글턴 `Task` 인스턴스**를 반환.
  - 호출할 때마다 새로 만드는 게 아니라 .NET 내부에 미리 만들어둔 완료 상태 객체를 재사용 (`static readonly Task s_completedTask = ...` 형태로 구현됨).
- 스레드풀 스케줄링이 전혀 일어나지 않으며, `Status`는 곧바로 `TaskStatus.RanToCompletion`.
- 실무에서는 **placeholder(더미) 용도**로 자주 사용됨.
  - `Task` 지역변수를 `null`로 두면 나중에 `await taskLoad` 시 `NullReferenceException` 위험이 있음.
  - 조건문에 따라 실제 Task를 할당하거나, 그렇지 않으면 `CompletedTask`로 "아무 일도 안 한 것"처럼 채워두는 안전장치로 씀.
- 예제 코드에서는 바로 다음 줄에서 `Task.Run(...)`으로 재할당되므로, `Task.CompletedTask` 대입은 초기값 세팅용이고 실제로는 버려지는 값.

---

## 2. `Task.Run(...)`은 호출 스레드를 그대로 두고 스레드풀 스레드에서 실행하는가?

**맞음.** 동작 순서:

1. `Task.Run`을 호출한 **현재(호출) 스레드는 즉시 리턴**받고 다음 코드로 진행 — 블로킹되지 않음.
2. 넘긴 델리게이트(`() => objTestLoad.DryRun_Loading(...)`)는 **ThreadPool의 워커 스레드**에 큐잉되어, 스레드풀이 여유 스레드를 배정해 실행.
3. `Task.Run`은 그 작업을 나타내는 `Task` 객체를 즉시 반환 — 이 `Task`로 나중에 `await`/`.Wait()`로 완료를 기다릴 수 있음.

### 주의: WinForms UI 스레드 관련
`DryRun_Loading` 내부에서 UI 컨트롤을 직접 건드리면(`label1.Text = ...` 등) `InvalidOperationException` (Cross-thread operation not valid) 발생.
→ 스레드풀 스레드에는 `SynchronizationContext`가 없으므로, UI 갱신이 필요하면 `Invoke`/`BeginInvoke`로 마샬링하거나 `IProgress<T>` 패턴 사용.

---

## 3. Task를 `await` / `.Wait()` / `GetAwaiter().GetResult()`로 사용하는 예시

```csharp
private CancellationTokenSource cts = new CancellationTokenSource();
private TestLoad objTestLoad = new TestLoad();

// ===== 방법 1: async 메서드 안에서 await (권장) =====
private async Task ButtonClick_AsyncAwait()
{
    Task taskLoad = Task.Run(() => objTestLoad.DryRun_Loading(cts.Token, true));

    global.WriteLog("로딩 작업 시작, 다른 일 처리 중...");

    // taskLoad가 끝날 때까지 "논블로킹"으로 대기
    // 호출 스레드가 멈추지 않고, 완료되면 이어서 실행됨
    await taskLoad;

    global.WriteLog("로딩 작업 완료!"); // await 이후 실행되는 부분 (continuation)
}

// ===== 방법 2: 동기 메서드에서 .Wait() (블로킹) =====
private void ButtonClick_Wait()
{
    Task taskLoad = Task.Run(() => objTestLoad.DryRun_Loading(cts.Token, true));

    // 호출 스레드가 여기서 "그대로 멈춰서" 기다림
    // UI 스레드에서 호출 시 UI가 멈춤 (비권장)
    taskLoad.Wait();

    global.WriteLog("로딩 작업 완료!");
}

// ===== 방법 3: GetAwaiter().GetResult() (블로킹, 예외 전파 방식이 Wait()와 다름) =====
private void ButtonClick_GetAwaiterGetResult()
{
    Task taskLoad = Task.Run(() => objTestLoad.DryRun_Loading(cts.Token, true));

    // Wait()는 예외를 AggregateException으로 감싸서 던지지만
    // GetAwaiter().GetResult()는 원래 예외를 그대로(unwrap해서) 던짐
    taskLoad.GetAwaiter().GetResult();

    global.WriteLog("로딩 작업 완료!");
}

// ===== 반환값이 있는 Task<T>라면 =====
private async Task ButtonClick_WithResult()
{
    Task<bool> taskLoad = Task.Run(() => objTestLoad.DryRun_LoadingWithResult(cts.Token, true));

    bool result = await taskLoad; // 완료 후 결과값 받음
    global.WriteLog($"로딩 결과: {result}");
}
```

### 실무 WinForms 버튼 클릭 이벤트 예시

```csharp
private async void btnLoad_Click(object sender, EventArgs e)
{
    btnLoad.Enabled = false;
    try
    {
        await Task.Run(() => objTestLoad.DryRun_Loading(cts.Token, true));
        global.WriteLog("완료");
    }
    catch (OperationCanceledException)
    {
        global.WriteLog("취소됨");
    }
    finally
    {
        btnLoad.Enabled = true; // await 이후이므로 다시 UI 스레드에서 안전하게 실행됨
    }
}
```

---

## 4. `await`를 만나면 호출 스레드가 스택에서 사라지는데, Task 객체는 왜 같이 사라지지 않는가?

**핵심: `Task` 객체는 스택이 아니라 힙(heap)에 있는 객체이기 때문에, 호출 스레드가 리턴되어도 사라지지 않는다.**

`await`를 만나면 사라지는 건 **"메서드 실행 중이던 스택 프레임"**이지, **Task 객체 자체**가 아님 — 이 둘은 분리해서 생각해야 함.

- **오해**: "호출 스레드가 사라지면 Task 변수도 같이 사라진다"
  **실제**: Task 객체는 힙에 있고, 상태머신 객체가 참조를 들고 있어서 계속 살아있음
- **오해**: "스레드가 스택 프레임을 버린다 = 데이터 유실"
  **실제**: 지역변수들은 애초에 스택이 아니라 상태머신의 필드(힙)에 저장되어 있었음
- **오해**: "await 이후 코드는 어딘가에 저장된다"
  **실제**: 그 "어딘가"가 바로 상태머신의 `MoveNext()` 메서드와 `state` 필드

즉, `async` 메서드는 컴파일 시점에 **평범한 메서드가 아니라 "콜백 체인을 가진 클래스"로 재작성**된다.

---

## 5. `async` 메서드가 상태머신 객체로 변환되는 과정 (의사 코드 상세)

### 원본 코드

```csharp
private async void btnLoad_Click(object sender, EventArgs e)
{
    Task taskLoad = Task.Run(() => objTestLoad.DryRun_Loading(cts.Token, true));

    global.WriteLog("로딩 작업 시작, 다른 일 처리 중...");

    await taskLoad;

    global.WriteLog("로딩 작업 완료!");
}
```

### 컴파일러가 생성하는 상태머신 (개념적 의사 코드)

```csharp
// 컴파일러가 원본 메서드를 아래와 같은 형태로 "재작성"한다고 이해하면 됨

// 1) 원본 메서드는 껍데기만 남고, 상태머신을 생성해서 시작시키는 역할만 함
private void btnLoad_Click(object sender, EventArgs e)
{
    // 힙에 상태머신 객체 생성
    var stateMachine = new BtnLoadClickStateMachine();

    // 원본 메서드의 매개변수/필드처럼 쓰일 값들을 상태머신 필드에 채워넣음
    stateMachine.sender = sender;
    stateMachine.e = e;
    stateMachine.state = -1; // 초기 상태

    // 진입점 실행 (MoveNext를 최초 1회 호출)
    stateMachine.MoveNext();
}

// 2) 컴파일러가 자동 생성하는 상태머신 클래스 (힙에 할당됨)
private class BtnLoadClickStateMachine
{
    // ----- 필드 영역 -----
    // 원본 메서드의 매개변수
    public object sender;
    public EventArgs e;

    // 원본 메서드의 "지역변수"들이 전부 여기 필드로 옮겨짐
    // (스택이 아니라 힙에 저장되므로 메서드 실행이 잠시 끊겨도 값이 유지됨)
    public Task taskLoad;

    // 현재 어느 지점까지 실행했는지 기록하는 상태값
    public int state;

    // taskLoad를 기다리기 위한 awaiter (완료 여부/콜백 등록 담당)
    private TaskAwaiter awaiter;

    // ----- 실행 로직: 원본 메서드 본문이 여기로 옮겨짐 -----
    public void MoveNext()
    {
        try
        {
            switch (state)
            {
                case -1:
                    // ===== await 이전 코드 =====
                    taskLoad = Task.Run(() => objTestLoad.DryRun_Loading(cts.Token, true));

                    global.WriteLog("로딩 작업 시작, 다른 일 처리 중...");

                    // ===== await taskLoad; 부분 =====
                    awaiter = taskLoad.GetAwaiter();

                    if (!awaiter.IsCompleted)
                    {
                        // 아직 안 끝났다면:
                        // "끝나면 나(MoveNext)를 다시 호출해줘"라고 콜백 등록
                        state = 0; // 다음에 재개할 지점 기록
                        awaiter.OnCompleted(this.MoveNext);

                        // 여기서 메서드 실행을 "중단"하고
                        // 호출 스레드에게 제어권을 그대로 반환함
                        // (호출 스레드는 UI 메시지 루프 등으로 복귀)
                        return;
                    }

                    // 이미 완료돼 있었다면 바로 다음 case로 진행 (동기적 완료 최적화)
                    goto case 0;

                case 0:
                    // ===== await 이후 코드 (continuation) =====
                    // Task 실행 중 예외가 있었다면 GetResult()에서 여기서 다시 던져짐
                    awaiter.GetResult();

                    global.WriteLog("로딩 작업 완료!");
                    break;
            }
        }
        catch (Exception ex)
        {
            // async void 메서드는 예외를 호출자에게 리턴할 방법이 없으므로
            // SynchronizationContext를 통해 처리(대개 그대로 throw되어 앱 크래시 위험)
            throw;
        }
    }
}
```

### 핵심 포인트

| 원본 코드 요소 | 상태머신에서의 정체 |
|---|---|
| 메서드의 지역변수 (`taskLoad`) | 상태머신 객체의 **필드** (힙에 저장) |
| `await taskLoad;` | `GetAwaiter()` → `IsCompleted` 체크 → 미완료 시 `OnCompleted(MoveNext)` 콜백 등록 후 `return` |
| await 이전 코드 | `MoveNext()`의 앞쪽 `case` 블록 |
| await 이후 코드 (continuation) | `MoveNext()`의 다음 `case` 블록 |
| "실행 재개 지점" | `state` 필드 값으로 기록 |

---

## 6. await 완료 후 continuation은 "다른 스레드에 맡겨지는" 것인가?

**아니다.** 정확히는:

> 작업을 끝낸 바로 그 스레드가, 그 자리에서 곧바로 이어서 실행한다 (콜백을 직접 호출). 단, `SynchronizationContext`가 캡처되어 있다면 그 컨텍스트로 실행을 위임(Post)한다.

### 동작 흐름 (WinForms 버튼 클릭 예시)

1. **UI 스레드**가 `Task.Run` 호출 → 작업이 스레드풀 워커스레드 A에게 큐잉됨.
   UI 스레드는 `await`까지 진행 → 아직 완료 안 됐으니 콜백(`MoveNext`)을 `taskLoad`에 등록 → **UI 메시지 루프로 즉시 복귀**.

2. **스레드풀 워커스레드 A**가 `DryRun_Loading` 실행. 완료되면 `taskLoad`를 "완료" 상태로 마킹하면서, 등록되어 있던 콜백을 **스레드 A 자신이 직접 호출**하려고 시도.

3. 여기서 분기:
   - **UI 스레드(WinForms)에서 await 한 경우**: `SynchronizationContext`가 캡처되어 있어서, 워커스레드 A는 직접 실행하지 않고 "이 콜백을 UI 스레드 메시지 큐에 넣어줘(Post)"라고 요청만 함 → UI 스레드가 나중에 메시지 루프를 돌 때 꺼내서 실행 → `global.WriteLog("완료")`는 **다시 UI 스레드에서** 실행됨.
   - **콘솔 앱이거나 `ConfigureAwait(false)`를 쓴 경우**: `SynchronizationContext`가 없으므로, **워커스레드 A가 그 자리에서 직접** continuation을 실행.

### 정리

| 표현 | 정확도 |
|---|---|
| "맡은 스레드가 상태머신의 상태를 변경한다" | ✅ 정확 (Task를 완료 상태로 바꾸고 콜백 호출) |
| "또 다른 스레드에 맡겨서 실행한다" | ❌ 새 스레드에 위임하는 게 아니라, **완료시킨 스레드 자신이 직접 실행**하거나 (SynchronizationContext가 있으면) **원래 캡처해둔 컨텍스트(대개 UI 스레드)로 되돌려 보내는** 것 |

`Task.Run`으로 새 작업을 스레드풀에 큐잉하는 것과, `await`의 continuation이 실행되는 것은 메커니즘이 다름:
- **전자**: 명시적으로 스레드풀에 작업을 던짐
- **후자**: "누군가 끝났을 때 저를 불러주세요"라고 콜백을 등록해뒀다가, 완료 시점에 (컨텍스트에 따라) 그 스레드가 직접 부르거나 원래 스레드로 돌아가는 것

WinForms에서 이 `SynchronizationContext` 캡처 덕분에, `await` 이후에 UI 컨트롤(`label1.Text = ...` 등)을 별도의 `Invoke` 없이도 안전하게 건드릴 수 있음.
