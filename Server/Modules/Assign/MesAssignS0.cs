using Server.Map;

namespace Server.Assign
{
  public sealed class MesMapS0Publish
  {
    public MesMapS0Publish()
    {
      SideClassBits = new ushort[Addresses.SideSlotCount];
    }

    public ushort CycleId { get; set; }
    public ushort VisionState { get; set; }

    /// <summary>bit0..5 = Z1..Z6 NG.</summary>
    public ushort ChannelNgBits { get; set; }

    /// <summary>bit0..11 = Side slot NG.</summary>
    public ushort SideNgMask { get; set; }

    public ushort ClassBitsInner1 { get; set; }
    public ushort ClassBitsInner2 { get; set; }
    public ushort ClassBitsOuter { get; set; }

    /// <summary>bit0=진원도/직경 NG · bit1=결함크기 NG.</summary>
    public ushort ApertureNgBits { get; set; }

    /// <summary>슬롯별 유형 bit (길이 12 · MMF용).</summary>
    public ushort[] SideClassBits { get; set; }

    public ushort DeltaTotal { get; set; }
    public ushort DeltaOk { get; set; }
    public ushort DeltaNg1 { get; set; }
    public ushort DeltaNg2 { get; set; }
    public ushort DeltaNg3 { get; set; }
    public ushort DeltaNg4 { get; set; }
    public ushort DeltaNg5 { get; set; }
    public ushort DeltaRc { get; set; }

    /// <summary>true면 DeltaTotal~DeltaRc에 Work 절대 누적(ushort)을 실어 MMF에 쓴다.</summary>
    public bool UseAbsoluteWorkCounters { get; set; }
  }
  
  public static class MesAssignS0
  {
    public const ushort ChannelNg_Rupture = 1 << 0;
    public const ushort ChannelNg_Height = 1 << 1;
    public const ushort ChannelNg_Aperture = 1 << 2;
    public const ushort ChannelNg_Inner1 = 1 << 3;
    public const ushort ChannelNg_Inner2 = 1 << 4;
    public const ushort ChannelNg_Outer = 1 << 5;

    public const ushort ApertureNg_Roundness = 1 << 0;
    public const ushort ApertureNg_Dent = 1 << 1;

    public static MesMapS0Publish Aggregate(MesCycleInput input, bool countAsNewCan)
    {
      var output = new MesMapS0Publish();
      if (input == null)
        return output;

      output.CycleId = input.CycleId;
      output.VisionState = input.VisionState;

      var anyNg = false;
      if (input.Channels == null)
        return FinishDeltas(output, countAsNewCan, anyNg);

      for (var i = 0; i < input.Channels.Count; i++)
      {
        var ch = input.Channels[i];
        if (ch == null)
          continue;

        if (!ch.IsOk)
          anyNg = true;

        switch (ch.Channel)
        {
          case MesInspChannel.Rupture:
            if (!ch.IsOk)
              output.ChannelNgBits |= ChannelNg_Rupture;
            break;
          case MesInspChannel.Height:
            if (!ch.IsOk)
              output.ChannelNgBits |= ChannelNg_Height;
            break;
          case MesInspChannel.Aperture:
            if (!ch.IsOk)
              output.ChannelNgBits |= ChannelNg_Aperture;
            break;
          case MesInspChannel.Inner1:
            if (!ch.IsOk)
            {
              output.ChannelNgBits |= ChannelNg_Inner1;
              output.ClassBitsInner1 |= ClassBitAppearance(ch.ClassName);
            }

            break;
          case MesInspChannel.Inner2:
            if (!ch.IsOk)
            {
              output.ChannelNgBits |= ChannelNg_Inner2;
              output.ClassBitsInner2 |= ClassBitAppearance(ch.ClassName);
            }

            break;
          case MesInspChannel.Outer:
            if (!ch.IsOk)
            {
              output.ChannelNgBits |= ChannelNg_Outer;
              output.ClassBitsOuter |= ClassBitAppearance(ch.ClassName);
            }

            break;
          case MesInspChannel.Side1:
          case MesInspChannel.Side2:
          case MesInspChannel.Side3:
            break;
        }
      }

      return FinishDeltas(output, countAsNewCan, anyNg);
    }

    /// <summary>얼룩/긁힘/찍힘만 bit. 기타·미분류는 0 (ngPart에 기타 없음).</summary>
    private static ushort ClassBitAppearance(string className)
    {
      var t = MesClassNameMap.MapClassNameToS0(className);
      if (t == MesS0DefectType.None || t == MesS0DefectType.Other)
        return 0;
      return MesClassNameMap.TypeToBit(t);
    }

    /// <summary>Side ROI 스냅샷 병합.</summary>
    public static void ApplySideSlots(MesMapS0Publish output, bool[] slotOk, string[] classNames)
    {
      if (output == null)
        return;
      if (output.SideClassBits == null || output.SideClassBits.Length < Addresses.SideSlotCount)
        output.SideClassBits = new ushort[Addresses.SideSlotCount];

      var n = Addresses.SideSlotCount;
      for (var slot = 0; slot < n; slot++)
      {
        var ok = slotOk != null && slot < slotOk.Length && slotOk[slot];
        if (ok)
          continue;

        output.SideNgMask |= (ushort)(1 << slot);
        var name = classNames != null && slot < classNames.Length ? classNames[slot] : string.Empty;
        output.SideClassBits[slot] |= ClassBitAppearance(name);
      }
    }

