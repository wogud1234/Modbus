using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Server.Map;
using Server.Mmf;
using Server.Register;

namespace Server.Config
{
  public static class MesHostParameterLoader
  {
    public const string FileName = "Mes.Host.params.json";
    public const int SupportedSchemaVersion = 1;

    public static MesHostParams LoadOrDefault(string explicitPath, string exeDirectory)
    {
      string path = ResolvePath(explicitPath, exeDirectory);
      // path = "D:\.net\ModSlave\setup\Parameter\Mes.Host.params.json" 
      
      // 1. 설정 파일 있으면 설정 파일로 객체 만들어서 반환
      if (path != null)
      {
        MesHostParams loaded = LoadFromFile(path);
        Validate(loaded);
        loaded.SourcePath = path;
        return loaded;
      }

      // 2. 설정 파일 없으면 기본값으로 객체 만들어서 반환
      var fallback = CreateDefaults();
      Validate(fallback);
      return fallback;
    }

    private static MesHostParams CreateDefaults()
    {
      return new MesHostParams
      {
        SchemaVersion = SupportedSchemaVersion,
        Profile = "DongwonNsCan08",
        WriterMode = "VisionLed",
        Modbus = new MesModbusParams
        {
          ListenIp = "127.0.0.1",
          Port = 8010,
          UnitId = 1,
          WordOrder = "ABCD",
          AllowWriteCoils = new[] { "Data_CMD" },
          AllowMasterIps = new[] { "127.0.0.1", "::1" }
        },
        Bridge = new MesBridgeParams
        {
          PollMs = 50,
          HeartbeatIntervalMs = 1000,
          HeartbeatPeers = "Both",
          AllowControlCounterOverwrite = false,
          ApplyControlLineState = true,
          ApplyVisionWorkCounters = true,
          ApplyVisionNgPart = true,
          RecalcPercentOnUpdate = true
        },
        Mmf = new MesMmfParams
        {
          HeartbeatByteSize = MesMmfDefine.HeartbeatByteSize,
          InterfaceByteSize = MesMmfDefine.InterfaceByteSize,
          Pairs = new MesMmfPairsParams
          {
            Vision = new MesMmfPairNames
            {
              AppHeartbeat = MesMmfDefine.MmfVisionHeartbeat,
              AppInterface = MesMmfDefine.MmfVisionInterface,
              HostHeartbeat = MesMmfDefine.MmfMesTowardVisionHeartbeat,
              HostInterface = MesMmfDefine.MmfMesTowardVisionInterface
            },
            Control = new MesMmfPairNames
            {
              AppHeartbeat = MesMmfDefine.MmfControlHeartbeat,
              AppInterface = MesMmfDefine.MmfControlInterface,
              HostHeartbeat = MesMmfDefine.MmfMesTowardControlHeartbeat,
              HostInterface = MesMmfDefine.MmfMesTowardControlInterface
            }
          }
        },
        Map = new MesMapParams
        {
          CoilCount = Addresses.CoilCount,
          HoldingCount = Addresses.HoldingCount,
          NgParts = new MesNgPartsParams
          {
            Enabled = true,
            Role = "VisionNgPart",
            DataType = "DWord",
            StartAddress = 400017,
            Stride = 2,
            Zones = new[] { "Inner1", "Inner2", "Outer" },
            Types = new[] { "얼룩", "긁힘", "찍힘" }
          },
          SideSlots = new MesSideSlotsParams
          {
            Cameras = 3,
            RoiPerCamera = 4,
            CoilStartAddress = 14,
            NgPartStartAddress = 400043,
            Types = new[] { "얼룩", "긁힘", "찍힘" }
          }
        }
      };
    }

    private static void Validate(MesHostParams p)
    {
      ValidateForP1(p);
      ValidateMmf(p);
      ValidateMap(p);
    }

    private static void ValidateMap(MesHostParams p)
    {
      if (p == null)
        return;
      // S0 전개 가능 여부
      MesNgPartLayout.ExpandDongwonS0(p);
    }

