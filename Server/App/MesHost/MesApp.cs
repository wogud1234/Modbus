using System;
using System.IO;
using System.Threading;
using NscanMes.Core.Persist;
using Server.Config;
using Server.Handshake;
using Server.Map;
using Server.Mmf;
using Server.Modbus;
using Server.Register;

namespace Server.App
{
  public class MesApp : IDisposable
  {
    public event Action<string> Log;
    
    private readonly MesBridgeOptions bridgeOptions;
    private readonly DataCmdHandler handshake;
    private readonly WorkClearHandler workClear;

    private readonly MesNgPartLayout ngParts;
    private readonly MesWorkPersist persist;
    private readonly ModbusTcpSlave slave;

    private MmfBridge bridge;
    
    private MesHeartbeatManager hbControlApp;
    private MesHeartbeatManager hbVisionApp;
    
    
    private MesHeartbeatManager hbMesControl;
    private MesHeartbeatManager hbMesVision;
    private MesInterface ifControlApp;
    private MesInterface ifVisionApp;
    private MesInterface ifMesControl;
    private MesInterface ifMesVision;
    
    private Timer heartbeatTimer;
    private bool started;

    public MesHostParams HostParams { get; }
    public Store Store { get; }
    public ModbusConfig ModbusConfig { get; }
    public bool IsVisionPeerAlive => bridge != null && bridge.IsVisionPeerAlive;
    public bool IsControlPeerAlive => bridge != null && bridge.IsControlPeerAlive;
    
    public MesApp(string paramsPath = null)
    {
      var exeDir = AppDomain.CurrentDomain.BaseDirectory + "Parameter"; 
      // exeDir -> "D:\.net\ModSlave\Server\bin\Debug\Parameter"

      if (string.IsNullOrWhiteSpace(paramsPath))
        paramsPath = ResolveParamsPath(exeDir);   
      
      // paramsPath = "D:\.net\ModSlave\setup\Parameter\Mes.Host.params.json" 
      HostParams = MesHostParameterLoader.LoadOrDefault(paramsPath, exeDir);
      // HostParams.SourcePath = "D:\.net\ModSlave\setup\Parameter\Mes.Host.params.json" 
      
      ModbusConfig = MesHostParameterLoader.ToModbusConfig(HostParams); // HostParams.Modbus 데이터 가공
      bridgeOptions = MesBridgeOptions.Fromparams(HostParams);          // HostParams.Bridge 데이터 가공
      var mapRuntime = MesMapRuntime.FromParams(HostParams);            // HostParams.Map 데이터 가공
      
      Store = new Store(ModbusConfig.WordOrder, mapRuntime.CoilCount, mapRuntime.HoldingCount);
      ngParts = mapRuntime.NgParts;

      // persistPath = "D:\.net\ModSlave\Server\bin\Debug\Parameter\mes-work-accum.bin"
      var persistPath = Path.Combine(exeDir, MesWorkPersist.DefaultFileName);
      persist = new MesWorkPersist(persistPath);

      handshake = new DataCmdHandler(Store);
      handshake.DataCmdRaised += OnDataCmdRaised;
      handshake.DataCmdCleared += OnDataCmdCleared;

      workClear = new WorkClearHandler(Store);
      workClear.WorkClearRaised += OnWorkClearRaised;
      workClear.WorkClearCleared += OnWorkClearCleared;

      slave = new ModbusTcpSlave(ModbusConfig, Store, handshake);
      slave.WorkClear = workClear;
      slave.Log += msg => RaiseLog("Modbus: " + msg);
    }

    private void OnWorkClearCleared()
    {
      RaiseLog("WorkClear_CMD=0 -> ACK=0");
    }

