namespace Server.Assign
{
  /// <summary>07 검사 채널 → 08 Zone 매핑용.</summary>
  public enum MesInspChannel
  {
    Rupture = 0,
    Height,
    Aperture,
    Inner1,
    Inner2,
    Outer,
    Side1,
    Side2,
    Side3
  }

  /// <summary>08 ngPart Zone.</summary>
  public enum MesMap08Zone
  {
    None = -1,
    Side = 0,
    Bottom = 1,
    Inside = 2
  }

  /// <summary>08 불량 유형 (Zone 내 1~8). Bit = TypeIndex-1.</summary>
  public enum MesNgPartType
  {
    Rust = 1,
    Spot = 2,
    Scratch = 3,
    Dent = 4,
    Crack = 5,
    Length = 6,
    Contamin = 7,
    Other = 8
  }
}