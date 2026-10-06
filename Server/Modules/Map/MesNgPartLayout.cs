using System;
using System.Collections.Generic;
using Server.Config;

namespace Server.Map
{
  public sealed class MesNgPartEntry
  {
    public int PartIndex1Based { get; set; }
    public ushort HoldingOffset { get; set; }
    public int ZoneIndex  { get; set; }
    public int TypeIndex { get; set; }
    public string ZoneName { get; set; }
    public string TypeName { get; set; }
    public int CamIndex { get; set; }
    public int RoiIndex { get; set; }
    public int SlotIndex { get; set; }
  }
  
  /*
     [NgPart(불량 부위) 16개의 Modbus 레지스터 레이아웃을 계산해서 보관하는 클래스.]
        설정값을 받아서 "NgPart 1번은 400017, 2번은 400019..." 처럼 실제 Modbus 주소표를 만들어주는 클래스.
  */
  public class MesNgPartLayout
  {
    public bool Enabled { get; private set; }
    public int Stride { get; private set; }
    public string[] Types { get; private set; }
    public MesNgPartEntry[] Entries { get; private set; }
    public int PartCount => Entries?.Length ?? 0;

    public int RequiredHoldingCount
    {
      get
      {
        if (!Enabled || PartCount == 0)
          return 0;
        var last = Entries[PartCount - 1];
        return last.HoldingOffset + Stride;
      }
    }
    
    public static MesNgPartLayout Disabled()
    {
      return new MesNgPartLayout
      {
        Enabled = false,
        Stride = 2,
        Types = new string[0],
        Entries = new MesNgPartEntry[0]
      };
    }

    public static MesNgPartLayout ExpandDongwonS0(MesHostParams host)
    {
      // 1단계: 기본값으로 초기화.
      var stride = Addresses.NgPartStride;
      var part1Address = 400001 + Addresses.HoldingNgPart1;
      var types = new[] { "얼룩", "긁힘", "찍힘" };

      // 2단계: JSON에 값 있으면 덮어씀.
      if (host != null && host.Map != null && host.Map.NgParts != null && host.Map.NgParts.Enabled)
      {
        MesNgPartsParams np = host.Map.NgParts;
        if(np.Stride >= 1) stride = np.Stride;
        if(np.StartAddress >= 400001) part1Address = np.StartAddress;
        if (np.Types != null && np.Types.Length > 0) types = np.Types;
      }

      var baseOff = part1Address - 400001;
      var entries = new List<MesNgPartEntry>(Addresses.NgPartCount);
      string[] names =
      {
        "Rupture", "Height", "ApertureRoundness", "ApertureDent",
        "Inner1_Stain", "Inner1_Scratch", "Inner1_Dent",
        "Inner2_Stain", "Inner2_Scratch", "Inner2_Dent",
        "Outer_Stain", "Outer_Scratch", "Outer_Dent",
        "Side_Stain", "Side_Scratch", "Side_Dent"
      };

      // 3단계: 위 값으로 16개 entry 전개.
      for (var i = 0; i < Addresses.NgPartCount; i++)
      {
        entries.Add(new MesNgPartEntry
        {
          PartIndex1Based = i + 1,
          HoldingOffset = (ushort)(baseOff + i * stride),
          ZoneIndex = i,
          TypeIndex = i >= 4 ? (i - 4) % 3 : -1,
          ZoneName = names[i],
          TypeName = i >= 4 ? types[(i - 4) % 3] : names[i],
          CamIndex = -1,
          RoiIndex = -1,
          SlotIndex = -1
        });
      }

      return new MesNgPartLayout
      {
        Enabled = true,
        Stride = stride,
        Types = (string[])types.Clone(),
        Entries = entries.ToArray()
      };
    }

    
    /*
      [검사 결과(classBits)와 구역(zoneIndex)을 받아서, 해당하는 Holding 레지스터 오프셋 목록을 뽑아주는 메서드]
      ushort classBits                : 검사 결과를 비트 플래그로 인코딩해서 넘겨줄 값            
      int zoneIndex                   : 구역       — 0=Inner1, 1=Inner2, 2=Outer                                                 
      List<ushort> holdingOffsetOut   : 결과를 담을 리스트
      index 0~3  : Rupture, Height, ApertureRoundness, ApertureDent  (외관 아님)                               
      index 4~6  : Inner1_얼룩, Inner1_긁힘, Inner1_찍힘   (zoneIndex=0)                                       
      index 7~9  : Inner2_얼룩, Inner2_긁힘, Inner2_찍힘   (zoneIndex=1)                                       
      index 10~12: Outer_얼룩,  Outer_긁힘,  Outer_찍힘    (zoneIndex=2)                                       
      index 13~15: Side_...
      
      ----------------------------------------------------------------
      
      classBits 구조                                                                                           
                                                                                                           
      bit 0 = 얼룩  (1 << 0 = 0b001 = 1)                                                                       
      bit 1 = 긁힘  (1 << 1 = 0b010 = 2)                                                                       
      bit 2 = 찍힘  (1 << 2 = 0b100 = 4)                                                                       
                                                                                                               
      예: classBits = 0b101 = 5 → 얼룩 + 찍힘 발생                                                             
                                                                                                               
      ---                                                                                                      
                                                                                                               
      반복문 풀기 (classBits=5, zoneIndex=0 → partBase=5)                                                      
                                                                                                               
      t=0 (얼룩 체크)                                                                                          
      1 << 0 = 0b001                                                                                           
      5 & 0b001 = 1  →  1 ≠ 0  →  continue 안 함 → 처리                                                        
      part = 5 + 0 = 5 → Entries[4] = Inner1_얼룩 오프셋 추가                                                  
                                                                                                               
      t=1 (긁힘 체크)                                                                                          
      1 << 1 = 0b010                                                                                           
      5 & 0b010 = 0  →  0 == 0  →  continue → 건너뜀                                                           
                                                                                                               
      t=2 (찍힘 체크)                                                                                          
      1 << 2 = 0b100                                                                                           
      5 & 0b100 = 4  →  4 ≠ 0  →  continue 안 함 → 처리                                                        
      part = 5 + 2 = 7 → Entries[6] = Inner1_찍힘 오프셋 추가                                                  
                                                                                                               
      ---                                                                                                      
                                                                                                               
      결과: classBits=5 → Inner1_얼룩, Inner1_찍힘 두 개의 Holding 오프셋만 리스트에 추가됨.
    */
    public void AppendAppearanceOffsets(ushort classBits, int zoneIndex, List<ushort> holdingOffsetsOut)
    {
      if (holdingOffsetsOut == null || !Enabled || Entries == null)
        return;
      if (zoneIndex < 0 || zoneIndex > 2)
        return;

      var partBase = 5 + zoneIndex * 3;
      for (var t = 0; t < Addresses.AppearanceTypeCount; t++)
      {
        if ((classBits & (1 << t)) == 0)
          continue;
        var part = partBase + t;
        if(part >=1 && part <= Entries.Length)
          holdingOffsetsOut.Add(Entries[part - 1].HoldingOffset);
      }
    }

