using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using Server.Assign;
using Server.Map;

namespace Server.Mmf
{
  /*
    [MesHeartbeatManager가 심박만 담당했다면, 이 클래스는 실제 데이터 채널(IF) 전체를 담당.]     
                                                                                                 
    MesHeartbeatManager  →  심박 MMF 읽기/쓰기                                                     
    MesInterface         →  데이터 MMF 읽기/쓰기 + 심박 관리자 포함                                
                                                                                                   
    주요 역할 예상                                                                                 
      1. 자기 데이터 MMF 생성/관리                                                                   
      2. 상대 데이터 MMF 열기/읽기                                                                   
      3. Vision 검사 결과 쓰기  (PublishVisionResult)                                                
      4. Control 라인 상태 쓰기 (PublishControlLineState)                                            
      5. MES 명령/스냅샷 쓰기   (SetMesCmd, PublishWorkSnapshot)                                     
      6. Peer Alive 확인은 내부 HeartbeatManager에 위임            
    
    MesPeerRole enum이 4개인 게 포인트예요. 
    같은 클래스 하나로 Vision/Control/MesTowardVision/MesTowardControl 네 가지 역할을 전부 커버하는 구조예요.
  */
  public enum MesPeerRole
  {
    Vision,
    Control,
    MesTowardVision,
    MesTowardControl
  }
  
  public class MesInterface
  {
    private readonly object gate = new object();
    private readonly object peerIfGate = new object();
    
    private readonly MesHeartbeatManager heartbeat;
    
    private readonly MemoryMappedFile myIfMmf;
    private readonly MemoryMappedViewAccessor myIf;
    
    private readonly string peerIfName;
    private MemoryMappedFile peerIfMmf;
    private MemoryMappedViewAccessor peerIf;

    private MesInterface(MesPeerRole role, MesHeartbeatManager heartbeat)
    {
      if (heartbeat == null)
        throw new ArgumentNullException("heartbeat");

      Role = role;
      this.heartbeat = heartbeat;

      string myIfName;
      ResolveNames(role, out myIfName, out peerIfName);

      myIfMmf = MemoryMappedFile.CreateOrOpen(myIfName, MesMmfDefine.InterfaceByteSize);
      myIf = myIfMmf.CreateViewAccessor(0, MesMmfDefine.InterfaceByteSize);
      MesMmfClear.ClearInterfaceRegion(myIf);
    }

    private MesInterface(MesPeerRole role, MesHeartbeatManager heartbeat, string myIfName, string peerIfName)
    {
      if (heartbeat == null)
        throw new ArgumentNullException("heartbeat");
      if (string.IsNullOrWhiteSpace(myIfName))
        throw new ArgumentNullException("myIfName");
      if (string.IsNullOrWhiteSpace(peerIfName))
        throw new ArgumentNullException("peerIfName");

      Role = role;
      this.heartbeat = heartbeat;
      this.peerIfName = peerIfName;

      myIfMmf = MemoryMappedFile.CreateOrOpen(myIfName, MesMmfDefine.InterfaceByteSize);
      myIf = myIfMmf.CreateViewAccessor(0, MesMmfDefine.InterfaceByteSize);
      MesMmfClear.ClearInterfaceRegion(myIf);
    }

    public MesPeerRole Role { get; }

    public void Dispose()
    {
      ReleasePeerIf();
      if (myIf != null)
        myIf.Dispose();
      if (myIfMmf != null)
        myIfMmf.Dispose();
    }

    public static MesInterface ForVision(MesHeartbeatManager heartbeat)
    {
      return new MesInterface(MesPeerRole.Vision, heartbeat);
    }

    public static MesInterface ForControl(MesHeartbeatManager heartbeat)
    {
      return new MesInterface(MesPeerRole.Control, heartbeat);
    }

    public static MesInterface ForMesTowardVision(MesHeartbeatManager heartbeat)
    {
      return new MesInterface(MesPeerRole.MesTowardVision, heartbeat);
    }

    public static MesInterface ForMesTowardControl(MesHeartbeatManager heartbeat)
    {
      return new MesInterface(MesPeerRole.MesTowardControl, heartbeat);
    }

