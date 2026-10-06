using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Server.Config
{
  // NscanMes.Host.params.json 루트 DTO.
  [DataContract]
  public class MesHostParams
  {
    [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
    [DataMember(Name = "profile")] public string Profile { get; set; }
    [DataMember(Name = "writerMode")] public string WriterMode { get; set; }
    [DataMember(Name = "modbus")] public MesModbusParams Modbus { get; set; }
    [DataMember(Name = "bridge")] public MesBridgeParams Bridge { get; set; }
    [DataMember(Name = "mmf")] public MesMmfParams Mmf { get; set; }
    [DataMember(Name = "map")] public MesMapParams Map { get; set; }
    public string SourcePath { get; set; }
  }

  [DataContract]
  public sealed class MesModbusParams
  {
    [DataMember(Name = "listenIp")] public string ListenIp { get; set; }
    [DataMember(Name = "port")] public int Port { get; set; }
    [DataMember(Name = "unitId")] public int UnitId { get; set; }
    [DataMember(Name = "wordOrder")] public string WordOrder { get; set; }
    [DataMember(Name = "allowWriteCoils")] public string[] AllowWriteCoils { get; set; }
    [DataMember(Name = "allowMasterIps")] public string[] AllowMasterIps { get; set; }    
  }   

  [DataContract]
  public sealed class MesBridgeParams
  {
    [DataMember(Name = "pollMs")]                       public int? PollMs { get; set; }
    [DataMember(Name = "heartbeatIntervalMs")]          public int? HeartbeatIntervalMs { get; set; }
    [DataMember(Name = "heartbeatPeers")]               public string HeartbeatPeers { get; set; }
    [DataMember(Name = "allowControlCounterOverwrite")] public bool? AllowControlCounterOverwrite { get; set; }
    [DataMember(Name = "applyControlLineState")]        public bool? ApplyControlLineState { get; set; }
    [DataMember(Name = "applyVisionWorkCounters")]      public bool? ApplyVisionWorkCounters { get; set; }
    [DataMember(Name = "applyVisionNgPart")]            public bool? ApplyVisionNgPart { get; set; }
    [DataMember(Name = "recalcPercentOnUpdate")]        public bool? RecalcPercentOnUpdate { get; set; }
  }

  [DataContract]
  public sealed class MesMmfParams
  {
    [DataMember(Name = "heartbeatByteSize")] public int HeartbeatByteSize { get; set; }
    [DataMember(Name = "interfaceByteSize")] public int InterfaceByteSize { get; set; }
    [DataMember(Name = "pairs")] public MesMmfPairsParams Pairs { get; set; }
  }

  [DataContract]
  public sealed class MesMmfPairsParams
  {
    [DataMember(Name = "vision")]  public MesMmfPairNames Vision { get; set; }
    [DataMember(Name = "control")] public MesMmfPairNames Control { get; set; }
  }

  [DataContract]
  public sealed class MesMmfPairNames
  {
    [DataMember(Name = "appHeartbeat")] 
    public string AppHeartbeat { get; set; }    // app(Vision or Control)이 열어두는 heartbeat 블록 이름  →  MES가 읽음
    
    [DataMember(Name = "appInterface")] 
    public string AppInterface { get; set; }    // app(Vision or Control)이 열어두는 데이터 블록 이름     →  MES가 읽음
    
    [DataMember(Name = "hostHeartbeat")] 
    public string HostHeartbeat { get; set; }   // MES가 열어두는 heartbeat 블록 이름        →  app(Vision or Control)이 읽음
    
    [DataMember(Name = "hostInterface")] 
    public string HostInterface { get; set; }   // MES가 열어두는 데이터 블록 이름           →  app(Vision or Control)이 읽음

    public bool HasAllNames()
    {
      return !string.IsNullOrWhiteSpace(AppHeartbeat)
             && !string.IsNullOrWhiteSpace(AppInterface)
             && !string.IsNullOrWhiteSpace(HostHeartbeat)
             && !string.IsNullOrWhiteSpace(HostInterface);
    }
  }

  [DataContract]
  public sealed class MesMapParams
  {
    [DataMember(Name = "coilCount")] public int? CoilCount { get; set; }
    [DataMember(Name = "holdingCount")] public int? HoldingCount { get; set; }
    [DataMember(Name = "ngParts")] public MesNgPartsParams NgParts { get; set; }
    [DataMember(Name = "sideSlots")] public MesSideSlotsParams SideSlots { get; set; }
  }

  [DataContract]
  public sealed class MesNgPartsParams
  {
    [DataMember(Name = "enabled")] public bool Enabled { get; set; }
    [DataMember(Name = "role")] public string Role { get; set; }
    [DataMember(Name = "dataType")] public string DataType { get; set; }
    [DataMember(Name = "startAddress")] public int StartAddress { get; set; }
    [DataMember(Name = "stride")] public int Stride { get; set; }
    [DataMember(Name = "zones")] public string[] Zones { get; set; }
    [DataMember(Name = "types")] public string[] Types { get; set; }
    [DataMember(Name = "items")] public List<MesNgPartItemParams> Items { get; set; }
  }

  [DataContract]
  public sealed class MesNgPartItemParams
  {
    [DataMember(Name = "name")] public string Name { get; set; }
    [DataMember(Name = "address")] public int Address { get; set; }
    [DataMember(Name = "zone")] public string Zone { get; set; }
    [DataMember(Name = "classKey")] public string ClassKey { get; set; }
  }
  
  [DataContract]
  public sealed class MesSideSlotsParams
  {
    [DataMember(Name = "cameras")] public int Cameras { get; set; }
    [DataMember(Name = "roiPerCamera")] public int RoiPerCamera { get; set; }
    [DataMember(Name = "coilStartAddress")] public int CoilStartAddress { get; set; }
    [DataMember(Name = "ngPartStartAddress")] public int NgPartStartAddress { get; set; }
    [DataMember(Name = "types")] public string[] Types { get; set; }
  }
}