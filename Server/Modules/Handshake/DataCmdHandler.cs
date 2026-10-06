using System;
using System.CodeDom;
using Server.Map;
using Server.Register;

namespace Server.Handshake
{
  public class DataCmdHandler
  {
    public event Action DataCmdRaised;
    public event Action DataCmdCleared;

    private readonly Store store;
    private readonly object sync = new object();
    private bool lastCmd;

    public DataCmdHandler(Store store)
    {
      if(store == null) throw new ArgumentNullException("store");
      
      this.store = store;
      this.lastCmd = store.GetCoil(Addresses.CoilDataCmd);
    }

    // 핸드셰이크 규칙 (주석 그대로)                                                                                                
    // - Data_CMD가 0에서 1로 바뀌면  → Data_REQ를 1로 세팅.                                                                   
    // - Data_CMD가 1에서 0으로 바뀌면 → Data_REQ를 0으로 세팅.
    public void OnCoilWritten(ushort offset, bool value)
    {
      if (offset != Addresses.CoilDataCmd) return;

      lock (sync)
      {
        var previous = lastCmd;
        lastCmd = value;

        if (!previous && value)       // 상승 에지(0→1)
        {
          store.SetCoil(Addresses.CoilDataReq, true);
          DataCmdRaised?.Invoke();
        }
        else if (previous && !value)  // 하강 에지(1→0)
        {
          store.SetCoil(Addresses.CoilDataReq, false);
          DataCmdCleared?.Invoke();
        }
      }
    }
  }
}