    /// <summary>params <c>mmf.pairs</c> 이름으로 Host↔App IF를 연다.</summary>
    public static MesInterface ForMesTowardVision(MesHeartbeatManager heartbeat, string hostInterface, string appInterface)
    {
      return new MesInterface(MesPeerRole.MesTowardVision, heartbeat, hostInterface, appInterface);
    }

    public static MesInterface ForMesTowardControl(MesHeartbeatManager heartbeat, string hostInterface, string appInterface)
    {
      return new MesInterface(MesPeerRole.MesTowardControl, heartbeat, hostInterface, appInterface);
    }

    public static MesInterface ForVision(MesHeartbeatManager heartbeat, string appInterface, string hostInterface)
    {
      return new MesInterface(MesPeerRole.Vision, heartbeat, appInterface, hostInterface);
    }

    public static MesInterface ForControl(MesHeartbeatManager heartbeat, string appInterface, string hostInterface)
    {
      return new MesInterface(MesPeerRole.Control, heartbeat, appInterface, hostInterface);
    }

    private static void ResolveNames(MesPeerRole role, out string myIf, out string peerIf)
    {
      switch (role)
      {
        case MesPeerRole.Vision:
          myIf = MesMmfDefine.MmfVisionInterface;
          peerIf = MesMmfDefine.MmfMesTowardVisionInterface;
          break;
        case MesPeerRole.Control:
          myIf = MesMmfDefine.MmfControlInterface;
          peerIf = MesMmfDefine.MmfMesTowardControlInterface;
          break;
        case MesPeerRole.MesTowardVision:
          myIf = MesMmfDefine.MmfMesTowardVisionInterface;
          peerIf = MesMmfDefine.MmfVisionInterface;
          break;
        case MesPeerRole.MesTowardControl:
          myIf = MesMmfDefine.MmfMesTowardControlInterface;
          peerIf = MesMmfDefine.MmfControlInterface;
          break;
        default:
          throw new ArgumentOutOfRangeException("role");
      }
    }

    public bool IsPeerAlive()
    {
      return heartbeat.IsPeerAlive();
    }

    //----- Mes → peer ------------------------------------------------------

    public void SetMesCmd(ushort cmd)
    {
      RequireMesSide();
      GuardAliveForWrite();
      WriteMyUInt16(MesInterProcessLayout.MesIf_Offset_MesCmd, cmd);
    }

    public void SetMesStatus(ushort status)
    {
      RequireMesSide();
      GuardAliveForWrite();
      WriteMyUInt16(MesInterProcessLayout.MesIf_Offset_MesStatus, status);
    }

    /*
      [메서드 명 'PublishWorkSnapshot' 에서 의미하는 스냅샷]
        특정 시점의 누적값을 그대로 찍어서 전달하기 때문이에요.                                                             
                                                                                                                        
        실시간으로 계속 바뀌는 값                                                                                           
          Total: 100 → 101 → 102 → 103 ...                                                                                  
                                                                                                                            
        특정 순간에 "지금 이 값이야"라고 찍어서 MMF에 씀                                                                    
          → 스냅샷 = 103                                                                                                    
                                                                                                                            
        사진 찍는 것과 같아요. 움직이는 피사체를 한 순간 정지시켜서 기록하는 것처럼, 계속 변하는 카운터를 특정 시점에       
        캡처해서 Control에 전달하는 거예요.                                                                                 
                                                                                                                            
        스트리밍(실시간 연속 전달)이 아니라 주기적으로 현재 상태를 복사해서 보내는 방식이라 snapshot이라는 단어를 쓴 거예요.
    */
    public void PublishWorkSnapshot(
      ushort seq,
      uint total,
      uint ok,
      uint rc,
      uint ng1,
      uint ng2,
      uint ng3,
      uint ng4,
      uint ng5)
    {
      RequireMesSide();
      lock (gate)
      {
        myIf.Write(MesInterProcessLayout.Offset_Version, MesInterProcessLayout.SchemaVersion);
        myIf.Write(MesInterProcessLayout.MesIf_Offset_SnapshotSeq, seq);
        WriteDWordWords(MesInterProcessLayout.MesIf_Offset_TotalLow, total);
        WriteDWordWords(MesInterProcessLayout.MesIf_Offset_OkLow, ok);
        WriteDWordWords(MesInterProcessLayout.MesIf_Offset_RcLow, rc);
        WriteDWordWords(MesInterProcessLayout.MesIf_Offset_Ng1Low, ng1);
        WriteDWordWords(MesInterProcessLayout.MesIf_Offset_Ng2Low, ng2);
        WriteDWordWords(MesInterProcessLayout.MesIf_Offset_Ng3Low, ng3);
        WriteDWordWords(MesInterProcessLayout.MesIf_Offset_Ng4Low, ng4);
        WriteDWordWords(MesInterProcessLayout.MesIf_Offset_Ng5Low, ng5);
      }
    }

