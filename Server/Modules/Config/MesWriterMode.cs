namespace Server.Config
{
  public enum MesWriterMode
  {
    VisionLed = 0,      // Vision 앱이 주도 — Vision MMF 채널에서 데이터를 읽어서 MES에 씀
    ControlLed = 1,     // Control 앱이 주도 — Control 채널에서 읽어서 씀
    Hybrid = 2          // Vision + Control 둘 다 사용
  }
}