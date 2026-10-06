using System.Collections.Generic;

namespace Server.Assign
{
  /// <summary>채널 1건 검사 스냅샷 (07 원천).</summary>
  public sealed class MesChannelResult
  {
    public MesChannelResult()
    {
      ClassName = string.Empty;
    }

    public MesChannelResult(MesInspChannel channel, bool isOk, string className)
    {
      Channel = channel;
      IsOk = isOk;
      ClassName = className ?? string.Empty;
    }

    public MesInspChannel Channel { get; set; }
    public bool IsOk { get; set; }

    /// <summary>DL/룰 클래스명 (예: 긁힘, 찍힘, 얼룩).</summary>
    public string ClassName { get; set; }
  }
  
  public sealed class MesCycleInput
  {
    public MesCycleInput()
    {
      VisionState = 1; // Run
      Channels = new List<MesChannelResult>();
    }

    public ushort CycleId { get; set; }
    public ushort VisionState { get; set; }
    public List<MesChannelResult> Channels { get; private set; }
  }

  /// <summary>MMF Publish / RegisterStore 반영용 08 집계 결과 (T1).</summary>
  public sealed class MesMap08Publish
  {
    public ushort CycleId { get; set; }
    public ushort VisionState { get; set; }
    public ushort ZoneNgBits { get; set; }
    public ushort ClassBitsSide { get; set; }
    public ushort ClassBitsBottom { get; set; }
    public ushort ClassBitsInside { get; set; }
    public ushort DeltaTotal { get; set; }
    public ushort DeltaOk { get; set; }
    public ushort DeltaNg1 { get; set; }
    public ushort DeltaRc { get; set; }
    public ushort DeltaNg2 { get; set; }
  }
}