    //----- Vision Write ----------------------------------------------------

    public void PublishVisionResult(MesMapS0Publish publish)
    {
      if (publish == null)
        throw new ArgumentNullException("publish");

      RequireRole(MesPeerRole.Vision);
      GuardAliveForWrite();

      lock (gate)
      {
        myIf.Write(MesInterProcessLayout.Offset_Version, MesInterProcessLayout.SchemaVersion);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_CycleId, publish.CycleId);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_VisionState, publish.VisionState);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_ChannelNgBits, publish.ChannelNgBits);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_SideNgMask, publish.SideNgMask);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_ClassBitsInner1, publish.ClassBitsInner1);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_ClassBitsInner2, publish.ClassBitsInner2);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_ClassBitsOuter, publish.ClassBitsOuter);

        for (var slot = 0; slot < Addresses.SideSlotCount; slot++)
        {
          ushort bits = 0;
          if (publish.SideClassBits != null && slot < publish.SideClassBits.Length)
            bits = publish.SideClassBits[slot];
          myIf.Write(MesInterProcessLayout.SideClassBitsOffset(slot), bits);
        }

        myIf.Write(MesInterProcessLayout.VisIf_Offset_DeltaTotal, publish.DeltaTotal);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_DeltaOk, publish.DeltaOk);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_DeltaNg1, publish.DeltaNg1);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_DeltaRc, publish.DeltaRc);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_DeltaNg2, publish.DeltaNg2);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_DeltaNg3, publish.DeltaNg3);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_DeltaNg4, publish.DeltaNg4);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_DeltaNg5, publish.DeltaNg5);
        myIf.Write(MesInterProcessLayout.VisIf_Offset_ApertureNgBits, publish.ApertureNgBits);
        var publishFlags = MesInterProcessLayout.PublishFlag_NewResult;
        if (publish.UseAbsoluteWorkCounters)
          publishFlags |= MesInterProcessLayout.PublishFlag_AbsoluteWorkCounters;
        myIf.Write(MesInterProcessLayout.VisIf_Offset_PublishFlags, publishFlags);
      }
    }

    /// <summary>
    ///   이슈#13(Vision #383) — Host가 소비한 Vision CycleId를 자기 IF에 기록(Vision이 peer로 읽음).
    ///   자기 IF Write라 Peer Alive 불필요.
    /// </summary>
    public void SetVisionAckCycleId(ushort cycleId)
    {
      RequireMesSide();
      WriteMyUInt16(MesInterProcessLayout.MesIf_Offset_VisionAckCycleId, cycleId);
    }

    //----- Control Write ---------------------------------------------------

    public void PublishControlLineState(ushort lineState)
    {
      RequireRole(MesPeerRole.Control);
      GuardAliveForWrite();

      lock (gate)
      {
        myIf.Write(MesInterProcessLayout.CtrlIf_Offset_LineState, lineState);
        myIf.Write(MesInterProcessLayout.CtrlIf_Offset_PublishFlags, (ushort)0);
      }
    }

    public void PublishControlLineAndCounters(
      ushort lineState,
      uint total,
      uint ok,
      uint rc,
      uint ng1,
      uint ng2)
    {
      RequireRole(MesPeerRole.Control);
      GuardAliveForWrite();

      lock (gate)
      {
        myIf.Write(MesInterProcessLayout.CtrlIf_Offset_LineState, lineState);
        WriteDWordWords(MesInterProcessLayout.CtrlIf_Offset_TotalLow, total);
        WriteDWordWords(MesInterProcessLayout.CtrlIf_Offset_OkLow, ok);
        WriteDWordWords(MesInterProcessLayout.CtrlIf_Offset_RcLow, rc);
        WriteDWordWords(MesInterProcessLayout.CtrlIf_Offset_Ng1Low, ng1);
        WriteDWordWords(MesInterProcessLayout.CtrlIf_Offset_Ng2Low, ng2);
        myIf.Write(MesInterProcessLayout.CtrlIf_Offset_PublishFlags, MesInterProcessLayout.PublishFlag_Counters);
      }
    }

    //----- Peer Read (Alive gated) -----------------------------------------

    public bool TryReadPeerUInt16(int offset, out ushort value)
    {
      value = 0;
      if (!IsPeerAlive())
        return false;
      if (!TryOpenPeerIf())
        return false;

      lock (peerIfGate)
      {
        if (peerIf == null)
          return false;
        value = peerIf.ReadUInt16(offset);
        return true;
      }
    }

    public bool TryReadPeerDWord(int lowOffset, out uint value)
    {
      ushort low, high;
      if (!TryReadPeerUInt16(lowOffset, out low) || !TryReadPeerUInt16(lowOffset + 2, out high))
      {
        value = 0;
        return false;
      }

      value = ((uint)high << 16) | low;
      return true;
    }

    public bool TryPeekPeerUInt16(int offset, out ushort value)
    {
      value = 0;
      if (!TryOpenPeerIf())
        return false;

      lock (peerIfGate)
      {
        if (peerIf == null)
          return false;
        value = peerIf.ReadUInt16(offset);
        return true;
      }
    }

    public ushort ReadMyUInt16(int offset)
    {
      lock (gate)
      {
        return myIf.ReadUInt16(offset);
      }
    }

    private void WriteMyUInt16(int offset, ushort value)
    {
      lock (gate)
      {
        myIf.Write(offset, value);
      }
    }

    private void WriteDWordWords(int lowOffset, uint value)
    {
      // IF 내부: low Word then high Word (little-endian words) — RegisterStore ABCD와는 별도; Bridge에서 변환
      var low = (ushort)(value & 0xFFFF);
      var high = (ushort)((value >> 16) & 0xFFFF);
      myIf.Write(lowOffset, low);
      myIf.Write(lowOffset + 2, high);
    }

    private void GuardAliveForWrite()
    {
      if (!IsPeerAlive())
        throw new InvalidOperationException("MES peer not Alive — IF Write blocked.");
    }

    private void RequireRole(MesPeerRole expected)
    {
      if (Role != expected)
        throw new InvalidOperationException("MES IF role mismatch. Expected " + expected + ", actual " + Role);
    }

    private void RequireMesSide()
    {
      if (Role != MesPeerRole.MesTowardVision && Role != MesPeerRole.MesTowardControl)
        throw new InvalidOperationException("MesCmd/Status requires MesToward* role.");
    }

    private bool TryOpenPeerIf()
    {
      lock (peerIfGate)
      {
        if (peerIf != null)
          return true;

        try
        {
          peerIfMmf = MemoryMappedFile.OpenExisting(peerIfName);
          peerIf = peerIfMmf.CreateViewAccessor(0, MesMmfDefine.InterfaceByteSize);
          return true;
        }
        catch (FileNotFoundException)
        {
          return false;
        }
        catch
        {
          ReleasePeerIf();
          return false;
        }
      }
    }

    private void ReleasePeerIf()
    {
      if (peerIf != null)
      {
        peerIf.Dispose();
        peerIf = null;
      }

      if (peerIfMmf != null)
      {
        peerIfMmf.Dispose();
        peerIfMmf = null;
      }
    }
  }
}