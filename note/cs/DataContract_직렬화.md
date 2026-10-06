# [DataContract] 직렬화 / 역직렬화

## 어트리뷰트 역할

| 어트리뷰트 | 대상 | 역할 |
|---|---|---|
| `[DataContract]` | 클래스 | "이 클래스는 직렬화 가능" 표시 |
| `[DataMember]` | 프로퍼티/필드 | "이 멤버를 JSON/XML에 포함" 표시 |

> `[DataContract]`만 붙이고 `[DataMember]`를 안 붙이면 아무 멤버도 직렬화되지 않는다.

---

## 예시 클래스

```csharp
using System.Runtime.Serialization;

[DataContract]
public sealed class MesHostParams
{
    [DataMember(Name = "host")]
    public string Host { get; set; }

    [DataMember(Name = "port")]
    public int Port { get; set; }

    [DataMember(Name = "timeout_ms")]
    public int TimeoutMs { get; set; }
}
```

---

## JSON ↔ 객체 구조도

```
[ NscanMes.Host.params.json ]          [ MesHostParams 객체 ]
                                         
{                                        MesHostParams
  "host"       : "192.168.0.10",   →      ├─ Host       = "192.168.0.10"
  "port"       : 502,              →      ├─ Port       = 502
  "timeout_ms" : 3000              →      └─ TimeoutMs  = 3000
}                                        

         역직렬화 (JSON → 객체)
         ────────────────────►
         직렬화   (객체 → JSON)
         ◄────────────────────
```

---

## 역직렬화 예시 (JSON 파일 → 객체)

```csharp
using System.IO;
using System.Runtime.Serialization.Json;

// JSON 파일을 읽어 MesHostParams 객체로 변환
var serializer = new DataContractJsonSerializer(typeof(MesHostParams));

using var stream = File.OpenRead("NscanMes.Host.params.json");
var param = (MesHostParams)serializer.ReadObject(stream);

Console.WriteLine(param.Host);       // "192.168.0.10"
Console.WriteLine(param.Port);       // 502
Console.WriteLine(param.TimeoutMs);  // 3000
```

> `DataContractJsonSerializer`는 `System.Runtime.Serialization` 어셈블리에 포함.  
> `.csproj`의 References에 `System.Runtime.Serialization`이 있어야 한다.

---

## 직렬화 예시 (객체 → JSON 파일)

```csharp
var param = new MesHostParams
{
    Host      = "192.168.0.10",
    Port      = 502,
    TimeoutMs = 3000
};

var serializer = new DataContractJsonSerializer(typeof(MesHostParams));

using var stream = File.Create("output.json");
serializer.WriteObject(stream, param);
// 결과: {"host":"192.168.0.10","port":502,"timeout_ms":3000}
```

---

## DataMember 주요 옵션

```csharp
[DataMember(Name = "host", IsRequired = true, Order = 0)]
public string Host { get; set; }
```

| 옵션 | 설명 |
|---|---|
| `Name` | JSON 키 이름 지정 (생략 시 프로퍼티 이름 그대로) |
| `IsRequired` | 역직렬화 시 해당 키가 없으면 예외 발생 |
| `Order` | 직렬화 순서 지정 |

---

## 흐름 요약

```
파일 시스템                  DataContractJsonSerializer          .NET 객체
─────────────────────────────────────────────────────────────────────────
NscanMes.Host.params.json
        │
        │  File.OpenRead()
        ▼
   FileStream  ──────────────  ReadObject(stream)  ──────────►  MesHostParams
                                                                  .Host
                                                                  .Port
                                                                  .TimeoutMs
```