    /*
      [OnWorkClearRaised() 흐름]
        Store.ClearWorkCounters()        →  holdings, accumHoldings = 0                                                           
            ↓                                                                                                                 
        bridge.NotifyWorkChanged()                                                                                                
                ↓                                                                                                                 
        PersistAndPublishWork()                                                                                                   
                ↓                                                                                                                 
        persist.SaveFrom(store)          →  파일에 0 저장                                                                         
        PublishWorkSnapshot()            →  MMF "MES Control IF"에 0 씀    
    */
    private void OnWorkClearRaised()
    {
      RaiseLog("WorkClear_CMD=1 -> Work/ngPart=0");
      Store.ClearWorkCounters();
      if(bridge != null)
        bridge.NotifyWorkChanged();

      // WorkClear는 "설비 실적 전체를 초기화해" 라는 명령이에요.
      // 재시작해도 0이 유지돼야 하니까 파일(persist)에도 0을 써야 해요.
      // 안 쓰면 재시작 시 파일에서 이전 값이 복원되버려요.

      // [Before]
      // KepServer가 CMD=1만 쓰고 CMD=0을 다시 쓰지 않아서
      // WorkClear_CMD, WorkClear_ACK가 영구적으로 1에 머물렀음.

      // [After]
      // 500ms 후 슬레이브가 자동 리셋 → WorkClear_ACK=0 + Cleared 이벤트 정상 발화.
      System.Threading.Timer t = null;
      t = new System.Threading.Timer(_ =>
      {
        t?.Dispose();
        Store.SetCoil(Addresses.CoilWorkClearCmd, false);
        workClear.OnCoilWritten(Addresses.CoilWorkClearCmd, false);
      }, null, 500, System.Threading.Timeout.Infinite);
    }

    /*
      [OnDataCmdCleared(), OnDataCmdRaised() 흐름]
        KepServerEX6 → DataCmd 코일 = 1 씀                                                                                        
           ↓                                                                                                                  
        OnDataCmdRaised()                                                                                                         
               ↓                                                                                                                  
        TrySetMesCmd(MesCmd_Idle)  →  MMF "MES Vision IF", "MES Control IF" 에 씀 
        
        DataCmd 0 → 1  (Raised)  →  카운터 리셋 + DataRequest(1) MMF에 씀
        DataCmd 1 → 0  (Cleared) →  카운터 리셋 + Idle(0) MMF에 씀                                                                
        DataCmd가 올라갈 때 요청 신호를 보내고, 내려갈 때 Idle로 복귀시키는 펄스 구조예요. 
        
        Raised  → "Data_CMD=1 -> Data_REQ=1 (clear NG coils)"                                                                     
        Cleared → "Data_CMD=0 -> Data_REQ=0 (MES Work Holding 윈도우 리셋)"                                                       
                                                                                                                                  
        Raised(1) 때 리셋 → 새 윈도우 시작 준비, 이전 데이터 지움                                                                 
        Cleared(0) 때 리셋 → 윈도우 종료, 다시 깨끗하게 비움                                                                      
                                                                                                                                  
        즉 윈도우 시작할 때도, 끝날 때도 카운터를 0으로 초기화하는 거예요.                                                        
                                                                                                                                  
        카운터=0                                                                                                                  
          DataCmd=1 (Raised)  → 카운터 리셋, 새 윈도우 시작, 데이터 쌓기 시작                                                     
          DataCmd=0 (Cleared) → 카운터 리셋, 윈도우 종료, 다음 윈도우 대기                                                        
        카운터=0                                                                                                                  
                                                                                                                                  
        KepServerEX6가 데이터를 읽어가는 타이밍이 DataCmd=1 구간 안이라서, 양쪽에서 리셋해서 항상 깨끗한 상태로 만드는 거예요.
    */
    private void OnDataCmdCleared()
    {
      RaiseLog("Data_CMD=0 -> Data_REQ=0 (MES Work Holding 윈도우 리셋)");
      Store.ClearMesWindowCounters();
      // → holdings[] 카운터 전부 0으로 리셋
      
      TrySetMesCmd(MesInterProcessLayout.MesCmd_Idle);
      // → MMF에 0 씀                                                                                                           
      // → "요청 끝났어, 대기 상태" 를 Vision/Control에 알림.                                                                                   
    }
    
