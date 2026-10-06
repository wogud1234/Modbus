namespace Server.Config
{
  /*
    MES (Manufacturing Execution System, 제조 실행 시스템) 브리지의 동작 설정을 담는 클래스입니다. 
    설정 데이터를 보관하고, 아래 두 가지 일도 합니다.                                                             
      1. 기본값을 만들고, 거기에 JSON (JavaScript Object Notation) 설정을 덮어써서 최종 설정 객체를 만듭니다   
         (팩토리 역할).                                                                                        
      2. 하트비트 조건을 판정합니다.
  */
  public class MesBridgeOptions
  {
    public MesWriterMode WriterMode { get; set; }             // 누가 MES 데이터를 주도적으로 쓰는지: VisionLed / ControlLed / Hybrid
    public int PollMs { get; set; }
    public int HeartbeatIntervalMs { get; set; }
    public MesHeartbeatPeers HeartbeatPeers { get; set; }     // 어느 쪽 장비가 살아 있어야 정상으로 볼지
    public bool AllowControlCounterOverwrite { get; set; }    // 제어 측이 카운터 값을 덮어쓸 수 있는지
    public bool ApplyControlLineState { get; set; }           // 제어 측의 라인 상태(가동/정지 등)를 반영할지
    public bool ApplyVisionWorkCounters { get; set; }         // 비전 측의 작업 수량 카운터를 반영할지
    public bool ApplyVisionNgPart { get; set; }               // 비전 측의 NG (No Good, 불량) 부품 정보를 반영할지
    public bool RecalcPercentOnUpdate { get; set; }           // 값이 갱신될 때 비율(%)을 다시 계산할지
    
    public static MesBridgeOptions CreateVisionLedDefaults()
    {
      return new MesBridgeOptions
      {
        WriterMode = MesWriterMode.VisionLed,
        PollMs = 50,
        HeartbeatIntervalMs = 1000,
        HeartbeatPeers = MesHeartbeatPeers.Both,
        AllowControlCounterOverwrite = false,
        ApplyControlLineState = true,
        ApplyVisionWorkCounters = true,
        ApplyVisionNgPart = true,
        RecalcPercentOnUpdate = true
      };
    }

    public static MesBridgeOptions Fromparams(MesHostParams host)
    {
      var mode = MesHostParameterLoader.ParseWriterMode(host?.WriterMode);
      
      // 1단계: WriterMode에 맞는 기본값(WriterMode에 따라 다른 기본값) 생성.
      var o = CreateDefaultsForMode(mode);
      var heartbeatPeersExplicit = false;
      
      //  2단계: JSON으로 덮어쓰기.
      if (host != null && host.Bridge != null)
      {
        var b = host.Bridge;
        if (b.PollMs.HasValue && b.PollMs.Value >= 10)
          o.PollMs = b.PollMs.Value;
        if (b.HeartbeatIntervalMs.HasValue && b.HeartbeatIntervalMs.Value >= 100)
          o.HeartbeatIntervalMs = b.HeartbeatIntervalMs.Value;
        if (!string.IsNullOrWhiteSpace(b.HeartbeatPeers))
        {
          o.HeartbeatPeers = MesHostParameterLoader.ParseHeartbeatPeers(b.HeartbeatPeers);
          heartbeatPeersExplicit = true;
        }

        if (b.AllowControlCounterOverwrite.HasValue)
          o.AllowControlCounterOverwrite = b.AllowControlCounterOverwrite.Value;
        if (b.ApplyControlLineState.HasValue)
          o.ApplyControlLineState = b.ApplyControlLineState.Value;
        if (b.ApplyVisionWorkCounters.HasValue)
          o.ApplyVisionWorkCounters = b.ApplyVisionWorkCounters.Value;
        if (b.ApplyVisionNgPart.HasValue)
          o.ApplyVisionNgPart = b.ApplyVisionNgPart.Value;
        if (b.RecalcPercentOnUpdate.HasValue)
          o.RecalcPercentOnUpdate = b.RecalcPercentOnUpdate.Value;
      }

      // 3단계: HeartbeatPeers 미설정이면 자동 추론
      if (!heartbeatPeersExplicit)
        o.HeartbeatPeers = InferHeartbeatPeersFromPairs(host, o.HeartbeatPeers);

      return o;
    }

    private static MesHeartbeatPeers InferHeartbeatPeersFromPairs(MesHostParams host, MesHeartbeatPeers fallback)
    {
      var hasV = MesHostParameterLoader.HasVisionPair(host);
      var hasC =  MesHostParameterLoader.HasControlPair(host);
      if (hasV && hasC)
        return MesHeartbeatPeers.Both;
      if (hasV)
        return MesHeartbeatPeers.VisionOnly;
      if (hasC)
        return MesHeartbeatPeers.ControlOnly;
      return fallback;
    }

    private static MesBridgeOptions CreateDefaultsForMode(MesWriterMode mode)
    {
      var o = CreateVisionLedDefaults();
      o.WriterMode = mode;

      switch (mode)
      {
        case MesWriterMode.ControlLed:
          o.HeartbeatPeers = MesHeartbeatPeers.ControlOnly;
          o.AllowControlCounterOverwrite = true;
          o.ApplyVisionWorkCounters = false;
          o.ApplyVisionNgPart = false;
          break;
        case MesWriterMode.Hybrid:
          o.HeartbeatPeers = MesHeartbeatPeers.Both;
          o.AllowControlCounterOverwrite = true;
          o.ApplyVisionWorkCounters = false;
          o.ApplyVisionNgPart = true;
          break;
      }
      return o;
    }
    
    // Vision/Control Alive 상태에 따라 Coil HEARTBEAT 토글 허용 여부.
    public bool IsHeartbeatPeerConditionMet(bool visionAlive, bool controlAlive)
    {
      switch (HeartbeatPeers)
      {
        case MesHeartbeatPeers.VisionOnly:
          return visionAlive;
        case MesHeartbeatPeers.ControlOnly:
          return controlAlive;
        case MesHeartbeatPeers.Any:
          return visionAlive || controlAlive;
        case MesHeartbeatPeers.Always:
          return true;
        case MesHeartbeatPeers.Both:
        default:
          return visionAlive && controlAlive;
      }
    }
  }
}