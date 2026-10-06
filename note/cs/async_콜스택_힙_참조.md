# async 상태머신과 콜스택 / 힙 / 스레드 관계 정리

원본 코드:

```csharp
async Task<bool> method() {
    // 작업1
    bool result = await methodAsync2();
    // 작업2
}

void 일반스레드() {
    // 초기화 작업..
    method();
    // 정리작업...
}
```

---

## 1. 일반 스레드의 콜스택이 힙에 할당된 `method` 상태머신을 참조하는가?

**결론**: "실행되는 동안"에는 콜스택이 힙의 상태머신을 참조하지만, `await`로 실행이 중단되고 콜스택이 풀리고 나면 그 스택 기반 참조는 사라진다. 이후 상태머신이 계속 살아있는 이유는 스택 참조가 아니라, **"Task 객체가 상태머신을 가리키는 콜백(delegate)을 들고 있기 때문"**이다.

### 컴파일러가 만드는 구조 (개념)

```csharp
// method()는 사실 "스타터 메서드"로 바뀜
private Task<bool> method()
{
    var sm = new MethodStateMachine();          // 힙에 할당
    sm.state = -1;
    sm.builder = AsyncTaskMethodBuilder<bool>.Create();
    sm.builder.Start(ref sm);   // 내부적으로 sm.MoveNext()를 최초 1회 호출
    return sm.builder.Task;      // 즉시 Task<bool> 반환 (완료 여부 무관)
}

private class MethodStateMachine
{
    public int state;
    public AsyncTaskMethodBuilder<bool> builder;
    public bool result;
    private TaskAwaiter<bool> awaiter;

    public void MoveNext()
    {
        // ... 작업1, await methodAsync2(), 작업2 ...
    }
}
```

### ① `method()` 호출 순간 — 콜스택이 힙 객체를 참조함

```
[콜스택 - 일반스레드가 쌓아올림]
┌─────────────────────────────┐
│ MethodStateMachine.MoveNext()│ ← 방금 진입, "작업1" 실행 중
├─────────────────────────────┤
│ method() (스타터 메서드)      │ ← sm 지역변수가 힙 객체를 참조
├─────────────────────────────┤
│ 일반스레드()                  │
└─────────────────────────────┘
```

- `method()` 프레임의 지역변수 `sm`이 힙 객체를 가리킴
- `MoveNext()` 프레임은 그 힙 객체의 인스턴스 메서드로 실행 중이므로, `this`가 곧 그 힙 객체

### ② `await methodAsync2()`에서 아직 완료 안 됐을 때

```csharp
case -1:
    // 작업1
    var task2 = methodAsync2();
    awaiter = task2.GetAwaiter();
    if (!awaiter.IsCompleted)
    {
        state = 0;
        awaiter.OnCompleted(this.MoveNext);  // ★ 여기가 핵심
        return;  // MoveNext() 리턴 → 스택 프레임 pop
    }
    goto case 0;
```

- `this.MoveNext`는 **"이 상태머신 인스턴스에 바인딩된 메서드 포인터(delegate)"**
- 이 delegate가 `task2`(`methodAsync2`의 Task) 안에 **콜백으로 저장**됨
- 즉, **`task2` 객체가 (delegate를 통해) `MethodStateMachine` 힙 객체에 대한 참조를 붙잡고 있는 상태**가 됨

`return` 실행 후:

```
[콜스택]
┌─────────────────────────────┐
│ 일반스레드()                  │  ← method() 프레임도 pop됨, 여기로 복귀
└─────────────────────────────┘
```

- `MoveNext()` 프레임 pop, `method()` 프레임도 pop
- **이 시점부터 콜스택에는 더 이상 `MethodStateMachine`을 가리키는 것이 없음**

### ③ 그런데 `MethodStateMachine`은 왜 GC 안 되고 살아있나?

```
[힙]
task2 (methodAsync2가 반환한 Task)
  └─ 내부 continuation 리스트
       └─ delegate (Action) → MethodStateMachine 인스턴스를 참조
```

