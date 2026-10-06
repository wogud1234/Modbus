using System.Collections.Generic;

namespace Server.Map
{
  public enum MesTagArea
  {
    Coil,
    Holding
  }

  public enum MesTagDataType
  {
    Boolean,
    Word,
    DWord
  }

  /// <summary>Map 태그 메타데이터 (이슈#39 확정표 SSOT).</summary>
  public sealed class MesTagInfo
  {
    public MesTagInfo(
      string name,
      MesTagArea area,
      ushort offset,
      string addressLabel,
      MesTagDataType dataType,
      string description)
    {
      Name = name;
      Area = area;
      Offset = offset;
      AddressLabel = addressLabel;
      DataType = dataType;
      Description = description;
    }

    public string Name { get; private set; }
    public MesTagArea Area { get; private set; }
    public ushort Offset { get; private set; }
    public string AddressLabel { get; private set; }
    public MesTagDataType DataType { get; private set; }
    public string Description { get; private set; }
  }
  
  public class MesMapLayout
  {
    private static readonly MesTagInfo[] s_tags = BuildTags();

    public static IReadOnlyList<MesTagInfo> Tags => s_tags;

    private static MesTagInfo[] BuildTags()
    {
      var list = new List<MesTagInfo>
      {
        Coil("Ready", Addresses.CoilReady, "000001", "운전준비"),
        Coil("OP_Mode_Normal", Addresses.CoilOpModeNormal, "000002", "운전 모드 — CAN 투입 검사 진행"),
        Coil("OP_Mode_Manual", Addresses.CoilOpModeManual, "000003", "운전 모드 — 수동"),
        Coil("Start", Addresses.CoilStart, "000004", "자동시작"),
        Coil("Stop", Addresses.CoilStop, "000005", "자동정지"),
        Coil("Data_REQ", Addresses.CoilDataReq, "000006", "데이터요구 응답"),
        Coil("Data_CMD", Addresses.CoilDataCmd, "000007", "데이터요구 (OPC Client → MesHost)"),
        Coil("WorkClear_CMD", Addresses.CoilWorkClearCmd, "000011", "Work 수량 초기화 명령 (OPC Client → MesHost)"),
        Coil("WorkClear_ACK", Addresses.CoilWorkClearAck, "000012", "Work 초기화 완료 응답"),
        Coil("HEARTBEAT", Addresses.CoilHeartbeat, "001000", "통신 감시 (Heartbeat)"),

        DWord("Total_TestCounter",Addresses.HoldingTotalTestCounter, "400001", "총 CAN 투입 수량"),
        DWord("OK_WorkCounter",   Addresses.HoldingOkWorkCounter, "400003", "검사결과 — Tray 적재 CAN 수량"),
        DWord("RC_WorkCounter",   Addresses.HoldingRcWorkCounter, "400005", "검사결과 — Recycle 적재 CAN 수량"),
        DWord("NG_Work1_Counter", Addresses.HoldingNgWork1Counter, "400007", "NG Stock#1 적재 수량 (Group A)"),
        DWord("NG_Work2_Counter", Addresses.HoldingNgWork2Counter, "400009", "NG Stock#2 적재 수량 (Group B)"),
        DWord("NG_Work3_Counter", Addresses.HoldingNgWork3Counter, "400011", "NG Stock#3 적재 수량 (Group C)"),
        DWord("NG_Work4_Counter", Addresses.HoldingNgWork4Counter, "400013", "NG Stock#4 적재 수량 (Group D)"),
        DWord("NG_Work5_Counter", Addresses.HoldingNgWork5Counter, "400015", "NG Stock#5 적재 수량 (Group Side)"),

        DWord("ngPart1", Addresses.HoldingNgPart1, "400017", "R부 터짐 검사 NG 수량"),
        DWord("ngPart2", Addresses.HoldingNgPart2, "400019", "총고 검사 NG 수량"),
        DWord("ngPart3", Addresses.HoldingNgPart3, "400021", "개구부 직경/진원도 NG 수량"),
        DWord("ngPart4", Addresses.HoldingNgPart4, "400023", "개구부 결함 크기 NG 수량"),
        DWord("ngPart5", Addresses.HoldingNgPart5, "400025", "내면1 얼룩 NG"),
        DWord("ngPart6", Addresses.HoldingNgPart6, "400027", "내면1 긁힘 NG"),
        DWord("ngPart7", Addresses.HoldingNgPart7, "400029", "내면1 찍힘 NG"),
        DWord("ngPart8", Addresses.HoldingNgPart8, "400031", "내면2 얼룩 NG"),
        DWord("ngPart9", Addresses.HoldingNgPart9, "400033", "내면2 긁힘 NG"),
        DWord("ngPart10", Addresses.HoldingNgPart10, "400035", "내면2 찍힘 NG"),
        DWord("ngPart11", Addresses.HoldingNgPart11, "400037", "외면 얼룩 NG"),
        DWord("ngPart12", Addresses.HoldingNgPart12, "400039", "외면 긁힘 NG"),
        DWord("ngPart13", Addresses.HoldingNgPart13, "400041", "외면 찍힘 NG"),
        DWord("ngPart14", Addresses.HoldingNgPart14, "400043", "측면 얼룩 NG"),
        DWord("ngPart15", Addresses.HoldingNgPart15, "400045", "측면 긁힘 NG"),
        DWord("ngPart16", Addresses.HoldingNgPart16, "400047", "측면 찍힘 NG"),

        Word("OK_Work_Percent",  Addresses.HoldingOkWorkPercent, "400049", "OK 비율"),
        Word("RC_Work_Percent",  Addresses.HoldingRcWorkPercent, "400051", "Recycle CAN 비율"),
        Word("NG_Work1_Percent", Addresses.HoldingNgWork1Percent, "400053", "NG Stock#1 비율"),
        Word("NG_Work2_Percent", Addresses.HoldingNgWork2Percent, "400055", "NG Stock#2 비율"),
        Word("NG_Work3_Percent", Addresses.HoldingNgWork3Percent, "400057", "NG Stock#3 비율"),
        Word("NG_Work4_Percent", Addresses.HoldingNgWork4Percent, "400059", "NG Stock#4 비율"),
        Word("NG_Work5_Percent", Addresses.HoldingNgWork5Percent, "400061", "NG Stock#5 비율")
      };

      return list.ToArray();
    }

    private static MesTagInfo Coil(string name, ushort offset, string label, string desc)
    {
      return new MesTagInfo(name, MesTagArea.Coil, offset, label, MesTagDataType.Boolean, desc);
    }

    private static MesTagInfo Word(string name, ushort offset, string label, string desc)
    {
      return new MesTagInfo(name, MesTagArea.Holding, offset, label, MesTagDataType.Word, desc);
    }

    private static MesTagInfo DWord(string name, ushort offset, string label, string desc)
    {
      return new MesTagInfo(name, MesTagArea.Holding, offset, label, MesTagDataType.DWord, desc);
    }
  }
}