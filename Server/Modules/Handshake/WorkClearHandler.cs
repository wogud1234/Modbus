using System;
using Server.Map;
using Server.Register;

namespace Server.Handshake
{
  public class WorkClearHandler
  {
    public event Action WorkClearRaised;
    public event Action WorkClearCleared;

    private readonly Store store;
    private readonly object sync = new object();
    private bool lastCmd;

    public WorkClearHandler(Store store)
    {
      if (store == null)
        throw new ArgumentNullException("store");

      this.store = store;
      this.lastCmd = store.GetCoil(Addresses.CoilWorkClearCmd);
    }

    public void OnCoilWritten(ushort offset, bool value)
    {
      if (offset != Addresses.CoilWorkClearCmd) return;

      lock (sync)
      {
        var previous = lastCmd;
        lastCmd = value;

        if (!previous && value)
        {
          store.SetCoil(Addresses.CoilWorkClearAck, true);
          WorkClearRaised?.Invoke();
        }
        else if (previous && !value)
        {
          store.SetCoil(Addresses.CoilWorkClearAck, false);
          WorkClearCleared?.Invoke();
        }
      }
    }
  }
}