    // [사이드 검사 결과를 받아서, 해당하는 Holding 레지스터 오프셋 목록을 뽑아주는 메서드]
    public void AppendSideAggregateOffsets(ushort sideClassOrBits, List<ushort> holdingOffsetsOut)
    {
      if (holdingOffsetsOut == null || !Enabled || Entries == null)
        return;

      for (var t = 0; t < Addresses.AppearanceTypeCount; t++)
      {
        if ((sideClassOrBits & (1 << t)) == 0)
          continue;
        var part = 14 + t;
        holdingOffsetsOut.Add(Entries[part - 1].HoldingOffset);
      }
    }
  }
  
  /*
    [MesMapParams vs MesMapRuntime]
    ┌───────────┬─────────────────────────┬────────────────────────────┐                                     
    │           │      MesMapParams       │       MesMapRuntime        │                                     
    ├───────────┼─────────────────────────┼────────────────────────────┤                                     
    │ 역할       │ JSON 설정값 그대로 보관    │ 실제 실행에 쓸 값으로 가공     │                                     
    ├───────────┼─────────────────────────┼────────────────────────────┤                                     
    │ 생성 시점   │ JSON 역직렬화 시          │ FromParams()로 변환 시      │                                     
    ├───────────┼─────────────────────────┼────────────────────────────┤                                     
    │ 값        │ 사용자가 입력한 그대로       │ 검증·보정·계산 완료된 값      │                                     
    └───────────┴─────────────────────────┴────────────────────────────┘                                     
  */
  public sealed class MesMapRuntime
  {
    public int CoilCount { get; private set; }
    public int HoldingCount { get; private set; }
    public MesNgPartLayout NgParts { get; private set; }

    public static MesMapRuntime FromParams(MesHostParams host)
    {
      var ng = MesNgPartLayout.ExpandDongwonS0(host);

      // 1단계: 일단 기본값으로 초기화
      var coils = Addresses.CoilCount;
      var holdings = Addresses.HoldingCount;
      
      // 2단계: JSON에 값 있으면 덮어씀.
      if (host != null && host.Map != null)
      {
        if(host.Map.CoilCount.HasValue && host.Map.CoilCount.Value > 0)
          coils = host.Map.CoilCount.Value;
        if(host.Map.HoldingCount.HasValue && host.Map.HoldingCount.Value > 0)
          holdings = host.Map.HoldingCount.Value;
      }

      // 3단계: 그 값이 너무 작으면 강제로 키움.
      var required = Math.Max(holdings, Addresses.HoldingNgWork5Percent + 1);
      if (ng.Enabled)
        required = Math.Max(required, ng.RequiredHoldingCount);
      if (required > holdings)
        holdings = required;

      if (coils < Addresses.CoilHeartbeat + 1)
        coils = Addresses.CoilHeartbeat + 1;

      return new MesMapRuntime
      {
        CoilCount = coils,
        HoldingCount = holdings,
        NgParts = ng
      };
    }
  }
}