    private static void ValidateMmf(MesHostParams p)
    {
      if (p == null)
        throw new ArgumentNullException("p");
      if (p.Mmf == null)
        throw new InvalidOperationException("mmf section is required");

      if (p.Mmf.HeartbeatByteSize != MesMmfDefine.HeartbeatByteSize)
        throw new InvalidOperationException(
          "mmf.heartbeatByteSize=" + p.Mmf.HeartbeatByteSize
                                   + " != code " + MesMmfDefine.HeartbeatByteSize);

      if (p.Mmf.InterfaceByteSize != MesMmfDefine.InterfaceByteSize)
        throw new InvalidOperationException(
          "mmf.interfaceByteSize=" + p.Mmf.InterfaceByteSize
                                   + " != code " + MesMmfDefine.InterfaceByteSize);

      if (p.Mmf.Pairs == null)
        throw new InvalidOperationException("mmf.pairs is required (at least one of vision/control)");

      var vision = p.Mmf.Pairs.Vision;
      var control = p.Mmf.Pairs.Control;
      var hasVision = vision != null;
      var hasControl = control != null;
      if (!hasVision && !hasControl)
        throw new InvalidOperationException("mmf.pairs empty — need vision and/or control");

      if (hasVision) ValidatePairNames("vision", vision);
      if (hasControl) ValidatePairNames("control", control);

      var mode = ParseWriterMode(p.WriterMode);
      switch (mode)
      {
        case MesWriterMode.VisionLed:
          if (!hasVision)
            throw new InvalidOperationException("writerMode=VisionLed requires mmf.pairs.vision");
          break;
        case MesWriterMode.ControlLed:
          if (!hasControl)
            throw new InvalidOperationException("writerMode=ControlLed requires mmf.pairs.control");
          break;
        case MesWriterMode.Hybrid:
          if (!hasVision || !hasControl)
            throw new InvalidOperationException("writerMode=Hybrid requires mmf.pairs.vision and control");
          break;
      }

      RejectControlIfNameCollision(vision, control);
    }

    private static void RejectControlIfNameCollision(MesMmfPairNames vision, MesMmfPairNames control)
    {
      var forbidden = new[]
      {
        "Vision",
        "Control",
        "Vision IF",
        "Control IF"
      };

      RejectForbidden("vision", vision, forbidden);
      RejectForbidden("control", control, forbidden);
    }

    private static void RejectForbidden(string key, MesMmfPairNames pair, string[] forbidden)
    {
      if (pair == null)
        return;

      RejectOne(key, "appHeartbeat", pair.AppHeartbeat, forbidden);
      RejectOne(key, "appInterface", pair.AppInterface, forbidden);
      RejectOne(key, "hostHeartbeat", pair.HostHeartbeat, forbidden);
      RejectOne(key, "hostInterface", pair.HostInterface, forbidden);
    }

    private static void RejectOne(string key, string field, string value, string[] forbidden)
    {
      if (string.IsNullOrWhiteSpace(value))
        return;
      for (var i = 0; i < forbidden.Length; i++)
        if (string.Equals(value, forbidden[i], StringComparison.Ordinal))
          throw new InvalidOperationException("mmf.pairs." + key + "." + field + "='" + value + "' collides with Control IF MMF name");
    }