    /// <summary>개구부 진원도/결함 세부 (ngPart3/4).</summary>
    public static void ApplyApertureDetails(MesMapS0Publish output, bool roundnessOk, bool dentOk)
    {
      if (output == null)
        return;
      if (!roundnessOk)
        output.ApertureNgBits |= ApertureNg_Roundness;
      if (!dentOk)
        output.ApertureNgBits |= ApertureNg_Dent;
      if (output.ApertureNgBits != 0)
        output.ChannelNgBits |= ChannelNg_Aperture;
    }

    /// <summary>
    ///   Work 버킷 (라인 분리 · Total 1회만):
    ///   A~D → Total+1 (NG면 Work1~4). 배출 캔은 Side 미도달.
    ///   Side 12슬롯 → Work5 += NG수, OK += OK수. Total은 가산하지 않음
    ///   (A~D 통과 시 이미 Total++. Side에서 또 +12 하면 이중).
    ///   A~D OK + Side가 한 Publish에만 온 경우(통과분 미가산)에 한해 Total += 12.
    /// </summary>
    /// <param name="workPriorityMode">
    ///   0=Individual / 2=Descending → A 우선 · 1=Ascending → D 우선.
    /// </param>
    /// <param name="applySideSlotWork">
    ///   true면 Side 12슬롯 결과를 Work5/OK에 슬롯 수만큼 반영 (검사 1회분).
    /// </param>
    public static void FinalizeDeltas(
      MesMapS0Publish output,
      bool countAsNewCan,
      int workPriorityMode = 0,
      bool applySideSlotWork = false)
    {
      if (output == null || !countAsNewCan)
        return;
      AssignWorkDeltas(output, workPriorityMode, applySideSlotWork);
    }

    private static MesMapS0Publish FinishDeltas(MesMapS0Publish output, bool countAsNewCan, bool anyNg)
    {
      if (countAsNewCan)
        AssignWorkDeltas(output, 0, false);
      return output;
    }

    private static void AssignWorkDeltas(MesMapS0Publish output, int workPriorityMode, bool applySideSlotWork)
    {
      output.DeltaTotal = 0;
      output.DeltaOk = 0;
      output.DeltaNg1 = 0;
      output.DeltaNg2 = 0;
      output.DeltaNg3 = 0;
      output.DeltaNg4 = 0;
      output.DeltaNg5 = 0;
      output.DeltaRc = 0;

      var ngA = (output.ChannelNgBits & (ChannelNg_Rupture | ChannelNg_Height)) != 0;
      var ngB = (output.ChannelNgBits & ChannelNg_Outer) != 0;
      var ngC = (output.ChannelNgBits & (ChannelNg_Inner1 | ChannelNg_Inner2)) != 0;
      var ngD = (output.ChannelNgBits & ChannelNg_Aperture) != 0;
      var hasAdNg = ngA || ngB || ngC || ngD;

      // A~D: 캔 1개 → Total+1. NG면 Work1~4 (배출 → Side 안 감)
      if (hasAdNg)
      {
        if (workPriorityMode == 1)
        {
          // Ascending: D > C > B > A
          if (ngD)
            output.DeltaNg4 = 1;
          else if (ngC)
            output.DeltaNg3 = 1;
          else if (ngB)
            output.DeltaNg2 = 1;
          else
            output.DeltaNg1 = 1;
        }
        else
        {
          // Individual / Descending: A > B > C > D
          if (ngA)
            output.DeltaNg1 = 1;
          else if (ngB)
            output.DeltaNg2 = 1;
          else if (ngC)
            output.DeltaNg3 = 1;
          else
            output.DeltaNg4 = 1;
        }

        output.DeltaTotal = 1;
      }
      else if (!applySideSlotWork)
      {
        // A~D 전원 OK · Side 없음 → 통과 캔 Total+1 (이후 Side에서 Total 재가산 금지)
        output.DeltaTotal = 1;
      }

      // Side 12슬롯: Work5만. Total 재가산 없음 (= A~D에서 ++ 후 NG분 -- 와 동일 효과)
      // OK는 Control 소관이므로 DeltaOk에 싣지 않는다 (이슈#7)
      // 예외: A~D NG 없이 Side만 온 Publish → 통과분이 아직 Total에 없으면 +12 1회
      if (applySideSlotWork)
      {
        var ngSide = PopCount12(output.SideNgMask);
        var okSide = Addresses.SideSlotCount - ngSide;
        if (ngSide < 0)
          ngSide = 0;
        if (okSide < 0)
          okSide = 0;

        output.DeltaNg5 = (ushort)ngSide;

        if (!hasAdNg)
          output.DeltaTotal = (ushort)(ngSide + okSide);
        // hasAdNg == true: Total은 A~D +1만. Side 슬롯은 이미 A~D 통과 시 카운트된 집단.
      }
    }

    /// <summary>SideNgMask 하위 12bit 중 1인 비트 수.</summary>
    private static int PopCount12(ushort mask)
    {
      var n = 0;
      var m = (ushort)(mask & 0x0FFF);
      while (m != 0)
      {
        n += m & 1;
        m >>= 1;
      }

      return n;
    }
  }
}