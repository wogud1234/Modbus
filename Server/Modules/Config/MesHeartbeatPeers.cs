namespace Server.Config
{
  public enum MesHeartbeatPeers
  {
    Both = 0,           // Vision + Control 둘 다 살아있어야 정상
    VisionOnly = 1,     // Vision만 살아있으면 정상 (Control은 체크 안 함)
    ControlOnly = 2,    // Control만 살아있으면 정상
    Any = 3,            // 둘 중 하나라도 살아있으면 정상
    Always = 4          // heartbeat 체크 자체를 안 함 — 항상 정상으로 간주
  }
}

/*
  "Heartbeat를 누구한테서 받아야 MES가 정상으로 판단하냐" 를 정하는 모드
*/