    public static bool HasVisionPair(MesHostParams p)
    {
      return p != null && p.Mmf != null && p.Mmf.Pairs != null && p.Mmf.Pairs.Vision != null;
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public static bool HasControlPair(MesHostParams p)
    {
      return p != null && p.Mmf != null && p.Mmf.Pairs != null && p.Mmf.Pairs.Control != null;
    }
    
    /*
      [CreateDefault() vs ToModbusConfig(host)]
      ┌──────────┬──────────────────────────────────────────────┬──────────────────────────────────────────┐   
      │          │               CreateDefaults()               │           ToModbusConfig(host)           │   
      ├──────────┼──────────────────────────────────────────────┼──────────────────────────────────────────┤   
      │ 하는 일   │ 설정 파일 전체의 기본값을 생성                    │ 이미 있는 설정에서 Modbus 부분만 꺼내 변환     │   
      ├──────────┼──────────────────────────────────────────────┼──────────────────────────────────────────┤   
      │ 입력     │ 없음                                           │ MesHostParams host                       │   
      ├──────────┼──────────────────────────────────────────────┼──────────────────────────────────────────┤   
      │ 출력      │ MesHostParams (JSON (JavaScript Object       │ ModbusConfig (실제 Modbus 서버가 쓰는      │   
      │          │ Notation) 파일과 같은 모양의 원본 설정)           │ 실행용 설정)                               │   
      ├──────────┼──────────────────────────────────────────────┼──────────────────────────────────────────┤   
      │ 다루는    │ Modbus, Bridge, MMF (Memory-Mapped File),    │ Modbus 섹션만                             │   
      │ 범위      │ Map 전부                                     │                                          │   
      ├──────────┼──────────────────────────────────────────────┼──────────────────────────────────────────┤   
      │ 값의      │ 문자열 그대로 ("ABCD", "Data_CMD")              │ 실제 타입으로 해석된 값 (WordOrder.ABCD      │   
      │ 형태      │                                              │ enum, 코일 주소 ushort[])                  │   
      ├──────────┼──────────────────────────────────────────────┼──────────────────────────────────────────┤   
      │ 접근      │ private (Loader 내부에서만 사용)                │ public (외부에서 호출)                     │   
      │ 제한      │                                              │                                          │   
      └──────────┴──────────────────────────────────────────────┴──────────────────────────────────────────┘   
      흐름으로 보면                                                                                            
        JSON 파일 있음 ──→ LoadFromFile() ──┐                                                                    
                                            ├─→ MesHostParams ──→ ToModbusConfig() ──→ ModbusConfig              
        JSON 파일 없음 ──→ CreateDefaults() ┘                     (Modbus 부분만 변환)                           
                                                                                                                 
      - CreateDefaults()는 **"파일이 없을 때 쓸 가짜 JSON"**을 코드로 만들어 둔 것입니다. LoadFromFile()을 대신하는 역할이에요.                                                                                   
      - ToModbusConfig()는 그다음 단계입니다. 파일에서 읽었든 기본값이든, 
        어디서 온 MesHostParams든 받아서 Modbus 서버가 바로 쓸 수 있는 형태로 바꿉니다.                                                                                        
    */
    public static ModbusConfig ToModbusConfig(MesHostParams host)
    {
      ModbusConfig config = ModbusConfig.CreateLocalTest();
      
      // 1. 초기화된 MesHostParams 객체에 Modbus 필드가 비어있으면,
      //    기본값(로컬 테스트용)으로 세팅한 Modbus 객체(AllowMasterIps만 채워져 있음) 반환.  
      if (host == null || host.Modbus == null)
        return config;

      // 2. 초기화된 MesHostParams 객체에 Modbus 필드에 값이 할당되어 있으면,
      //    나머지 Modbus 객체의 필드들 채워서 반환.
      var m = host.Modbus;
      if (!string.IsNullOrWhiteSpace(m.ListenIp))
        config.ListenIp = m.ListenIp;
      if (m.Port > 0)
        config.Port = m.Port;
      if (m.UnitId >= 0)
        config.UnitId = (byte)m.UnitId;
      config.WordOrder = ParseWordOrder(m.WordOrder);
      config.AllowWriteCoilOffsets = ResolveAllowWriteCoils(m.AllowWriteCoils);
      if (m.AllowMasterIps != null)
        config.AllowMasterIps = (string[])m.AllowMasterIps.Clone();
      return config;
    }
    
    // 코일 이름 문자열을 실제 코일 주소(ushort)로 바꿔서 배열로 반환
    public static ushort[] ResolveAllowWriteCoils(string[] names)
    {
      if (names == null || names.Length == 0)
        return new[] { Addresses.CoilDataCmd, Addresses.CoilWorkClearCmd };

      var list = new List<ushort>();
      for (var i = 0; i < names.Length; i++)
      {
        var n = names[i];
        if (string.IsNullOrWhiteSpace(n))
          continue;
        if (string.Equals(n, "Data_CMD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n, "DataCmd", StringComparison.OrdinalIgnoreCase))
        {
          list.Add(Addresses.CoilDataCmd);
          continue;
        }

        if (string.Equals(n, "WorkClear_CMD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n, "WorkClearCmd", StringComparison.OrdinalIgnoreCase))
        {
          list.Add(Addresses.CoilWorkClearCmd);
          continue;
        }

        throw new InvalidOperationException("unknown allowWriteCoils entry: " + n);
      }

      if (list.Count == 0)
        list.Add(Addresses.CoilDataCmd);
      if (!list.Contains(Addresses.CoilWorkClearCmd))
        list.Add(Addresses.CoilWorkClearCmd);
      return list.ToArray();
    }
    
    private static void ValidatePairNames(string key, MesMmfPairNames pair)
    {
      if (!pair.HasAllNames())
        throw new InvalidOperationException("mmf.pairs." + key + " requires app/host Heartbeat+Interface names");
    }

    private static void ValidateForP1(MesHostParams p)
    {
      if (p == null)
        throw new ArgumentNullException("p");
      
      if(p.SchemaVersion != 0 && p.SchemaVersion != SupportedSchemaVersion)
        throw new InvalidOperationException("unsupported schemaVersion=" + p.SchemaVersion + " (expected " + SupportedSchemaVersion + ")");

      ParseWriterMode(p.WriterMode);

      if (p.Modbus != null)
      {
        if (p.Modbus.Port < 1 || p.Modbus.Port > 65535)
          throw new InvalidOperationException("modbus.port out of range: " + p.Modbus.Port);
        if (p.Modbus.UnitId < 0 || p.Modbus.UnitId > 255)
          throw new InvalidOperationException("modbus.unitId out of range: " + p.Modbus.UnitId);
        ParseWordOrder(p.Modbus.WordOrder);
      }
      
      if(p.Bridge != null && !string.IsNullOrWhiteSpace(p.Bridge.HeartbeatPeers))
        ParseHeartbeatPeers(p.Bridge.HeartbeatPeers);
    }

    public static MesHeartbeatPeers ParseHeartbeatPeers(string raw)
    {
      if (string.IsNullOrWhiteSpace(raw))
        return MesHeartbeatPeers.Both;

      if (string.Equals(raw, "Both", StringComparison.OrdinalIgnoreCase)) return MesHeartbeatPeers.Both;
      if (string.Equals(raw, "VisionOnly", StringComparison.OrdinalIgnoreCase)) return MesHeartbeatPeers.VisionOnly;
      if (string.Equals(raw, "ControlOnly", StringComparison.OrdinalIgnoreCase)) return MesHeartbeatPeers.ControlOnly;
      if (string.Equals(raw, "Any", StringComparison.OrdinalIgnoreCase)) return MesHeartbeatPeers.Any;
      if (string.Equals(raw, "Always", StringComparison.OrdinalIgnoreCase)) return MesHeartbeatPeers.Always;

      throw new InvalidOperationException("unknown heartbeatPeers: " + raw);
    }

    private static WordOrder ParseWordOrder(string raw)
    {
      if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "ABCD", StringComparison.OrdinalIgnoreCase))
        return WordOrder.ABCD;
      if (string.Equals(raw, "CDAB", StringComparison.OrdinalIgnoreCase))
        return WordOrder.CDAB;

      throw new InvalidOperationException("unknown wordOrder: " + raw);
    }

    public static MesWriterMode ParseWriterMode(string raw)
    {
      if (string.IsNullOrWhiteSpace(raw)) return MesWriterMode.VisionLed;
      if (string.Equals(raw, "VisionLed", StringComparison.OrdinalIgnoreCase)) return MesWriterMode.VisionLed;
      if (string.Equals(raw, "ControlLed", StringComparison.OrdinalIgnoreCase)) return MesWriterMode.ControlLed;
      if (string.Equals(raw, "Hybrid", StringComparison.OrdinalIgnoreCase)) return MesWriterMode.Hybrid;
      throw new InvalidOperationException("unknown writerMode: " + raw);
    }

    private static MesHostParams LoadFromFile(string path)
    {
      var json = File.ReadAllText(path, Encoding.UTF8);
      return Deserialize(json);
    }

    private static MesHostParams Deserialize(string json)
    {
      if(string.IsNullOrWhiteSpace(json)) 
        throw new InvalidOperationException("params JSON is empty");

      using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
      {
        var ser = new DataContractJsonSerializer(typeof(MesHostParams));
        var p = ser.ReadObject(ms) as MesHostParams;
        if(p == null)
          throw new InvalidOperationException("params JSON deserialize failed");
        return p;
      }
    }

    private static string ResolvePath(string explicitPath, string exeDirectory)
    {
      // 1. 사용자가 지정한 경로 or \setup\Parameter\Mes.Host.params.json 를 찾아서 있으면 반환
      if (!string.IsNullOrWhiteSpace(explicitPath))
      {
        if(!File.Exists(explicitPath))
          throw new FileNotFoundException("params file not found: " + explicitPath, explicitPath);
        return Path.GetFullPath(explicitPath);
      }

      // 2. 1번에서 못찾으면 실행 폴더에서 찾아서 있으면 반환
      if (!string.IsNullOrWhiteSpace(exeDirectory))
      {
        var local = Path.Combine(exeDirectory, FileName);
        if(File.Exists(local))
          return Path.GetFullPath(local);
      }

      // 3. 1,2번에서 못 찾으면 ProgramData\Mes\.. 에서 찾아서 있으면 반환
      var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
      if (!string.IsNullOrWhiteSpace(programData))
      {
        var shared = Path.Combine(programData, "Mes", FileName);
        if(File.Exists(shared))
          return Path.GetFullPath(shared);
      }

      return null;
    }
  }
}