`task2`가 콜백 delegate를 들고 있고, 그 delegate가 `MethodStateMachine` 인스턴스를 참조하므로 **GC 루트에서 도달 가능(reachable)** → 스택이 다 풀려도 GC되지 않고 계속 살아있음.

나중에 `methodAsync2`가 완료되면, `task2`가 저장해뒀던 delegate(`MoveNext`)를 호출 → 다시 `MethodStateMachine.MoveNext()`가 실행되며 "작업2" 처리. 이때는 **스레드풀 스레드(또는 SynchronizationContext에 따라 다른 스레드)의 새 콜스택**이지, 원래 `일반스레드()`의 콜스택과는 무관함.

### 정리 표

| 시점 | 콜스택이 상태머신을 참조하는가? |
|---|---|
| `method()` 호출 ~ 첫 `await`에서 중단되기 전 | ✅ 예 — `method()` 프레임의 지역변수, `MoveNext()`의 `this`로 참조 |
| 첫 `await`에서 미완료 상태로 `return`한 직후 | ❌ 아니오 — 스택 프레임 pop됨. 이후엔 **Task가 delegate로 참조**하는 방식으로만 생존 |
| 완료 후 continuation(`MoveNext` 재호출) 실행 시 | ✅ 예 — 단, **그때의 콜스택은 원래 호출 스레드가 아니라 완료를 처리하는 스레드(스레드풀 등)의 새 콜스택** |

즉, "스택이 상태머신을 참조하느냐"는 **순간적으로만 참**이고, 상태머신이 여러 `await` 지점을 오가며 오래 살아있을 수 있는 진짜 이유는 **Task 객체 안의 콜백 체인이 참조를 붙들고 있기 때문**. 이게 왜 `async` 메서드의 지역변수들이 "스택이 아니라 힙"에 있어야만 하는지에 대한 근본적인 이유이기도 함 — 스택 기반이었다면 애초에 이런 생존이 불가능함.

---

## 2. "await를 만나면 콜스택이 사라진다" = 일반 스레드가 사라진다는 뜻인가?

**아니다.** 스레드 자체가 사라지는 게 아니라, **스레드가 실행 중이던 콜스택의 "프레임들"이 pop되는 것**이다.

### 스레드 ≠ 콜스택 프레임

- **스레드**: OS가 관리하는 실행 흐름의 주체. `일반스레드()`를 실행하는 스레드는 프로그램이 끝날 때까지(혹은 명시적 종료 전까지) **계속 살아있음**.
- **콜스택 프레임**: 그 스레드가 호출한 메서드들의 "돌아갈 위치, 지역변수" 등을 기록한 스택의 한 칸. 메서드 호출 시 push, `return` 시 pop.

`await`에서 "콜스택이 사라진다"는 건, **그 스레드가 죽는다는 뜻이 아니라, 단지 `MoveNext()`와 `method()` 프레임이 정상적으로 `return`되면서 스택에서 빠진다**는 뜻. 평범한 메서드가 `return`하면 프레임이 빠지는 것과 원리는 동일함. 다만 "아직 논리적으로 다 안 끝났는데 미리 return한다"는 점이 특이할 뿐.

### 시간 순서로 본 콜스택 변화

```
시각 t0: 초기화 작업 실행 중
┌─────────────┐
│ 일반스레드()  │
└─────────────┘

시각 t1: method() 호출 → 상태머신 생성 → MoveNext() 진입 → "작업1" 실행 중
┌─────────────────────────────┐
│ MethodStateMachine.MoveNext()│
├─────────────────────────────┤
│ method()                     │
├─────────────────────────────┤
│ 일반스레드()                  │
└─────────────────────────────┘

시각 t2: await methodAsync2()에서 미완료 → OnCompleted 등록 → return, return
┌─────────────┐
│ 일반스레드()  │   ← MoveNext(), method() 프레임은 pop되어 사라짐
└─────────────┘
   (스레드는 여기서 계속 실행을 이어감, 죽지 않음!)

시각 t3: 정리작업 실행 중 (여전히 같은 스레드)
┌─────────────┐
│ 일반스레드()  │
└─────────────┘
```