    private void OnDataCmdRaised()
    {
      RaiseLog("Data_CMD=1 -> Data_REQ=1 (clear NG coils)");
      Store.ClearMesWindowCounters();

      // [Before]
      // TrySetMesCmd(MesInterProcessLayout.MesCmd_DataRequest);
      // → KepServer가 CMD=1만 쓰고 CMD=0을 다시 쓰지 않아서
      //   Data_CMD, Data_REQ가 영구적으로 1에 머물렀음.

      // [After]
      // KepServer는 CMD=1을 쓴 뒤 CMD=0을 다시 쓰지 않으므로, 슬레이브가 500ms 후 자동 리셋.
      // 하강 에지 → handshake.OnCoilWritten → Data_REQ=0 + Cleared 이벤트 정상 발화.
      TrySetMesCmd(MesInterProcessLayout.MesCmd_DataRequest);
      System.Threading.Timer t = null;
      t = new System.Threading.Timer(_ =>
      {
        t?.Dispose();
        Store.SetCoil(Addresses.CoilDataCmd, false);
        handshake.OnCoilWritten(Addresses.CoilDataCmd, false);
      }, null, 500, System.Threading.Timeout.Infinite);
    }

    private void TrySetMesCmd(ushort cmd)
    {
      try
      {
        if(ifMesVision != null && ifMesVision.IsPeerAlive())
          ifMesVision.SetMesCmd(cmd);
        if(ifMesControl != null && ifMesControl.IsPeerAlive())
          ifMesControl.SetMesCmd(cmd);
      }
      catch (Exception ex)
      {
        RaiseLog("MesCmd notify: " + ex.Message);
      }
    }

    private void RaiseLog(string message)
    {
      var handler = Log;
      if (handler != null)
        handler(message);
    }
    
    private string ResolveParamsPath(string exeDir) 
    {
      var name = MesHostParameterLoader.FileName;
      var local = Path.Combine(exeDir, name);
      // exeDir -> "D:\.net\ModSlave\Server\bin\Debug\Parameter"
      // local -> "D:\.net\ModSlave\Server\bin\Debug\Parameter\Mes.Host.params.json"
      if (File.Exists(local))
        return local;

      var dir = exeDir;
      for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
      {
        var setup = Path.Combine(dir, "setup", "Parameter", name);
        if (File.Exists(setup))
          return setup;
        var parent = Directory.GetParent(dir);
        /*
          [탐색 과정]
            "D:\.net\ModSlave\Server\bin\Debug"
            "D:\.net\ModSlave\Server\bin"
            "D:\.net\ModSlave\Server"
            "D:\.net\ModSlave"
            
            최종: setup = "D:\.net\ModSlave\setup\Parameter\Mes.Host.params.json" 
        */
        if (parent == null)
          break;
        dir = parent.FullName;
      }

      return null;
    }


    public void Start()
    {
      if (started) return;
      started = true;

      hbMesVision = MesHeartbeatManager.ForMesTowardVision();
      hbMesControl = MesHeartbeatManager.ForMesTowardControl();
      hbMesVision.Start();
      hbMesControl.Start();

      ifMesVision = MesInterface.ForMesTowardVision(hbMesVision);
      ifMesControl = MesInterface.ForMesTowardControl(hbMesControl);

      bridge = new MmfBridge(Store, ifMesVision, ifMesControl, bridgeOptions, ngParts, persist);
      bridge.Log += RaiseLog;
      bridge.Start();

      slave.Start();
      RaiseLog(string.Format("MesApp started. Modbus {0}:{1}", ModbusConfig.ListenIp, ModbusConfig.Port));
    }

    public void Stop()
    {
      if (!started) return;
      started = false;

      bridge?.Stop();
      slave?.Stop();
      hbMesVision?.Stop();
      hbMesControl?.Stop();
      RaiseLog("MesApp stopped.");
    }

    public void Dispose()
    {
      Stop();
      bridge?.Dispose();
      hbMesVision?.Dispose();
      hbMesControl?.Dispose();
      ifMesVision?.Dispose();
      ifMesControl?.Dispose();
      slave?.Dispose();
    }
  }
}