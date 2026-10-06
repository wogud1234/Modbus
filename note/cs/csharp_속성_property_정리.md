# C# 속성(Property) 정리

## 목차
1. 속성이란
2. 필드 vs 속성
3. 속성의 기본 형태와 종류
4. 자동 구현 속성과 백킹 필드
5. 접근 제한자 / 읽기 전용 / init / required
6. 표현식 본문(expression-bodied)과 초기화
7. 실무 패턴 (검증, 변경 알림, 지연 초기화)
8. 컴파일 후 코드 모습
9. 성능
10. 제약 사항과 주의점
11. 역사 (C# 버전별 변화)
12. 필드와 속성을 같이 선언했을 때의 함정
13. 요약
14. 자동 구현 속성 vs 수동 구현 속성: 사용 시점과 예시

---

## 1. 속성이란

- **필드처럼 읽고 쓰지만, 내부적으로는 메서드(get/set)가 실행되는 멤버**.
- 사용하는 쪽에서는 필드 접근 문법(`obj.Name`)을 쓰고, 선언하는 쪽에서는 접근 시점에 코드를 실행할 수 있다.
- 객체의 **상태(데이터)를 외부에 노출하는 표준 방법**이며, Java의 getter/setter 관례(`getName()`, `setName()`)를 언어 차원에서 문법으로 지원한 것이다.

```csharp
// Java 스타일
private string name;
public string getName() { return name; }
public void setName(string value) { name = value; }

// C# 속성
private string name;
public string Name
{
    get { return name; }
    set { name = value; }   // value: setter에 전달된 값을 가리키는 암시적 매개변수
}
```

---

## 2. 필드 vs 속성

| 항목 | 필드 (field) | 속성 (property) |
|---|---|---|
| 정체 | 데이터 저장 공간 | 접근자 메서드(get/set) 묶음 |
| 로직 삽입 | 불가 | 가능 (검증, 로깅, 이벤트 등) |
| 접근 제어 | 읽기/쓰기 동일 수준 | get과 set에 서로 다른 접근 수준 부여 가능 |
| 읽기 전용 표현 | `readonly` (생성자에서만 쓰기) | get만 선언 / `private set` / `init` |
| 인터페이스 멤버 | 불가 | 가능 |
| virtual / abstract | 불가 | 가능 |
| `ref` / `out` 전달 | 가능 | **불가** (메서드 호출이라 주소가 없음) |
| `volatile` | 가능 | 불가 (백킹 필드가 아닌 속성 자체에는 적용 안 됨) |
| 데이터 바인딩 (WinForms/WPF) | 대부분 불가 | **가능** (속성만 인식) |
| 리플렉션/직렬화 | 도구에 따라 다름 | 대부분의 도구가 속성 기준 |
| 관례 | 보통 `private`, camelCase | 보통 `public`, PascalCase |

### 왜 필드를 public으로 노출하지 않고 속성을 쓰는가
1. **캡슐화**: 나중에 검증이나 변경 알림을 추가해도 **호출하는 쪽 코드를 바꾸지 않아도 됨**.
2. **이진 호환성(binary compatibility)**: 필드를 속성으로 바꾸면 소스 코드는 그대로여도 **이미 컴파일된 어셈블리는 다시 빌드해야 함** (필드 접근 IL과 메서드 호출 IL이 다르므로). 처음부터 속성으로 두면 이 문제가 없다.
3. **프레임워크 요구**: 데이터 바인딩, 직렬화, PropertyGrid 등은 속성을 대상으로 동작한다.

---

## 3. 속성의 기본 형태와 종류

### 3-1. 완전 구현 속성 (직접 백킹 필드 선언)
```csharp
private int speed;

public int Speed
{
    get { return speed; }
    set { speed = value; }
}
```

### 3-2. 읽기 전용 속성 (get만)
```csharp
private readonly string name;
public string Name { get { return name; } }
```

### 3-3. 쓰기 전용 속성 (set만) — 거의 쓰지 않음
```csharp
private string password;
public string Password { set { password = value; } }
```

### 3-4. 계산 속성 (저장 공간 없음)
```csharp
public double Width { get; set; }
public double Height { get; set; }

public double Area => Width * Height;   // 백킹 필드가 없음. 접근할 때마다 계산
```

### 3-5. 자동 구현 속성
```csharp
public string Name { get; set; }
```
→ 4장 참조.

### 3-6. 인덱서 (this[...] 형태의 속성)
```csharp
public class RegisterMap
{
    private readonly ushort[] regs = new ushort[100];

    public ushort this[int address]
    {
        get => regs[address];
        set => regs[address] = value;
    }
}

var map = new RegisterMap();
map[10] = 1234;      // 속성 문법과 동일하게 사용
```

### 3-7. virtual / abstract / 인터페이스 속성
```csharp
public interface IDevice
{
    string Name { get; }          // 인터페이스에는 구현 없이 선언만
}

public abstract class DeviceBase : IDevice
{
    public abstract string Name { get; }
    public virtual int Timeout { get; set; } = 1000;
}
```

### 3-8. static 속성
```csharp
public static class Config
{
    public static string Path { get; set; } = @"C:\Config";
}
```

---

## 4. 자동 구현 속성과 백킹 필드

### 개념
```csharp
public string Name { get; set; }
```
- get/set 본문을 생략하면 컴파일러가 **보이지 않는 백킹 필드(backing field)** 를 자동 생성하고, get/set이 그 필드를 읽고 쓰도록 구현한다.
- 개발자는 그 필드에 **이름으로 접근할 수 없다** (C# 14의 `field` 키워드 제외, 11장 참조).

### 자동 구현 속성과 직접 선언한 필드는 연결되지 않는다
```csharp
private RegisterStore store;                  // 필드 A (직접 선언)
public RegisterStore Store { get; set; }      // 속성 + 컴파일러가 만든 숨은 필드 B
```
- `store`(A)와 `Store`(B)는 **서로 다른 저장 공간**이다. `Store = x;`를 해도 `store`는 바뀌지 않는다.
- 필드를 따로 선언했다면 속성 안에서 그 필드를 직접 사용해야 의미가 있다.

```csharp
private RegisterStore store;

public RegisterStore Store
{
    get => store;
    set => store = value;
}
```

### 직접 필드 + 속성이 필요한 경우
- setter에서 **검증 / 이벤트 발생**(`INotifyPropertyChanged`)이 필요할 때
- 클래스 내부에서 **필드를 직접** 쓰고 싶을 때
- **`volatile` 이 필요할 때** (자동 구현 속성의 백킹 필드에는 `volatile`을 붙일 수 없음)

```csharp
private volatile bool running;
public bool Running => running;   // 외부에는 읽기 전용으로 노출
```

---

## 5. 접근 제한자 / 읽기 전용 / init / required

### 5-1. get과 set에 서로 다른 접근 수준
```csharp
public int Count { get; private set; }      // 밖에서는 읽기만, 클래스 안에서만 쓰기
public int Id { get; protected set; }
```
- 규칙: **접근자의 접근 수준은 속성 자체보다 제한적이어야** 한다 (`public` 속성의 `set`을 `private`로 좁히는 것은 OK, 반대는 불가).

### 5-2. get-only 자동 속성 (C# 6.0)
```csharp
public string Name { get; }                 // 생성자 또는 초기화자에서만 할당 가능

public Device(string name)
{
    Name = name;
}
```
- 백킹 필드가 **`readonly`** 로 생성된다.

### 5-3. init 접근자 (C# 9.0)
```csharp
public string Name { get; init; }

var d = new Device { Name = "Camera1" };    // 객체 초기화 시점에만 할당 가능
// d.Name = "Camera2";                      // 컴파일 에러
```
- **객체 초기화자(`new T { ... }`)나 생성자에서만** 쓸 수 있는 setter. 불변 객체를 초기화자 문법으로 만들 때 쓴다.

### 5-4. required 멤버 (C# 11)
```csharp
public class Device
{
    public required string Name { get; init; }
}

// var d = new Device();                    // 컴파일 에러: Name 미지정
var d = new Device { Name = "Camera1" };
```
- 객체를 만들 때 **반드시 초기화해야 하는** 멤버로 강제한다.

---

## 6. 표현식 본문(expression-bodied)과 초기화

### 6-1. 표현식 본문 (C# 6.0 / 7.0)
```csharp
public string FullName => $"{First} {Last}";            // get-only 계산 속성 (6.0)

public string Name
{
    get => name;                                        // 접근자 단위 (7.0)
    set => name = value ?? throw new ArgumentNullException(nameof(value));
}
```
- `=>`는 `{ get { return ...; } }`의 축약형이다.

### 6-2. 자동 속성 초기화자 (C# 6.0)
```csharp
public int Timeout { get; set; } = 3000;
public List<string> Logs { get; } = new List<string>();
```
- 백킹 필드의 **초기값**을 선언과 동시에 지정한다.
- 주의: `=>`와 `=`는 완전히 다르다.

```csharp
public List<int> A { get; } = new List<int>();   // 인스턴스 생성 시 한 번 만들고 계속 재사용
public List<int> B => new List<int>();           // 접근할 때마다 새 리스트를 만듦 (흔한 실수)
```

---

## 7. 실무 패턴

### 7-1. 검증
```csharp
private int speed;
public int Speed
{
    get => speed;
    set
    {
        if (value < 0 || value > 5000)
            throw new ArgumentOutOfRangeException(nameof(value));
        speed = value;
    }
}
```

### 7-2. 변경 알림 (INotifyPropertyChanged, WinForms/WPF 데이터 바인딩)
```csharp
public class ViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private string status;
    public string Status
    {
        get => status;
        set
        {
            if (status == value) return;
            status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }
}
```

### 7-3. 지연 초기화 (lazy)
```csharp
private RegisterStore store;
public RegisterStore Store => store ??= new RegisterStore();   // 처음 접근할 때 생성
```

### 7-4. 스레드 간 플래그 노출
```csharp
private volatile bool running;
public bool IsRunning => running;
```

### 7-5. 외부에는 읽기 전용 컬렉션으로 노출
```csharp
private readonly List<string> logs = new List<string>();
public IReadOnlyList<string> Logs => logs;
```

---

## 8. 컴파일 후 코드 모습

속성은 **문법 설탕(syntactic sugar)** 이다. 컴파일하면 **메서드와 필드**로 바뀐다.

### 8-1. 자동 구현 속성
**작성한 코드**
```csharp
public class Device
{
    public string Name { get; set; }
}
```

**컴파일 후 (디컴파일 개념 코드)**
```csharp
public class Device
{
    [CompilerGenerated]
    private string <Name>k__BackingField;        // 숨은 백킹 필드 (이름에 <, > 가 있어 C#에서 직접 접근 불가)

    public string Name
    {
        [CompilerGenerated] get { return <Name>k__BackingField; }
        [CompilerGenerated] set { <Name>k__BackingField = value; }
    }
}
```

**IL 수준**
```
.field private string '<Name>k__BackingField'

.property instance string Name()
{
    .get instance string Device::get_Name()
    .set instance void   Device::set_Name(string)
}

.method public hidebysig specialname instance string get_Name() cil managed
{
    ldarg.0
    ldfld string Device::'<Name>k__BackingField'
    ret
}

.method public hidebysig specialname instance void set_Name(string 'value') cil managed
{
    ldarg.0
    ldarg.1
    stfld string Device::'<Name>k__BackingField'
    ret
}
```
핵심은 다음과 같다.
- 속성 하나 → **`get_이름()` / `set_이름(value)` 메서드**로 변환 (`specialname` 표시)
- `.property` 항목은 **메타데이터**일 뿐이고, 실제 실행되는 것은 메서드 호출
- 컴파일러가 만든 필드는 `<속성명>k__BackingField` 이름을 갖는다

### 8-2. 사용하는 쪽 코드도 바뀐다
```csharp
d.Name = "Camera1";
string n = d.Name;
```
↓ 컴파일 후
```csharp
d.set_Name("Camera1");
string n = d.get_Name();
```
```
callvirt instance void Device::set_Name(string)
callvirt instance string Device::get_Name()
```
- 이 때문에 **필드 → 속성으로 바꾸면 이진 호환이 깨진다** (`ldfld` → `callvirt`).
- 클래스에 `get_Name` 같은 이름의 메서드를 직접 만들 수 없다 (컴파일러가 예약).

### 8-3. 직접 구현한 속성 (필드 + 로직)
```csharp
private int speed;
public int Speed
{
    get => speed;
    set { if (value < 0) throw new ArgumentOutOfRangeException(); speed = value; }
}
```
→ 컴파일 후
```csharp
private int speed;                                   // 내가 선언한 필드 그대로

public int get_Speed() { return speed; }
public void set_Speed(int value)
{
    if (value < 0) throw new ArgumentOutOfRangeException();
    speed = value;
}
```
- 백킹 필드를 컴파일러가 만들지 않는다. 내가 선언한 필드를 그대로 사용한다.

### 8-4. 계산 속성 (백킹 필드 없음)
```csharp
public double Area => Width * Height;
```
→ 컴파일 후: 필드는 생기지 않고 `get_Area()` 메서드만 생긴다.
```csharp
public double get_Area() { return Width * Height; }
```

### 8-5. get-only 자동 속성
```csharp
public string Name { get; }
```
→ 컴파일 후: 백킹 필드가 **`initonly`(readonly)** 로 생성되고 `get_Name()`만 존재.
```
.field private initonly string '<Name>k__BackingField'
```

### 8-6. init 접근자
```csharp
public string Name { get; init; }
```
→ 컴파일 후: `set_Name` 대신 **`init` 접근자**가 생기며, 반환 타입에 `modreq(IsExternalInit)` 표시가 붙는다. 이 표시를 보고 컴파일러가 "객체 초기화 시점에만 호출 가능"으로 검사한다. (런타임이 강제하는 것이 아니라 **컴파일러 수준의 규칙**이며, 리플렉션으로는 호출 가능하다.)

```
.method public hidebysig specialname instance void modreq(System.Runtime.CompilerServices.IsExternalInit)
        set_Name(string 'value') cil managed
```

### 8-7. 인덱서
```csharp
public ushort this[int address] { get => regs[address]; set => regs[address] = value; }
```
→ 컴파일 후: **`get_Item(int)` / `set_Item(int, ushort)`** 메서드로 변환 (속성 이름은 기본 `Item`).

### 8-8. 자동 속성의 초기화자
```csharp
public int Timeout { get; set; } = 3000;
```
→ 컴파일 후: **생성자 맨 앞**에서 백킹 필드에 대입하는 코드로 바뀐다.
```csharp
public Device()
{
    <Timeout>k__BackingField = 3000;   // 기본 생성자에 삽입
    base..ctor();
}
```

---

## 9. 성능

- 속성은 메서드 호출이지만, 단순 get/set은 **JIT가 인라이닝**(호출을 본문으로 치환)한다. 자동 구현 속성은 필드 직접 접근과 사실상 **성능 차이가 없다.**
- 인라이닝이 잘 되는 조건: 본문이 짧고, 예외 throw나 복잡한 분기가 없을 것.
- 주의할 점:
  - 속성 get에 **무거운 연산**(파일 읽기, DB 조회, 루프)을 넣지 않는다. 호출하는 쪽은 필드처럼 싸다고 가정한다.
  - `List<T>`를 반환하는 계산 속성(`=> new List<T>()`)은 **접근할 때마다 할당**한다.
  - 값 타입(struct)을 반환하는 속성은 **복사본**이 반환되므로 `obj.Point.X = 5` 같은 수정이 반영되지 않는다 (컴파일 에러로 막힘).

```csharp
public Point Location { get; set; }      // struct Point

// d.Location.X = 10;                    // 컴파일 에러 CS1612: 복사본을 수정하게 되므로
var p = d.Location; p.X = 10; d.Location = p;    // 꺼내서 수정 후 다시 대입
```

---

## 10. 제약 사항과 주의점

1. **`ref` / `out` 인자로 전달 불가**: 속성은 메모리 주소가 없다.
   ```csharp
   // int.TryParse(text, out obj.Value);   // 컴파일 에러
   int.TryParse(text, out int v); obj.Value = v;
   ```
2. **`volatile` 적용 불가** (자동 구현 속성의 백킹 필드에도 불가): 직접 필드를 선언해야 한다.
3. **`Interlocked` 사용 불가**: 필드에만 `ref`로 전달할 수 있다.
4. **스레드 안전성은 별개**: 속성 자체는 어떤 동기화도 제공하지 않는다. 다중 스레드 접근이면 `lock` / `volatile` 필드 / `Interlocked`를 별도로 고려한다.
5. **getter에 부수 효과를 넣지 말 것**: 디버거가 속성을 자동 평가(watch, 툴팁)할 때 상태가 바뀔 수 있다.
6. **getter에서 예외를 던지지 말 것**: 필드처럼 보이는 문법이라 호출하는 쪽이 예외를 기대하지 않는다.
7. **속성 이름과 필드 이름 혼동 주의**: `store` / `Store`처럼 대소문자만 다른 이름은 오타로 엉뚱한 쪽을 참조하기 쉽다. (`_store` 접두사 관례로 구분하기도 한다.)
8. **`{ get; }` 컬렉션 속성도 내용은 수정 가능**: 참조만 못 바꾸는 것이지 내부 `Add`는 가능하다.
9. **직렬화/바인딩 라이브러리는 대개 public 속성만 대상**: public 필드만 있으면 누락될 수 있다.

---

## 11. 역사 (C# 버전별 변화)

| 버전 | 연도 | 추가된 것 |
|---|---|---|
| C# 1.0 | 2002 | 속성 (get/set, 완전 구현) |
| C# 2.0 | 2005 | get과 set에 **서로 다른 접근 제한자** (`public get; private set;`) |
| C# 3.0 | 2007 | **자동 구현 속성** (`{ get; set; }`), 객체 초기화자 |
| C# 6.0 | 2015 | **get-only 자동 속성**, **자동 속성 초기화자**, `=>` 표현식 본문 속성 |
| C# 7.0 | 2017 | get/set 접근자 단위의 표현식 본문 |
| C# 9.0 | 2020 | **`init` 접근자**, record |
| C# 11 | 2022 | **`required` 멤버** |
| C# 14 | 2025 | **`field` 키워드**: 자동 구현 속성의 백킹 필드를 접근자 안에서 `field`로 직접 참조 (아래) |

### C# 14 `field` 키워드
```csharp
public string Name
{
    get;
    set => field = value?.Trim() ?? "";    // 별도 필드 선언 없이 숨은 백킹 필드를 직접 사용
}
```
- 자동 구현 속성의 편의성을 유지하면서, 한쪽 접근자에만 로직을 넣고 싶을 때 별도 필드 선언을 생략할 수 있게 해준다.
- 사용하려면 C# 14 이상을 지원하는 컴파일러(.NET 10 SDK)와 `LangVersion` 설정이 필요하다. 사용 중인 프로젝트의 .NET/C# 버전을 먼저 확인할 것. (예: .NET Framework 4.x 프로젝트는 기본 언어 버전이 낮다.)

> 참고: 회사 프로젝트가 .NET Framework 기반이라면 사용 가능한 C# 버전이 제한되어 `init`, `required`, `field` 등은 쓰지 못할 수 있다.

---

## 12. 필드와 속성을 같이 선언했을 때의 함정

```csharp
private RegisterStore store;
public RegisterStore Store { get; set; }
```

| 상황 | 결과 |
|---|---|
| 클래스 안에서 `store`를 **어디에도 안 씀** | 첫 줄을 빼도 동작 동일 (미사용 필드 경고만 사라짐) |
| 클래스 안에서 `store.Something()`처럼 **필드를 직접 사용** | 외부에서 `Store`에 값을 넣어도 `store`는 `null` → `NullReferenceException` 위험 |

**의도별 올바른 작성**
```csharp
// (1) 단순히 값을 담고 노출 → 속성만
public RegisterStore Store { get; set; }

// (2) 필드와 속성을 연결하고 싶다 → 속성이 필드를 사용하도록 직접 구현
private RegisterStore store;
public RegisterStore Store
{
    get => store;
    set => store = value;
}

// (3) 스레드 간 플래그 → volatile 필드 + 읽기 전용 속성
private volatile bool running;
public bool Running => running;
```

---

## 13. 요약

- **속성 = 필드처럼 보이는 get/set 메서드 묶음.** 컴파일하면 `get_X()` / `set_X()` 메서드가 된다.
- **자동 구현 속성**은 컴파일러가 `<X>k__BackingField`라는 숨은 필드를 만들어 주는 것이다. 내가 따로 선언한 필드와는 **연결되지 않는다.**
- 단순 노출은 자동 구현 속성, 로직이 필요하면 직접 필드 + 속성 구현.
- 읽기 전용은 `{ get; }`, 초기화 시점에만 쓰기는 `{ get; init; }`, 클래스 내부에서만 쓰기는 `{ get; private set; }`.
- `ref`/`out`, `volatile`, `Interlocked`는 **속성에 쓸 수 없고 필드에만** 쓸 수 있다.
- 속성은 대부분 JIT가 인라이닝하므로 필드 직접 접근과 성능 차이가 거의 없다. 다만 getter에 무거운 작업을 넣지 않는다.
- 필드 → 속성 변경은 소스 호환은 되지만 **이진 호환은 깨지므로**, public 멤버는 처음부터 속성으로 설계한다.

---

## 14. 자동 구현 속성 vs 수동 구현 속성: 사용 시점과 예시

- **자동 구현 속성**: `public int Port { get; set; }` (컴파일러가 숨은 백킹 필드를 만들어 줌)
- **수동 구현 속성(필드 연결)**: `private int port;` + `public int Port { get => port; set => port = value; }` (내가 선언한 필드를 속성이 직접 사용)

### 14-1. 판단 기준 한눈에

| 상황 | 선택 |
|---|---|
| 값을 그냥 담고 꺼내기만 한다 | **자동 구현** |
| 생성자에서 한 번 받고 바뀌지 않는다 (`{ get; }`) | **자동 구현** |
| 내부에서만 바꾸고 외부는 읽기만 한다 (`{ get; private set; }`) | **자동 구현** (단, 다른 스레드가 읽는 값이면 수동 검토) |
| 설정/DTO(Data Transfer Object), JSON 직렬화, DataGridView 바인딩용 모델 | **자동 구현** |
| setter에서 **검증 / 정규화 / 값 변환**이 필요하다 | **수동 구현** |
| 값이 바뀔 때 **변경 알림**(`INotifyPropertyChanged`)이 필요하다 | **수동 구현** |
| 스레드 간 플래그라서 **`volatile`** 이 필요하다 | **수동 구현** |
| **`Interlocked`** 로 원자적 증가/교체가 필요하다 | **수동 구현** |
| **`lock`** 으로 여러 값을 묶어 일관되게 읽고 써야 한다 | **수동 구현** |
| 필드를 **`ref` / `out`** 으로 넘겨야 한다 | **수동 구현** |
| 다른 객체의 값에 **위임**한다 (자체 저장 공간 없음) | **수동 구현** (필드 없이 get/set만 직접 작성) |
| **지연 초기화**(처음 접근할 때 생성)가 필요하다 | **수동 구현** |
| 내부 컬렉션을 **읽기 전용 뷰**로만 노출한다 | **수동 구현** (필드 + `IReadOnlyList<T>` 속성) |

### 14-2. 자동 구현 속성을 사용해야 하는 경우

#### (1) 설정 / DTO: 값을 담아서 전달만 하는 클래스
```csharp
public class ServerConfig
{
    public string IpAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 502;
    public int ReceiveTimeout { get; set; } = 3000;
}
```
- 로직이 없으므로 필드를 따로 선언할 이유가 없다.
- JSON 직렬화, PropertyGrid 표시 등 도구가 **public 속성 기준**으로 동작한다.

#### (2) 생성자로 주입받고 바뀌지 않는 의존성 (get-only)
```csharp
public class ModbusTcpSlave
{
    public RegisterStore Store { get; }          // 백킹 필드는 readonly로 생성됨

    public ModbusTcpSlave(RegisterStore store)
    {
        Store = store;
    }
}
```
- 생성 이후 교체되지 않는 참조는 `{ get; }`이 가장 간결하고 안전하다.
- 인터페이스 구현도 마찬가지다. `IDevice.Name { get; }`을 `public string Name { get; }`로 구현한다.

#### (3) 내부에서만 갱신하고 외부는 읽기만 하는 상태 (private set)
```csharp
public class InspectionSession
{
    public DateTime StartedAt { get; private set; }
    public string LastError { get; private set; }

    public void Start()
    {
        StartedAt = DateTime.Now;
        LastError = null;
    }
}
```
- 한 스레드(예: UI 스레드)에서만 쓰고 읽는 값이면 이것으로 충분하다.
- 다른 스레드가 반복해서 읽는 플래그라면 14-3의 (3)을 본다.

#### (4) 이벤트 콜백 / 로그 델리게이트
```csharp
public Action<string> Log { get; set; }

// 사용
Log?.Invoke($"Accept error: {e.Message}");
```

#### (5) DataGridView 바인딩 / JSON 모델
```csharp
public class InspectionResult
{
    public int Id { get; set; }
    public string Result { get; set; }
    public DateTime Timestamp { get; set; }
}

grid.DataSource = new BindingList<InspectionResult>(list);
```
- DataGridView는 **속성만** 컬럼으로 인식한다. public 필드로 만들면 컬럼이 생기지 않는다.

### 14-3. 수동 구현 속성(필드 연결)을 사용해야 하는 경우

#### (1) setter에서 검증이 필요할 때
```csharp
private int port = 502;

public int Port
{
    get => port;
    set
    {
        if (value < 1 || value > 65535)
            throw new ArgumentOutOfRangeException(nameof(value));
        port = value;
    }
}
```
- 잘못된 값이 객체 안으로 들어오지 못하게 막는다.

#### (2) 변경 알림 (WinForms / WPF 데이터 바인딩)
```csharp
public class DeviceViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private string status;
    public string Status
    {
        get => status;
        set
        {
            if (status == value) return;                 // 같은 값이면 알림 생략
            status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }
}
```
- 값을 **이전 값과 비교하고, 바뀐 시점에 이벤트를 발생**시켜야 하므로 필드가 필요하다.

#### (3) 스레드 간 플래그: volatile
```csharp
private volatile bool running;
public bool IsRunning => running;

public void Start()
{
    running = true;
    // Accept 스레드 시작 ...
}

public void Stop()
{
    running = false;
    listener.Stop();        // 블로킹 중인 AcceptTcpClient를 깨움
}

private void AcceptLoop()
{
    while (running) { ... }   // 다른 스레드가 바꾼 값을 즉시 볼 수 있어야 함
}
```
- 자동 구현 속성의 백킹 필드에는 `volatile`을 붙일 수 없으므로 **필드를 직접 선언**해야 한다.
- `volatile`이 없으면 Accept 스레드가 `running`을 캐시된 값으로 읽어 종료 신호를 못 볼 수 있다.

#### (4) 원자적 카운터: Interlocked
```csharp
private long okCount;

public long OkCount => Interlocked.Read(ref okCount);

public void IncrementOk() => Interlocked.Increment(ref okCount);
```
- `Interlocked`는 `ref`로 **필드**를 받아야 하는데, 속성은 `ref`로 넘길 수 없다.
- 여러 워커 스레드가 동시에 증가시키는 카운터(예: OK/NG 검사 수)에 해당한다.
- `Interlocked.Read`는 32비트 프로세스에서 64비트 값을 안전하게 읽기 위해 쓴다.

#### (5) lock으로 여러 값을 일관되게 보호
```csharp
private readonly object sync = new object();
private Point position;                       // struct (X, Y가 한 쌍으로 읽히고 써져야 함)

public Point Position
{
    get { lock (sync) return position; }
    set { lock (sync) position = value; }
}
```
- 값 여러 개를 따로 읽으면 중간에 다른 스레드가 바꿔서 X는 이전 값, Y는 새 값이 되는 일이 생긴다. 접근자 안에서 `lock`을 걸려면 필드가 있어야 한다.

#### (6) 값 변환 / 스케일 (장비 원시값 ↔ 실제 단위)
```csharp
private short rawTemp;                        // 장비가 주는 원시값 (0.1 ℃ 단위 정수)

public double Temperature
{
    get => rawTemp * 0.1;
    set => rawTemp = (short)Math.Round(value * 10);
}
```
- Modbus 레지스터처럼 정수로 주고받는 값에 배율을 적용해 노출할 때 쓴다. 저장 형태(`short`)와 노출 형태(`double`)가 다르므로 자동 구현으로는 표현할 수 없다.

#### (7) 문자열 정규화
```csharp
private string name = "";

public string Name
{
    get => name;
    set => name = value?.Trim() ?? "";
}
```
- C# 14 이상이면 별도 필드 선언 없이 `set => field = value?.Trim() ?? "";`로 쓸 수 있다 (11장 참조).

#### (8) 다른 객체의 값에 위임 (자체 저장 공간 없음)
```csharp
private readonly TcpClient client;

public int ReceiveTimeout
{
    get => client.ReceiveTimeout;
    set => client.ReceiveTimeout = value;
}
```
- 값의 실제 저장 위치가 `TcpClient` 안에 있으므로 백킹 필드가 필요 없다. get/set을 직접 작성해서 연결한다.

#### (9) 필드를 ref / out 으로 사용해야 할 때
```csharp
private int value;

public bool TryParseValue(string text) => int.TryParse(text, out value);   // 필드는 out으로 전달 가능
public int Value => value;
```
- `int.TryParse(text, out obj.Value)`처럼 **속성은 `out`/`ref`로 넘길 수 없다.**

#### (10) 지연 초기화
```csharp
private RegisterStore store;
public RegisterStore Store => store ??= new RegisterStore();    // 처음 접근할 때 생성
```

#### (11) 내부 컬렉션을 읽기 전용으로 노출
```csharp
private readonly List<string> logs = new List<string>();
public IReadOnlyList<string> Logs => logs;
```
- `public List<string> Logs { get; } = new List<string>();`처럼 자동 구현으로 만들면 외부에서 `Logs.Add(...)`로 내용을 마음대로 바꿀 수 있다.

### 14-4. 처음엔 자동 구현으로 시작해도 되는가

- **된다.** 자동 구현 속성을 나중에 수동 구현 속성으로 바꿔도 사용하는 쪽 코드는 그대로다 (속성 → 속성이므로 소스 호환, 이진 호환 모두 유지).
- 반대로 **public 필드로 시작했다가 속성으로 바꾸면** 이진 호환이 깨진다 (2장, 8-2 참조). 그래서 public 멤버는 처음부터 속성으로 만들고, 로직이 필요해지는 시점에 수동 구현으로 바꾼다.

### 14-5. 판단 흐름

```
값을 그냥 담고 꺼내기만 하는가?
 ├ 예  → 자동 구현 속성
 │        (생성자에서만 설정 → { get; } / 내부에서만 변경 → { get; private set; })
 └ 아니오
     ├ 검증 / 정규화 / 값 변환 / 변경 알림이 필요   → 수동 구현 (필드 + 로직)
     ├ volatile / Interlocked / ref·out이 필요       → 수동 구현 (필드를 직접 선언)
     ├ lock으로 여러 값을 묶어 보호                  → 수동 구현
     ├ 다른 객체 값에 위임                           → 수동 구현 (필드 없이 get/set 직접 작성)
     ├ 지연 초기화                                   → 수동 구현 (필드 + ??=)
     └ 내부 컬렉션을 읽기 전용으로 노출              → 수동 구현 (필드 + IReadOnlyList)
```

### 14-6. 주의할 점

- **필드와 자동 구현 속성을 같이 선언하면 서로 연결되지 않는다** (4장, 12장). 필드가 필요해서 선언했다면 속성이 그 필드를 사용하도록 `get => 필드; set => 필드 = value;`로 직접 구현한다.
- **C# 14의 `field` 키워드**는 "한쪽 접근자에만 로직을 넣고 싶은데 필드를 따로 선언하기 번거로운" 경우(검증, 정규화)를 줄여 준다. 다만 `volatile`, `Interlocked`, `lock`처럼 필드를 명시적으로 다뤄야 하는 경우에는 여전히 필드를 직접 선언하는 쪽이 의도가 분명하다.
- **.NET Framework 프로젝트**라면 사용 가능한 C# 버전이 제한되어 `init`, `required`, `field` 등을 쓰지 못할 수 있다 (11장). 그 경우 이 장의 수동 구현 패턴이 표준 방법이다.
- **다른 스레드가 읽고 쓰는 값**은 `{ get; private set; }`만으로는 가시성이 보장되지 않는다. `volatile` / `Interlocked` / `lock` 중 하나로 보호한다.