**t2 → t3 구간에서 "일반스레드"라는 프레임은 스택에서 한 번도 빠진 적이 없음.** 스레드는 `method()` 호출 이전부터 지금까지 **쭉 같은 스레드로, 끊기지 않고** 실행됨. `method()`와 `MoveNext()`라는 **안쪽 프레임 2개만 pop**되어 사라진 것이고, 그 결과 스레드는 자연스럽게 `method()` 호출문 다음 줄인 `// 정리작업...`으로 넘어감.

### 비유 — 일반 동기 메서드와 동일한 원리

```csharp
void 일반스레드() {
    작업A();
    작업B();
}
void 작업A() {
    // 뭔가 함
} // return하면 작업A 프레임이 pop됨
```

`작업A()`가 `return`해서 프레임이 사라진다고 "일반스레드가 사라졌다"고 하지 않음. `async` 메서드도 원리는 동일함. 다만 **"아직 논리적으로 다 안 끝났는데(미래 시점에 재개될 걸 알면서도) 미리 return한다"**는 점이 특이할 뿐, 프레임 pop → 호출자로 제어 복귀라는 스택의 기본 동작은 동일.

### 정리 표

| 오해 | 실제 |
|---|---|
| "await를 만나면 스레드가 사라진다/죽는다" | ❌ 스레드는 계속 살아있고 그대로 다음 코드를 실행함 |
| "콜스택이 사라진다 = 실행 흐름 전체가 없어진다" | ❌ 안쪽 메서드(상태머신)의 프레임만 pop되고, 바깥쪽(`일반스레드()`) 프레임은 그대로 남아 계속 실행됨 |
| "method()가 끝난 것처럼 보인다" | ✅ 맞음 — 정확히는 `method()`가 (완료되지 않은) `Task<bool>`을 반환하며 정상적으로 `return`한 것. 호출자는 그걸 `await`하지 않았으므로 그냥 무시하고 다음 줄로 진행 |

**결론**: 이 예제에서 `일반스레드()`가 `method()`를 `await` 없이 호출했기 때문에, `method()` 내부의 `await`가 어디서 중단되든 상관없이 **`일반스레드()`는 거의 즉시 `// 정리작업...`으로 넘어감.** `method()`의 나머지 부분(작업2, 결과 확정)은 나중에 `methodAsync2()`가 끝난 시점에 (주로) 다른 스레드가 이어서 처리하게 됨.

---

## 3. `methodAsync2()` 내부에 `Task.Run(...)`이 없어도 다른 스레드가 이어서 처리하는가?

**"다른 스레드가 이어서 처리한다"는 게 보장되는 게 아니라, `methodAsync2()` 내부에서 실제로 무엇을 `await`하느냐에 따라 완전히 달라진다.** `Task.Run`은 스레드 전환을 일으키는 여러 방법 중 하나일 뿐이다.

### 핵심: 스레드 전환은 "가장 안쪽에서 실제로 대기하는 대상"이 결정한다

`await`는 그 자체로 스레드를 바꾸는 마법이 아니다. 상태머신이 몇 겹으로 쌓여있든, **결국 맨 밑바닥에서 뭘 기다리는지**가 실제 스레드 동작을 결정한다.

#### 케이스 1: 내부에 진짜 비동기 I/O가 있는 경우 (파일, 네트워크, DB)

```csharp
async Task<bool> methodAsync2()
{
    // 네트워크 I/O — OS의 IOCP(I/O Completion Port) 사용
    var data = await httpClient.GetStringAsync(url);
    return data.Length > 0;
}
```

- `Task.Run`이 없어도, `GetStringAsync`는 내부적으로 **OS 커널 레벨의 비동기 I/O**를 사용함. 요청을 OS에 던져놓고, 그동안 **어떤 스레드도 점유하지 않음** (블로킹 대기가 아니라 진짜 비동기).
- 응답이 도착하면 **IOCP 스레드풀**이 콜백을 받아 실행 → 이때 `methodAsync2`, `method` 상태머신의 `MoveNext`가 이어서 실행됨.
- "다른 스레드"가 이어받는 게 맞음. 단, `Task.Run`이 스레드풀에 작업을 큐잉한 게 아니라, **IOCP가 I/O 완료를 통지받은 스레드**라는 차이가 있음.

