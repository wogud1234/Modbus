# IPv4-mapped IPv6 주소 (`::ffff:a.b.c.d`) 정리

## 1. 개요

`::ffff:192.168.0.10`은 **IPv4-매핑 IPv6 주소**다.
IPv6 형식 안에 IPv4 주소(`192.168.0.10`)를 담은 표기이며, 실제로는 같은 IPv4 호스트를 가리킨다.

## 2. 주소 구조

| 구간 | 크기 | 값 |
|---|---|---|
| 앞부분 | 80비트 | 전부 0 |
| 표시 필드 | 16비트 | `ffff` (IPv4 매핑 표시) |
| 뒷부분 | 32비트 | 실제 IPv4 주소 |

- 예약 대역: `::ffff:0:0/96`

## 3. 언제 이 형태로 보이나

서버가 **IPv6 소켓 하나를 듀얼스택 모드**로 열어두었을 때, IPv4 클라이언트가 접속하면 주소가 `::ffff:` 접두부와 함께 애플리케이션에 전달된다.

| 서버 소켓 | IPv4 클라이언트 접속 시 보이는 주소 |
|---|---|
| IPv4 전용 (`0.0.0.0` 바인딩) | `192.168.0.10` (그대로) |
| IPv6 듀얼스택 (`::` 바인딩, `DualMode=true`) | `::ffff:192.168.0.10` |
| IPv6 전용 (`IPV6_V6ONLY`) | IPv4 접속 불가 |

**중요:** 네트워크 선(wire)에 IPv6 패킷이 흐르는 것이 아니다. 실제 패킷은 IPv4이고, **OS 커널이 애플리케이션에 보여주는 표기만 바뀐다.**

## 4. 왜 이런 약속이 필요한가

- IPv6 소켓의 주소 구조체(`sockaddr_in6`)는 128비트 주소만 담을 수 있다.
- 듀얼스택 소켓 하나로 IPv4 클라이언트도 받으려면 32비트 IPv4 주소를 128비트 공간에 넣을 방법이 필요하다.
- 그래서 `::ffff:0:0/96` 대역을 "IPv4 주소를 담은 것"으로 예약했다.
- 애플리케이션은 접두부만 보고 원래 IPv4임을 구분할 수 있고, 커널은 이 주소로 send할 때 IPv4 패킷으로 변환해 내보낸다.

## 5. 표준 근거

| 문서 | 내용 |
|---|---|
| RFC 4291 | IPv6 주소 아키텍처. `::ffff:0:0/96`을 IPv4-mapped IPv6 address로 정의 |
| RFC 3493 | IPv6용 Basic Socket API. IPv6 소켓이 IPv4 클라이언트와 통신할 때 매핑 주소 사용을 규정 |

이 접두부는 **소켓 API 레벨의 표기 규약**이며, Windows / Linux / macOS 등 주요 OS가 모두 따른다. 따라서 .NET 같은 런타임에서도 동일하게 보인다.

## 6. .NET 예제

### 6.1 듀얼스택 리스너

```csharp
var listener = new TcpListener(IPAddress.IPv6Any, 5000);
listener.Server.DualMode = true;   // IPV6_V6ONLY 해제
listener.Start();

var client = listener.AcceptTcpClient();
var remote = (IPEndPoint)client.Client.RemoteEndPoint;
// IPv4 클라이언트라면 remote.Address는 ::ffff:192.168.0.10
```

- `new TcpListener(IPAddress.IPv6Any, port)`는 플랫폼/버전에 따라 DualMode 기본값이 다를 수 있으므로 **명시적으로 `DualMode = true`** 지정을 권장한다.
- `TcpListener.Create(port)`는 자동으로 듀얼스택을 구성한다.

### 6.2 매핑 주소 정규화

```csharp
IPAddress ip = IPAddress.Parse("::ffff:192.168.0.10");

bool mapped = ip.IsIPv4MappedToIPv6;   // true
IPAddress v4 = ip.MapToIPv4();         // 192.168.0.10
```

## 7. 실무 주의점

- IP 화이트리스트, 장비 IP 매칭, 로그 비교 시 **`MapToIPv4()`로 먼저 정규화**해야 한다.
- 정규화하지 않으면 `"192.168.0.10" != "::ffff:192.168.0.10"` 문자열 불일치로 원인 찾기 어려운 버그가 생긴다.
- `192.168.x.x`는 사설 IP 대역이므로 같은 내부 네트워크(LAN) 장비에서 접속한 것이다.

## 8. 한 줄 요약

> 듀얼스택 IPv6 소켓에서 IPv4 클라이언트는 `::ffff:` + IPv4 형태로 보이며, 이는 RFC로 정해진 소켓 API 표기 규약이다. 비교·필터링 전에는 `MapToIPv4()`로 정규화하자.
