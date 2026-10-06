namespace Server.Mmf
{
  public class MesInterProcessLayout
  {
    /// <summary>4 = Mes→Control Work 스냅샷 (Total/OK/RC/NG1~5).</summary>
    public const ushort SchemaVersion = 4;

    public const int DefaultInterfacePollMs = 50;
    public const int DefaultAliveTimeoutMs = 3000;

    //----- Shared / Mes → peer IF ------------------------------------------
    public const int Offset_Version         = 0x00; // ushort — SchemaVersion. 모든 IF 공통 첫 필드
    public const int MesIf_Offset_MesCmd    = 0x02; // ushort — MES가 peer에게 보내는 명령 (MesCmd_* 상수)
    public const int MesIf_Offset_MesStatus = 0x04; // ushort — MES 상태값

    // ushort — Work 스냅샷 갱신 시퀀스 번호 [Control 전용]
    // MES가 Work 누적값을 Control IF에 쓸 때마다 1씩 증가시키는 카운터. Control이 "새 데이터가 왔다"는 걸 감지하는 용도         
    public const int MesIf_Offset_SnapshotSeq = 0x06; 

    // ushort — MES가 마지막으로 소비한 Vision CycleId [Vision 전용]
    // MES가 Vision 결과를 소비한 후 "나 CycleId N까지 처리했어"라고 Vision에게 알려주는 값.
    // Vision은 이 값이 자기 CycleId랑 같아질 때까지 다음 publish를 잠깐 대기
    public const int MesIf_Offset_VisionAckCycleId = 0x06; 

    // [Control 전용] (Work 누적값)
    // 아래 카운터는 uint(32bit)를 Low/High 두 ushort(16bit)로 나눠 저장 (little-endian word 순)
    /*
      [MES가 자기 MMF에 쓰는 값들....]
        Vision이 매 검사마다 MMF에 씀 -> VisIf_Offset_DeltaTotal = 이번 사이클 증가분 (+1, +2 ...) -> ES가 읽어서 내부에서 누적                               
        -> MES가 자기 MMF에 씀 (MesIf_Offset_TotalLow) = 누적 합계 (100, 101, 102 ...)         
    */
    public const int MesIf_Offset_TotalLow = 0x08; // ushort — 전체 검사 수량 Low word  → Modbus HoldingTotalTestCounter(400001)
    public const int MesIf_Offset_OkLow    = 0x0C; // ushort — OK 수량 Low word         → HoldingOkWorkCounter(400003)
    public const int MesIf_Offset_RcLow    = 0x10; // ushort — RC(재검) 수량 Low word    → HoldingRcWorkCounter(400005)
    public const int MesIf_Offset_Ng1Low   = 0x14; // ushort — NG Group A 수량 Low word  → HoldingNgWork1Counter(400007)
    public const int MesIf_Offset_Ng2Low   = 0x18; // ushort — NG Group B 수량 Low word  → HoldingNgWork2Counter(400009)
    public const int MesIf_Offset_Ng3Low   = 0x1C; // ushort — NG Group C 수량 Low word  → HoldingNgWork3Counter(400011)
    public const int MesIf_Offset_Ng4Low   = 0x20; // ushort — NG Group D 수량 Low word  → HoldingNgWork4Counter(400013)
    public const int MesIf_Offset_Ng5Low   = 0x24; // ushort — NG Group Side 수량 Low word → HoldingNgWork5Counter(400015)

    // ------------------------------------------------------------------------------------------------------------
    //----- Vision MES IF (Vision Write) · Schema 2 -------------------------
    public const int VisIf_Offset_CycleId        = 0x02; // ushort — Vision 검사 사이클 ID (매 검사마다 증가)
    
    /*
      [VisIf_Offset_PublishFlags에 담기는 데이터]
        bit0 가 1이면                                                                           
          → "새 검사 결과 있음"                                                                                    
          → MES가 이걸 보고 읽을지 말지 결정                                                                       
                                                                                                                 
        bit1 가 1이면                                                                
          → "Delta 필드에 증분이 아닌 절대값을 담았음"                                                             
          → MES가 이걸 보고 누적 방식을 다르게 처리  
    */
    public const int VisIf_Offset_PublishFlags    = 0x04; // ushort — PublishFlag_* 비트 조합 (신규 결과 여부 등)
    
    public const int VisIf_Offset_VisionState     = 0x06; // ushort — Vision 장비 상태 (VisionState_Idle/Run/Alarm)
    public const int VisIf_Offset_ChannelNgBits   = 0x08; // ushort — 인덱스 카메라 5대 중 어느 카메라가 NG가 났는지 한눈에 보는 비트맵
    public const int VisIf_Offset_SideNgMask      = 0x0A; // ushort — 캔 12개 중 어느 캔에서 NG가 났는지 한눈에 보는 비트맵
    public const int VisIf_Offset_ClassBitsInner1 = 0x0C; // ushort — Inner1 면 불량 분류 비트 (얼룩/긁힘/찍힘)
    public const int VisIf_Offset_ClassBitsInner2 = 0x0E; // ushort — Inner2 면 불량 분류 비트
    public const int VisIf_Offset_ClassBitsOuter  = 0x10; // ushort — Outer 면 불량 분류 비트

    /// <summary>SideClassBits[slot] — slot 0..11, 각 ushort low 3bit = 얼룩/긁힘/찍힘.</summary>
    public const int VisIf_Offset_SideClassBits0 = 0x14; // ushort — Side 슬롯 0 불량 분류 비트 (이후 stride=2씩)

    public const int VisIf_SideClassBitsStride = 2; // 슬롯 하나당 2바이트 (ushort)

    // 아래 Delta 카운터: 이전 publish 이후 증가분 (AbsoluteWorkCounters 플래그 시 절대값)
    public const int VisIf_Offset_DeltaTotal = 0x2C; // ushort — 전체 검사 수량 증분 (이번 사이클 증가분)
    public const int VisIf_Offset_DeltaOk    = 0x2E; // ushort — OK 수량 증분
    public const int VisIf_Offset_DeltaNg1   = 0x30; // ushort — NG Group A(파열) 수량 증분  → HoldingNgPart1(400017)
    public const int VisIf_Offset_DeltaRc    = 0x32; // ushort — RC(재검) 수량 증분
    public const int VisIf_Offset_DeltaNg2   = 0x34; // ushort — NG Group B(Height) 수량 증분 → HoldingNgPart2(400019)
    public const int VisIf_Offset_DeltaNg3   = 0x36; // ushort — NG Group C 수량 증분
    public const int VisIf_Offset_DeltaNg4   = 0x38; // ushort — NG Group D 수량 증분
    public const int VisIf_Offset_DeltaNg5   = 0x3A; // ushort — NG Group Side 수량 증분

    /// <summary>bit0=진원도/직경 · bit1=결함크기.</summary>
    public const int VisIf_Offset_ApertureNgBits = 0x3C; // ushort — Aperture(구멍) NG 세부 비트 → HoldingNgPart3/4(400021/400023)

    public const ushort PublishFlag_NewResult = 1 << 0; // bit0 — 새로운 검사 결과가 있음
    // bit0 = 1  →  Vision이 새 검사 결과를 방금 씀  →  MES가 읽어야 함                                           
    // bit0 = 0  →  아직 새 결과 없음 (대기 중)      →  MES가 스킵
    
    public const ushort PublishFlag_AbsoluteWorkCounters = 1 << 1; 
    // PublishFlags bit1 = 0  →  DeltaTotal, DeltaOk ... 에 증분값 담김  (+1, +3 ...)                             
    // PublishFlags bit1 = 1  →  DeltaTotal, DeltaOk ... 에 절대값 담김  (100, 101 ...)
    //  MES가 읽을 때 이 비트를 먼저 확인하고 처리 방식을 분기.

    public const ushort VisionState_Idle  = 0; // Vision 대기 상태
    public const ushort VisionState_Run   = 1; // Vision 검사 실행 중
    public const ushort VisionState_Alarm = 2; // Vision 알람 발생

    // ------------------------------------------------------------------------------------------------------------
    //----- Control MES IF (Control Write) ----------------------------------
    public const int CtrlIf_Offset_LineState    = 0x02; // ushort — 라인 상태 비트 (LineState_* 상수 조합)
    public const int CtrlIf_Offset_PublishFlags = 0x04; // ushort — PublishFlag_* (카운터 유효 여부 등)
    // 아래 카운터는 uint(32bit)를 Low/High 두 ushort(16bit)로 나눠 저장
    public const int CtrlIf_Offset_TotalLow  = 0x06; // ushort — 전체 검사 수량 Low word  → HoldingTotalTestCounter(400001)
    public const int CtrlIf_Offset_TotalHigh = 0x08; // ushort — 전체 검사 수량 High word
    public const int CtrlIf_Offset_OkLow     = 0x0A; // ushort — OK 수량 Low word         → HoldingOkWorkCounter(400003)
    public const int CtrlIf_Offset_OkHigh    = 0x0C; // ushort — OK 수량 High word
    public const int CtrlIf_Offset_RcLow     = 0x0E; // ushort — RC(재검) 수량 Low word    → HoldingRcWorkCounter(400005)
    public const int CtrlIf_Offset_RcHigh    = 0x10; // ushort — RC(재검) 수량 High word
    public const int CtrlIf_Offset_Ng1Low    = 0x12; // ushort — NG Group A 수량 Low word  → HoldingNgWork1Counter(400007)
    public const int CtrlIf_Offset_Ng1High   = 0x14; // ushort — NG Group A 수량 High word
    public const int CtrlIf_Offset_Ng2Low    = 0x16; // ushort — NG Group B 수량 Low word  → HoldingNgWork2Counter(400009)
    public const int CtrlIf_Offset_Ng2High   = 0x18; // ushort — NG Group B 수량 High word
    /*
      [vision이 쓰는 데이터와 중복됨 관련...]
        - control은 이 값을 왜 자기 메모리에 mes 보고 읽어가라고 쓰는거야?? 이 데이터들은 이미 vision이 자기 메모리 영역에 쓰지않았어?                                                 
                                                                                                                 
        ● 좋은 포인트예요. 데이터 출처가 다른 거예요.                                                                
                                                                                                                     
          Vision IF의 카운터   →  Vision 검사 결과 (불량 판정 기준)                                                  
          Control IF의 카운터  →  실제 설비 카운터 (PLC/장비가 센 값)                                                
                                                                                                                     
          예를 들어 캔이 1000개 투입됐을 때:                                                                         
                                                                                                                     
          Vision  →  총 998개 검사 완료 (2개는 센서 미감지로 누락)                                                   
          Control →  총 1000개 통과 (설비 센서 기준)                                                                 
                                                                                                                     
          두 값이 다를 수 있어요. MES가 둘 다 받아서 비교하거나 각각 다른 용도로 Modbus에 올려야 하기 때문에         
          Control도 자기 카운터를 따로 쓰는 거예요.                                                                  
                                                                                                                     
          또한 Vision이 꺼져 있거나 통신이 끊겼을 때도 Control 카운터는 계속 올라가야 하니까, 독립적인 카운터 소스를 
          갖는 게 설계상 맞아요.
    */
    
    public const ushort LineState_Ready    = 1 << 0; // bit0 — 라인 준비 완료
    public const ushort LineState_OpNormal = 1 << 1; // bit1 — 자동 운전 모드
    public const ushort LineState_OpManual = 1 << 2; // bit2 — 수동 운전 모드
    public const ushort LineState_Start    = 1 << 3; // bit3 — 라인 가동 중
    public const ushort LineState_Stop     = 1 << 4; // bit4 — 라인 정지

    public const ushort PublishFlag_Counters = 1 << 0; // bit0 — 카운터 필드가 유효함
    // bit0 = 1  →  TotalLow~Ng2High 카운터 필드에 유효한 데이터가 있음  →  MES가 읽어야 함                       
    // bit0 = 0  →  카운터 없음 (LineState만 갱신한 경우)              →  카운터 스킵 

    public const ushort MesCmd_Idle        = 0; // MES 명령 없음 (대기)
    public const ushort MesCmd_DataRequest = 1; // MES가 peer에게 데이터 요청

    public static int SideClassBitsOffset(int slotIndex0)
    {
      return VisIf_Offset_SideClassBits0 + slotIndex0 * VisIf_SideClassBitsStride;
    }
  }
}
