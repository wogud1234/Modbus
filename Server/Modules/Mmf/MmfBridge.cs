using System;
using System.Collections.Generic;
using System.Threading;
using NscanMes.Core.Persist;
using Server.Assign;
using Server.Config;
using Server.Map;
using Server.Register;

namespace Server.Mmf
{
  /*
   [MMF ↔ Modbus RegisterStore 사이의 다리(Bridge) 역할]
   
         Vision MMF / Control MMF                                                                                            
              ↓  폴링(50ms마다)                                                                                           
          MmfBridge                                                                                                       
              ↓  변환                                                                                                     
        RegisterStore (Modbus Holding/Coil)                                                                               
              ↓                                                                                                           
         KepServerEX6                                                                                                     
                                                                                                                      
        주요 동작                                                                                                           
        TickVision()   →  Vision MMF 읽기 → NewResult 플래그 확인 → 카운터/NG 레지스터 반영                                 
        TickControl()  →  Control MMF 읽기 → LineState → Coil 반영, 카운터 → Holding 반영                                   
                                                                                                                            
        핵심 포인트                                                                                                         
        Vision   →  델타값(증분)으로 오면 IncrementDWord, 절대값이면 SetDWord                                               
        Control  →  항상 절대값으로 와서 바로 SetDWord로 덮어씀                                                             
        CycleId  →  1씩 증가해야 정상, 건너뛰면 유실로 카운트     
  */
  public sealed class MmfBridge : IDisposable
  {
    private const int VisionCycleResetGuard = 1000;

    private readonly object gate = new object();
    private readonly MesInterface mesControl;
    private readonly MesInterface mesVision;
    private readonly MesNgPartLayout ngParts;
    private readonly MesWorkPersist persist;
    private readonly Store store;
    
    private bool lastControlAlive;
    private bool lastVisionAlive;
    
    private bool hasVisionCycle;
    private uint lastControlTotal = uint.MaxValue;
    private ushort lastVisionCycleId;

    private Timer timer;
    private ushort workSnapshotSeq;

    //------------------------------------------------------------------------------------------------------------------------------------------------
    /*
      [MmfBridge는 MES 입장에서 동작하기 때문에, Vision/Control의 MMF를 peer로 읽는 쪽]
      
       MesTowardVision  →  "MES Vision IF" 자기 MMF                                                                        
                           "Vision MES IF" peer로 읽음  ← Vision 데이터 읽기                                               
                                                                                                                      
       MesTowardControl →  "MES Control IF" 자기 MMF                                                                       
                           "Control MES IF" peer로 읽음 ← Control 데이터 읽기
    */
    public MmfBridge(Store store, MesInterface mesTowardVision, MesInterface mesTowardControl,
      MesBridgeOptions options = null,
      MesNgPartLayout ngParts = null,
      MesWorkPersist persist = null)
    {
      if (store == null)
        throw new ArgumentNullException("store");
      if (mesTowardVision == null && mesTowardControl == null)
        throw new ArgumentException("at least one MES interface is required");

      this.store = store;
      mesVision = mesTowardVision;
      mesControl = mesTowardControl;
      Options = options ?? MesBridgeOptions.CreateVisionLedDefaults();
      this.ngParts = ngParts ?? MesNgPartLayout.Disabled();
      this.persist = persist;
      if (Options.PollMs < 10)
        Options.PollMs = 10;
    }

    /// <summary>이슈#13(Vision #383 T-003) — CycleId 불연속으로 감지된 누적 유실 사이클 수(진단용).</summary>
    public uint VisionCycleLossCount { get; private set; }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public bool IsVisionPeerAlive => mesVision != null && lastVisionAlive;

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public bool IsControlPeerAlive => mesControl != null && lastControlAlive;

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public MesBridgeOptions Options { get; }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public void Dispose()
    {
      Stop();
    }

