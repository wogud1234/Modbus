using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace Server.Mmf
{
  // 프로세스 간 생존 신호에 관한 MMF 영역 관리하는 클래스
  
  public sealed class MesHeartbeatManager : IDisposable
  {
    /*
      [백오프]
        실패했을 때 잠시 기다렸다가 재시도하는 전략.
          백오프 없을 때                                                                                             
            50ms마다 OpenExisting() 호출                                                                             
            → 상대 프로세스 없으면 매번 예외 발생                                                                    
            → 디버거에 예외 노이즈 가득           
                                                                               
          백오프 있을 때                                                                                             
            OpenExisting() 실패                                                                                      
            → 1초 동안 재시도 자체를 안 함                                                                           
            → 1초 후 다시 시도                                                                                       
            → 또 실패하면 또 1초 대기 
    */
    private static readonly TimeSpan openRetryBackoff = TimeSpan.FromSeconds(1);
    
    private readonly MemoryMappedFile myMmf;              //  MMF 블록 자체 ("Vision MES" 라는 이름의 메모리 덩어리) 
    private readonly MemoryMappedViewAccessor myAccessor; // 그 메모리에 실제로 읽고 쓰는 도구 (Read/Write 메서드 제공)
    private readonly object peerLock = new object();
    private readonly string peerName;
    private readonly object timerLock = new object();
    private long counter;

    private int m_intervalMs = MesMmfDefine.DefaultHeartbeatIntervalMs;

    // 마지막으로 OpenExisting() 실패한 시각 (백오프용)
    // "실패한 지 1초 안 됐으면 OpenExisting() 자체를 건너뜀" 
    private DateTime lastOpenAttemptFailedUtc = DateTime.MinValue;  // C#에서 DataTime이 가질 수 있는 가장 오래된 시각 (0001년 1월 1일 00:00:00)
    
    // 상대 카운터가 마지막으로 바뀐 시각 (타임아웃 판단용)
    // "카운터가 마지막으로 바뀐 게 3초 넘었으면 죽은 것"
    private DateTime lastPeerChangeUtc = DateTime.UtcNow;   // 재 시각 (UTC, 협정 세계시 기준)
    
    private long lastPeerCounter = -1;
    private MemoryMappedFile peerMmf;                
    private MemoryMappedViewAccessor peerAccessor;  
    private Timer writeTimer;

    
    
    //------------------------------------------------------------------------------------------------------------------------------------------------
    public MesHeartbeatManager(string myName, string peerName)
    {
      this.peerName = peerName;
      myMmf = MemoryMappedFile.CreateOrOpen(myName, MesMmfDefine.HeartbeatByteSize);  // 해당 이름의 MMF가 없으면 새로 만들고, 이미 있으면 그냥 염
      myAccessor = myMmf.CreateViewAccessor(0, MesMmfDefine.HeartbeatByteSize);     // MMF의 특정 범위를 읽고 쓸 수 있는 accessor 생성 
      MesMmfClear.ClearHeartbeatRegion(myAccessor);
      
      /*
        [8바이트를 읽는다 == mmf 메모리 8칸을 읽는다. long 배열 한 칸에 그 8바이트를 담는다.]
          
            MMF [8][9][10][11][12][13][14][15]  ← 8칸 읽음                                                             
                    ↓                                                                                            
               long 한 칸                 ← 8바이트 담김   
           
        [MesMmfDefine.HeartbeatByteSize가 16이라는 것은 long배열 두 칸에 들어가는 양의 데이터를 읽는다는뜻. - 최대 16바이트까지 읽겠다.]  
        
             size = 16바이트 = long 2칸 분량
             [0~7]   →  long 1칸 (DateTime.UtcNow.Ticks)                                                                
             [8~15]  →  long 1칸 (m_counter)  
             
         [심박 카운터 (long) 인 이유??]                                                                                         
            - 프로세스끼리만 쓰는 내부 데이터, Modbus 규격 무관                                                        
            - DateTime.Ticks가 long 타입 (0001년부터 100나노초 단위 누적값이라 매우 큰 수)                             
            - 카운터도 오래 돌아도 오버플로 안 나게 long으로                                         
      */
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public void Dispose()
    {
      Stop();
      ReleasePeer();
      if (myAccessor != null) myAccessor.Dispose();
      if (myMmf != null) myMmf.Dispose();
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public static MesHeartbeatManager ForVision()
    {
      return new MesHeartbeatManager(MesMmfDefine.MmfVisionHeartbeat, MesMmfDefine.MmfMesTowardVisionHeartbeat);
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public static MesHeartbeatManager ForControl()
    {
      return new MesHeartbeatManager(MesMmfDefine.MmfControlHeartbeat, MesMmfDefine.MmfMesTowardControlHeartbeat);
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public static MesHeartbeatManager ForMesTowardVision()
    {
      return new MesHeartbeatManager(MesMmfDefine.MmfMesTowardVisionHeartbeat, MesMmfDefine.MmfVisionHeartbeat);
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public static MesHeartbeatManager ForMesTowardControl()
    {
      return new MesHeartbeatManager(MesMmfDefine.MmfMesTowardControlHeartbeat, MesMmfDefine.MmfControlHeartbeat);
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public void Start(int intervalMs = MesMmfDefine.DefaultHeartbeatIntervalMs)
    {
      lock (timerLock)
      {
        m_intervalMs = intervalMs;
        if (writeTimer != null)
          writeTimer.Dispose();
        writeTimer = new Timer(_ => WriteHeartbeat(), null, 0, intervalMs);
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public void Stop()
    {
      lock (timerLock)
      {
        if (writeTimer != null)
        {
          writeTimer.Dispose();
          writeTimer = null;
        }
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public bool IsPeerAlive(int timeoutMs = MesInterProcessLayout.DefaultAliveTimeoutMs)
    {
      lock (peerLock)
      {
        long counter;
        if (!TryReadPeerCounter(out counter))
        {
          lastPeerCounter = -1;
          return false;
        }

        if (counter != lastPeerCounter)
        {
          lastPeerCounter = counter;
          lastPeerChangeUtc = DateTime.UtcNow;
          return true;
        }

        return (DateTime.UtcNow - lastPeerChangeUtc).TotalMilliseconds <= timeoutMs;
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private void WriteHeartbeat()
    {
      counter++;
      myAccessor.Write(0, DateTime.UtcNow.Ticks);
      /*
        [mmf 메모리 0번째 칸 부터 7번째 칸에 현재 시간을 저장]
        
           DateTime.UtcNow.Ticks = 638,500,000,000,000,000  (예시)                                        
              이진수로 변환하면:                                                                             
                [00001000  11011010  10110001  00100011  10000110  00000000  00000000  00000000]                      
                  0번칸      1번칸      2번칸      3번칸      4번칸      5번칸     6번칸      7번칸                   
                                                                                                         
          ReadInt64(0)으로 읽으면 이 8칸을 다시 long으로 조립해서 반환해주는 거예요. 
          이진 데이터를 사람이 읽을 수 있는 숫자로 해석하는 건 전적으로 코드가 하는 일이에요.         
      */
      
      myAccessor.Write(8, counter);
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private bool TryReadPeerCounter(out long counter)
    {
      counter = 0;
      try
      {
        if (peerAccessor == null)
        {
          // 최근에 열기 실패했으면(=피어가 아직 없음) 백오프 기간 동안은 재시도(OS 호출) 자체를 건너뛴다.
          if (DateTime.UtcNow - lastOpenAttemptFailedUtc < openRetryBackoff)
            return false;

          peerMmf = MemoryMappedFile.OpenExisting(peerName);
          peerAccessor = peerMmf.CreateViewAccessor(0, MesMmfDefine.HeartbeatByteSize);
          
          /*
            [OpenExisting vs CreateOrOpen]
            
                CreateOrOpen  →  없으면 만들고, 있으면 열기  (자기 MMF에 사용)
                OpenExisting  →  있으면 열고, 없으면 예외    (상대 MMF에 사용)                                 
                                                                                                               
                왜 구분하냐면:                                                                                 
                자기 MMF는 내가 주인이라 없으면 내가 만들어야 해요. 상대 MMF는 상대가 만들어야 하는 거라 내가 만들면 안 돼요. 
                상대가 아직 안 떴으면 FileNotFoundException이 나고, 그걸 잡아서 "상대 아직 없음"으로 처리하는 거예요.                                                                     
                                                                                                               
                나(Vision)    →  "Vision MES"  CreateOrOpen  (내 거, 내가 만듦)                                
                상대(MES)     →  "MES Vision"  OpenExisting  (상대 거, 상대가 만든 것을 열기만)                
          */
        }

        counter = peerAccessor.ReadInt64(8);
        return true;
      }
      catch (FileNotFoundException)
      {
        ReleasePeer();
        lastOpenAttemptFailedUtc = DateTime.UtcNow;
        return false;
      }
      catch
      {
        ReleasePeer();
        lastOpenAttemptFailedUtc = DateTime.UtcNow;
        return false;
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private void ReleasePeer()
    {
      if (peerAccessor != null)
      {
        peerAccessor.Dispose();
        peerAccessor = null;
      }

      if (peerMmf != null)
      {
        peerMmf.Dispose();
        peerMmf = null;
      }
    }
  }
}