#### 케이스 2: `Task.Delay` (타이머 기반)

```csharp
async Task<bool> methodAsync2()
{
    await Task.Delay(1000); // 타이머
    return true;
}
```

- 스레드를 전혀 점유하지 않고 **타이머**만 걸어둠.
- 1초 후 **타이머 콜백을 처리하는 스레드풀 스레드**가 이어받아 실행.
- "다른 스레드"지만, `Task.Run`과는 메커니즘이 다름 (스레드풀에 작업 큐잉이 아니라 타이머 만료 콜백).

#### 케이스 3: 내부에 진짜 비동기 대기가 "전혀 없는" 경우 ⚠️ 중요

```csharp
async Task<bool> methodAsync2()
{
    // CPU 연산만 있고 await할 게 없음
    int sum = 0;
    for (int i = 0; i < 1000000; i++) sum += i;
    return sum > 0;
    // await가 하나도 없으면 컴파일러 경고(CS1998)까지 뜸
}
```

- 상태머신이 만들어지긴 하지만, 실행 중단 지점(`await`)이 없으므로 **처음부터 끝까지 호출한 스레드가 동기적으로 다 실행**함.
- **스레드 전환이 전혀 일어나지 않음.** `일반스레드`가 `method()` → `methodAsync2()`까지 쭉 이어서 실행하고, 완료된 `Task`(이미 완료 상태)를 반환받고 계속 진행함.

#### 케이스 4: 내부에 `await`는 있지만, 이미 완료된 상태였다면?

```csharp
async Task<bool> methodAsync2()
{
    var cached = GetCachedResultIfAny(); // 캐시에 이미 있음
    if (cached != null)
        return cached.Value;

    var result = await SomeIoCallAsync(); // 실행이 여기까지 안 옴
    return result;
}
```

- `await`문 자체를 거치지 않고 return하면, 당연히 스레드 전환도 없음.
- 설령 `await SomeIoCallAsync()`를 거치더라도, 그 시점에 **이미 결과가 나와 있는 상태(동기적으로 즉시 완료)**라면 — 예를 들어 캐시된 Task거나 이미 완료된 Task라면 — `IsCompleted == true`이므로 `OnCompleted` 콜백 등록 없이 **그대로 이어서 실행**됨. 이때도 스레드 전환 없음.

### 정리 표

| 상황 | 스레드 전환 여부 |
|---|---|
| 내부에 `Task.Run(...)` | ✅ 스레드풀 워커 스레드로 전환 |
| 내부에 진짜 비동기 I/O (`HttpClient`, 파일 `ReadAsync`, DB async 호출 등) | ✅ 전환됨 (IOCP 콜백 스레드) |
| 내부에 `Task.Delay` | ✅ 전환됨 (타이머 콜백 스레드) |
| 내부에 `await`할 대상이 없거나 (동기 코드만) | ❌ 전환 없음, 호출 스레드가 끝까지 동기 실행 |
| `await` 시점에 대상이 이미 완료돼 있었음 | ❌ 전환 없음, 곧바로 이어서 실행 (동기적 완료 최적화) |

### 결론

**"await를 만나면 무조건 다른 스레드로 넘어간다"는 생각은 틀렸다.** 정확히는:

> **await는 "완료될 때까지 현재 실행을 중단하고, 완료되면 재개하겠다"는 의미일 뿐, 그 재개가 어느 스레드에서 일어날지는 전혀 보장하지 않는다.** 재개 스레드는 그 Task를 누가/어떻게 완료시켰는지(스레드풀 콜백, IOCP, 타이머, 혹은 아예 스레드 전환 없이 즉시 동기 완료)에 따라 결정된다.

`Task.Run`이 없는 `methodAsync2()`가 다른 스레드에서 이어서 처리되는지 여부는, **그 메서드 내부를 직접 열어봐야 알 수 있음** — 진짜 비동기 I/O나 타이머가 있으면 전환되고, 순수 CPU 연산뿐이면 전환 없이 호출 스레드가 그대로 끝까지 실행함.