    public event Action<string> Log;

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public void Start()
    {
      lock (gate)
      {
        if (timer != null)
          return;

        timer = new Timer(OnTick, null, 0, Options.PollMs);
        RaiseLog(string.Format(
          "MmfBridge started poll={0}ms mode={1} ctrlOverwrite={2} visionCounters={3} visionNg={4}",
          Options.PollMs,
          Options.WriterMode,
          Options.AllowControlCounterOverwrite,
          Options.ApplyVisionWorkCounters,
          Options.ApplyVisionNgPart));
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public void Stop()
    {
      Timer t;
      lock (gate)
      {
        t = timer;
        timer = null;
      }

      if (t == null)
        return;

      using (var done = new ManualResetEvent(false))
      {
        t.Dispose(done);
        done.WaitOne(2000);
      }

      RaiseLog("MmfBridge stopped");
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private void OnTick(object state)
    {
      try
      {
        TickVision();
        TickControl();
      }
      catch (Exception ex)
      {
        RaiseLog("MmfBridge tick: " + ex.Message);
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private void TickVision()
    {
      if (mesVision == null)
        return;

      var alive = mesVision.IsPeerAlive();
      if (alive != lastVisionAlive)
      {
        lastVisionAlive = alive;
        RaiseLog(alive ? "Vision peer Alive" : "Vision peer Lost");
        if (alive)
          TrySetMesStatus(mesVision, 1);
      }

      if (!alive)
        return;

      if (!Options.ApplyVisionWorkCounters && !Options.ApplyVisionNgPart)
        return;

      ushort flags;
      if (!mesVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_PublishFlags, out flags))
        return;
      if ((flags & MesInterProcessLayout.PublishFlag_NewResult) == 0)
        return;

      ushort cycleId;
      if (!mesVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_CycleId, out cycleId))
        return;

      if (hasVisionCycle && cycleId == lastVisionCycleId)
        return;

      // 이슈#13(Vision #383 T-003) — CycleId는 1씩 증가(0은 skip)하므로, 직전 반영값+1이
      // 아니면 그 사이 사이클이 ack 유실(단일 슬롯 덮어쓰기)로 소실된 것이다.
      if (hasVisionCycle)
        CheckVisionCycleLoss(cycleId);

      ushort channelNgLog, sideNgLog, cInner1Log, cInner2Log, cOuterLog;
      if (ApplyVisionPublish(mesVision, store, Options, ngParts,
            out channelNgLog, out sideNgLog, out cInner1Log, out cInner2Log, out cOuterLog))
      {
        lastVisionCycleId = cycleId;
        hasVisionCycle = true;

        // 이슈#13(Vision #383) — 델타 소비를 즉시 ack해야 Vision 쪽 다음 publish 대기가
        // 불필요하게 길어지지 않는다.
        try
        {
          mesVision.SetVisionAckCycleId(cycleId);
        }
        catch (InvalidOperationException)
        {
        }

        RaiseLog(string.Format(
          "Bridge Vision cycle={0} Total={1} OK(ctrl)={2} NG1={3} chNg=0x{4:X} sideNg=0x{5:X} cI1=0x{6:X} cI2=0x{7:X} cO=0x{8:X}",
          cycleId,
          store.GetDWord(Addresses.HoldingTotalTestCounter),
          store.GetDWord(Addresses.HoldingOkWorkCounter),
          store.GetDWord(Addresses.HoldingNgWork1Counter),
          channelNgLog,
          sideNgLog,
          cInner1Log,
          cInner2Log,
          cOuterLog));
        PersistAndPublishWork();
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>
    ///   이슈#13(Vision #383 T-003) — 직전 반영 CycleId 대비 gap을 계산해 유실 사이클을 감지·로그한다.
    ///   CycleId는 Vision <c>CMesVisionPublisher.NextCycleId</c>에서 1씩 증가하며 0은 건너뛴다.
    /// </summary>
    private void CheckVisionCycleLoss(ushort cycleId)
    {
      var expected = (ushort)(lastVisionCycleId + 1);
      if (expected == 0)
        expected = 1;

      if (cycleId == expected)
        return;

      int gap = (ushort)(cycleId - expected);
      if (gap <= 0)
        return;

      if (gap >= VisionCycleResetGuard)
      {
        RaiseLog(string.Format(
          "Vision CycleId 역행/불연속(gap={0}) 감지 — 재시작으로 판단, 유실 카운트 제외 (last={1} now={2})",
          gap, lastVisionCycleId, cycleId));
        return;
      }

      VisionCycleLossCount += (uint)gap;
      RaiseLog(string.Format(
        "MES Vision 카운트 유실 감지 — {0}개 사이클 누락(last={1} now={2}), 누적 유실={3}",
        gap, lastVisionCycleId, cycleId, VisionCycleLossCount));
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private void TickControl()
    {
      if (mesControl == null)
        return;

      var alive = mesControl.IsPeerAlive();
      if (alive != lastControlAlive)
      {
        lastControlAlive = alive;
        RaiseLog(alive ? "Control peer Alive" : "Control peer Lost");
        if (alive)
          TrySetMesStatus(mesControl, 1);
      }

      if (!alive)
      {
        PublishWorkSnapshot();
        return;
      }

      ApplyControlPublish(mesControl, store, Options, ref lastControlTotal, RaiseLog);
      if (Options.AllowControlCounterOverwrite)
        PersistAndPublishWork();
      else
        PublishWorkSnapshot();
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private static void TrySetMesStatus(MesInterface mes, ushort status)
    {
      try
      {
        mes.SetMesStatus(status);
      }
      catch (InvalidOperationException)
      {
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    /// <returns>반영했으면 true</returns>
    internal static bool ApplyVisionPublish(
      MesInterface mesTowardVision, 
      Store store, 
      MesBridgeOptions options, 
      MesNgPartLayout ngParts = null)
    {
      ushort a, b, c, d, e;
      return ApplyVisionPublish(mesTowardVision, store, options, ngParts, out a, out b, out c, out d, out e);
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    internal static bool ApplyVisionPublish(
      MesInterface mesTowardVision,
      Store store,
      MesBridgeOptions options,
      MesNgPartLayout ngParts,
      out ushort channelNgOut,
      out ushort sideNgOut,
      out ushort classInner1Out,
      out ushort classInner2Out,
      out ushort classOuterOut)
    {
      channelNgOut = 0;
      sideNgOut = 0;
      classInner1Out = 0;
      classInner2Out = 0;
      classOuterOut = 0;

      if (options == null)
        options = MesBridgeOptions.CreateVisionLedDefaults();
      if (ngParts == null)
        ngParts = MesNgPartLayout.Disabled();

      ushort flags;
      if (!mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_PublishFlags, out flags))
        return false;
      if ((flags & MesInterProcessLayout.PublishFlag_NewResult) == 0)   // 새 값이 아니면
        return false;

      var any = false;

      ushort channelNg = 0;
      ushort sideNg = 0;
      mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_ChannelNgBits, out channelNg);
      mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_SideNgMask, out sideNg);

      channelNgOut = channelNg;
      sideNgOut = sideNg;

      // ngPart1~4 (측정) + Work deltas + Appearance/Side ngPart
      if ((channelNg & MesAssignS0.ChannelNg_Rupture) != 0)
      {
        store.IncrementDWord(Addresses.HoldingNgPart1, 1);
        any = true;
      }

      if ((channelNg & MesAssignS0.ChannelNg_Height) != 0)
      {
        store.IncrementDWord(Addresses.HoldingNgPart2, 1);
        any = true;
      }

      ushort apertureNg = 0;
      mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_ApertureNgBits, out apertureNg);
      if (apertureNg == 0 && (channelNg & MesAssignS0.ChannelNg_Aperture) != 0)
        apertureNg = MesAssignS0.ApertureNg_Roundness; // 세부 없으면 진원도 버킷
      if ((apertureNg & MesAssignS0.ApertureNg_Roundness) != 0)
      {
        store.IncrementDWord(Addresses.HoldingNgPart3, 1);
        any = true;
      }

      if ((apertureNg & MesAssignS0.ApertureNg_Dent) != 0)
      {
        store.IncrementDWord(Addresses.HoldingNgPart4, 1);
        any = true;
      }

      if (options.ApplyVisionWorkCounters)
      {
        // OK_WorkCounter는 Control 소관 — Vision의 DeltaOk는 읽지도, 가산하지도 않는다 (이슈#7)
        ushort dTotal, dNg1, dNg2, dNg3, dNg4, dNg5, dRc;
        mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_DeltaTotal, out dTotal);
        mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_DeltaNg1, out dNg1);
        mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_DeltaNg2, out dNg2);
        mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_DeltaNg3, out dNg3);
        mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_DeltaNg4, out dNg4);
        mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_DeltaNg5, out dNg5);
        mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_DeltaRc, out dRc);

        var absolute = (flags & MesInterProcessLayout.PublishFlag_AbsoluteWorkCounters) != 0;
        if (absolute)
        {
          SetWorkCounterAbsolute(store, Addresses.HoldingTotalTestCounter, dTotal);
          SetWorkCounterAbsolute(store, Addresses.HoldingNgWork1Counter, dNg1);
          SetWorkCounterAbsolute(store, Addresses.HoldingNgWork2Counter, dNg2);
          SetWorkCounterAbsolute(store, Addresses.HoldingNgWork3Counter, dNg3);
          SetWorkCounterAbsolute(store, Addresses.HoldingNgWork4Counter, dNg4);
          SetWorkCounterAbsolute(store, Addresses.HoldingNgWork5Counter, dNg5);
          if (dRc != 0)
            SetWorkCounterAbsolute(store, Addresses.HoldingRcWorkCounter, dRc);
        }
        else
        {
          store.IncrementDWord(Addresses.HoldingTotalTestCounter, dTotal);
          store.IncrementDWord(Addresses.HoldingNgWork1Counter, dNg1);
          store.IncrementDWord(Addresses.HoldingNgWork2Counter, dNg2);
          store.IncrementDWord(Addresses.HoldingNgWork3Counter, dNg3);
          store.IncrementDWord(Addresses.HoldingNgWork4Counter, dNg4);
          store.IncrementDWord(Addresses.HoldingNgWork5Counter, dNg5);
          if (dRc != 0)
            store.IncrementDWord(Addresses.HoldingRcWorkCounter, dRc);
        }

        any = true;
      }

