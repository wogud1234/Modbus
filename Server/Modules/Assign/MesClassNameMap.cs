using System;
using Server.Map;

namespace Server.Assign
{
  /// <summary>S0 유형 4종 (얼룩/긁힘/찍힘/기타). bit0..3.</summary>
  public enum MesS0DefectType
  {
    Stain = 0,
    Scratch = 1,
    Dent = 2,
    Other = 3,
    None = -1
  }

  public class MesClassNameMap
  {
    /// <summary>
    ///   Vision DL ClassName → S0 유형.
    ///   실명: 얼룩이물 / 긁힘 / 찍힘돌출 / R부찍힘 (Outer·Inner·Side 공통).
    /// </summary>
    public static MesS0DefectType MapClassNameToS0(string className)
    {
      if (string.IsNullOrWhiteSpace(className))
        return MesS0DefectType.None;

      var s = className.Trim();
      if (string.Equals(s, "OK", StringComparison.OrdinalIgnoreCase))
        return MesS0DefectType.None;

      // 정확 매칭 우선 (모델 ClassInfo.Name)
      if (string.Equals(s, "얼룩이물", StringComparison.Ordinal)
          || string.Equals(s, "얼룩", StringComparison.Ordinal))
        return MesS0DefectType.Stain;
      if (string.Equals(s, "긁힘", StringComparison.Ordinal))
        return MesS0DefectType.Scratch;
      if (string.Equals(s, "찍힘돌출", StringComparison.Ordinal)
          || string.Equals(s, "R부찍힘", StringComparison.Ordinal)
          || string.Equals(s, "찍힘", StringComparison.Ordinal))
        return MesS0DefectType.Dent;

      if (Contains(s, "얼룩") || Contains(s, "이물") || Contains(s, "오염")
          || Contains(s, "Stain") || Contains(s, "Contamin") || Contains(s, "Particle"))
        return MesS0DefectType.Stain;
      if (Contains(s, "긁힘") || Contains(s, "스크래치") || Contains(s, "Scratch"))
        return MesS0DefectType.Scratch;
      if (Contains(s, "찍힘") || Contains(s, "돌출") || Contains(s, "Dent") || Contains(s, "충격"))
        return MesS0DefectType.Dent;

      // SIDE_NG · 미분류
      if (Contains(s, "SIDE_NG") || Contains(s, "기타") || Contains(s, "Other") || Contains(s, "Unknown"))
        return MesS0DefectType.Other;

      return MesS0DefectType.Other;
    }

    public static ushort TypeToBit(MesS0DefectType type)
    {
      var index = (int)type;
      if (index < 0 || index >= Addresses.AppearanceTypeCount)
        return 0;
      return (ushort)(1 << index);
    }

    public static string TypeName(MesS0DefectType type)
    {
      switch (type)
      {
        case MesS0DefectType.Stain: return "얼룩";
        case MesS0DefectType.Scratch: return "긁힘";
        case MesS0DefectType.Dent: return "찍힘";
        case MesS0DefectType.Other: return "기타";
        default: return "-";
      }
    }

    /// <summary>레거시 8유형 API.</summary>
    public static MesNgPartType MapClassName(string className)
    {
      switch (MapClassNameToS0(className))
      {
        case MesS0DefectType.Stain:
          return MesNgPartType.Contamin;
        case MesS0DefectType.Scratch:
          return MesNgPartType.Scratch;
        case MesS0DefectType.Dent:
          return MesNgPartType.Dent;
        default:
          return MesNgPartType.Other;
      }
    }

    public static ushort TypeToBit(MesNgPartType type)
    {
      var index = (int)type - 1;
      if (index < 0 || index > 7)
        index = 7;
      return (ushort)(1 << index);
    }

    private static bool Contains(string haystack, string needle)
    {
      return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
  }
}