      if (options.ApplyVisionNgPart && ngParts.Enabled)
      {
        ushort cInner1, cInner2, cOuter;
        mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_ClassBitsInner1, out cInner1);
        mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_ClassBitsInner2, out cInner2);
        mesTowardVision.TryReadPeerUInt16(MesInterProcessLayout.VisIf_Offset_ClassBitsOuter, out cOuter);

        classInner1Out = cInner1;
        classInner2Out = cInner2;
        classOuterOut = cOuter;

        var offsets = new List<ushort>();
        ngParts.AppendAppearanceOffsets(cInner1, 0, offsets);
        ngParts.AppendAppearanceOffsets(cInner2, 1, offsets);
        ngParts.AppendAppearanceOffsets(cOuter, 2, offsets);

        ushort sideOr = 0;
        for (var slot = 0; slot < Addresses.SideSlotCount; slot++)
        {
          ushort sideClass;
          if (!mesTowardVision.TryReadPeerUInt16(
                MesInterProcessLayout.SideClassBitsOffset(slot), out sideClass))
            sideClass = 0;
          sideOr |= sideClass;
        }

        ngParts.AppendSideAggregateOffsets(sideOr, offsets);

        // 동일 offset 중복 +1 방지
        var unique = new HashSet<ushort>(offsets);
        foreach (var off in unique)
          store.IncrementDWord(off, 1);

        any = true;
      }

      if (any && options.RecalcPercentOnUpdate)
        store.RecalcPercents();

      return any;
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>Control LineState 항상(옵션) · 카운터는 overwrite Flag일 때만.</summary>
    internal static void ApplyControlPublish(
      MesInterface mesTowardControl,
      Store store,
      MesBridgeOptions options,
      ref uint lastTotal,
      Action<string> log)
    {
      if (options == null)
        options = MesBridgeOptions.CreateVisionLedDefaults();

      ushort flags;
      if (!mesTowardControl.TryReadPeerUInt16(MesInterProcessLayout.CtrlIf_Offset_PublishFlags, out flags))
        return;

      if (options.ApplyControlLineState)
      {
        ushort line;
        if (mesTowardControl.TryReadPeerUInt16(MesInterProcessLayout.CtrlIf_Offset_LineState, out line))
        {
          store.SetCoil(Addresses.CoilReady, (line & MesInterProcessLayout.LineState_Ready) != 0);
          store.SetCoil(Addresses.CoilOpModeNormal, (line & MesInterProcessLayout.LineState_OpNormal) != 0);
          store.SetCoil(Addresses.CoilOpModeManual, (line & MesInterProcessLayout.LineState_OpManual) != 0);
          store.SetCoil(Addresses.CoilStart, (line & MesInterProcessLayout.LineState_Start) != 0);
          store.SetCoil(Addresses.CoilStop, (line & MesInterProcessLayout.LineState_Stop) != 0);
        }
      }

      if (!options.AllowControlCounterOverwrite)
        return;
      if ((flags & MesInterProcessLayout.PublishFlag_Counters) == 0)
        return;

      var total = ReadPeerDWord(mesTowardControl, MesInterProcessLayout.CtrlIf_Offset_TotalLow);
      var ok = ReadPeerDWord(mesTowardControl, MesInterProcessLayout.CtrlIf_Offset_OkLow);
      var rc = ReadPeerDWord(mesTowardControl, MesInterProcessLayout.CtrlIf_Offset_RcLow);
      var ng1 = ReadPeerDWord(mesTowardControl, MesInterProcessLayout.CtrlIf_Offset_Ng1Low);
      var ng2 = ReadPeerDWord(mesTowardControl, MesInterProcessLayout.CtrlIf_Offset_Ng2Low);

      // Control은 델타가 아니라 절대값을 발행하므로 IncrementDWord가 아니라 직접 덮어쓴다.
      // 윈도우/누적(Lifetime) 양쪽 다 같은 절대값으로 맞춰야 대시보드가 어긋나지 않는다.
      store.SetDWord(Addresses.HoldingTotalTestCounter, total);
      store.SetDWord(Addresses.HoldingOkWorkCounter, ok);
      store.SetDWord(Addresses.HoldingRcWorkCounter, rc);
      store.SetDWord(Addresses.HoldingNgWork1Counter, ng1);
      store.SetDWord(Addresses.HoldingNgWork2Counter, ng2);
      store.SetAccumDWord(Addresses.HoldingTotalTestCounter, total);
      store.SetAccumDWord(Addresses.HoldingOkWorkCounter, ok);
      store.SetAccumDWord(Addresses.HoldingRcWorkCounter, rc);
      store.SetAccumDWord(Addresses.HoldingNgWork1Counter, ng1);
      store.SetAccumDWord(Addresses.HoldingNgWork2Counter, ng2);
      if (options.RecalcPercentOnUpdate)
        store.RecalcPercents();

      if (total != lastTotal)
      {
        lastTotal = total;
        if (log != null)
          log(string.Format("Bridge Control counters Total={0} OK={1} RC={2} NG1={3} NG2={4}", total, ok, rc, ng1,
            ng2));
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private static uint ReadPeerDWord(MesInterface iface, int lowOffset)
    {
      ushort low, high;
      iface.TryReadPeerUInt16(lowOffset, out low);
      iface.TryReadPeerUInt16(lowOffset + 2, out high);
      return ((uint)high << 16) | low;
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>Work Clear 등 외부에서 Holding을 바꾼 뒤 파일·Control 스냅샷 동기.</summary>
    public void NotifyWorkChanged()
    {
      PersistAndPublishWork();
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private void PersistAndPublishWork()
    {
      if (persist != null)
        try
        {
          persist.SaveFrom(store);
        }
        catch (Exception ex)
        {
          RaiseLog("Work persist save: " + ex.Message);
        }

      PublishWorkSnapshot();
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private void PublishWorkSnapshot()
    {
      // IsPeerAlive() 확인 없이 바로 쓰면, "Control App" 쪽 피어가 안 떠 있는 평소 상태(별도 모니터
      // 프로세스를 안 띄운 경우)에서 이 메서드가 불릴 때마다(TickControl은 pollMs=50ms마다 돎)
      // InvalidOperationException이 던져졌다 캐치되는 걸 반복하게 됨 - 디버거에 초당 수십 번씩
      // first-chance 예외로 잡혀서 노이즈만 나고, 실제로는 의도된 정상 경로(피어 없음)임.
      if (mesControl == null || !mesControl.IsPeerAlive())
        return;

      workSnapshotSeq++;
      try
      {
        mesControl.PublishWorkSnapshot(
          workSnapshotSeq,
          store.GetDWord(Addresses.HoldingTotalTestCounter),
          store.GetDWord(Addresses.HoldingOkWorkCounter),
          store.GetDWord(Addresses.HoldingRcWorkCounter),
          store.GetDWord(Addresses.HoldingNgWork1Counter),
          store.GetDWord(Addresses.HoldingNgWork2Counter),
          store.GetDWord(Addresses.HoldingNgWork3Counter),
          store.GetDWord(Addresses.HoldingNgWork4Counter),
          store.GetDWord(Addresses.HoldingNgWork5Counter));
      }
      catch (InvalidOperationException)
      {
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>Vision 절대 Work 카운터 — 윈도우·Lifetime 양쪽 SET (이슈#16).</summary>
    private static void SetWorkCounterAbsolute(Store store, ushort offset, uint value)
    {
      store.SetDWord(offset, value);
      store.SetAccumDWord(offset, value);
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private void RaiseLog(string msg)
    {
      var h = Log;
      if (h != null)
        h(msg);
    }
  }
}