using System;
using Server.Map;

namespace Server.Register
{
  public class Store
  {
    private readonly bool[] coils;
    private readonly ushort[] holdings;
    
    // MES 공개 영역(m_holdings)의 Total/OK/RC/NG/ngPart 카운터는 60초 폴링 핸드셰이크(Data_CMD)에
    // 맞춰 매 윈도우마다 0으로 리셋됨(ClearMesWindowCounters). 그와 별개로 설비 자체의 누적 생산실적
    // (대시보드 등 내부 화면용)은 리셋되면 안 되므로, 같은 오프셋에 같은 델타를 동시에 반영하는
    // 미러(mirror) 배열을 둔다 — IncrementDWord를 통해서만 갱신됨.
    private readonly ushort[] accumHoldings;
    private readonly object sync = new object();
    
    public WordOrder WordOrder { get; set; }
    public int CoilCount => coils.Length;
    public int HoldingCount => holdings.Length;

    public Store(WordOrder order = WordOrder.ABCD)
      : this(order, Addresses.CoilCount, Addresses.HoldingCount) {}
    
    public Store(WordOrder order, int coilCount, int holdingCount)
    {
      if(coilCount < 1) 
        throw new ArgumentOutOfRangeException("coilCount");
      if(holdingCount < 1)
        throw new ArgumentOutOfRangeException("holdingCount");
      
      coils = new bool[coilCount];
      holdings = new ushort[holdingCount];
      accumHoldings = new ushort[holdingCount];
      WordOrder = order;
    }

    public bool GetCoil(ushort offset)
    {
      lock (sync)
      {
        EnsureCoil(offset);
        return coils[offset];
      }
    }

    public void SetCoil(ushort offset, bool value)
    {
      lock (sync)
      {
        EnsureCoil(offset);
        coils[offset] = value;
      }
    }

    public ushort GetHolding(ushort offset)
    {
      lock (sync)
      {
        EnsureHolding(offset);
        return holdings[offset];
      }
    }
    public void SetHolding(ushort offset, ushort value)
    {
      lock (sync)
      {
        EnsureHolding(offset);
        holdings[offset] = value;
      }
    }

    public void ToggleCoil(ushort offset)
    {
      lock (sync)
      {
        EnsureCoil(offset);
        coils[offset] = !coils[offset];
      }
    }

    public void ClearWorkCounters()
    {
      ClearMesWindowCounters();
      lock (sync)
      {
        WordOrderUtil.WriteDWord(accumHoldings, Addresses.HoldingTotalTestCounter, 0, WordOrder);
        WordOrderUtil.WriteDWord(accumHoldings, Addresses.HoldingOkWorkCounter, 0, WordOrder);
        WordOrderUtil.WriteDWord(accumHoldings, Addresses.HoldingRcWorkCounter, 0, WordOrder);
        WordOrderUtil.WriteDWord(accumHoldings, Addresses.HoldingNgWork1Counter, 0, WordOrder);
        WordOrderUtil.WriteDWord(accumHoldings, Addresses.HoldingNgWork2Counter, 0, WordOrder);
        WordOrderUtil.WriteDWord(accumHoldings, Addresses.HoldingNgWork3Counter, 0, WordOrder);
        WordOrderUtil.WriteDWord(accumHoldings, Addresses.HoldingNgWork4Counter, 0, WordOrder);
        WordOrderUtil.WriteDWord(accumHoldings, Addresses.HoldingNgWork5Counter, 0, WordOrder);
        for (var part = 1; part <= Addresses.NgPartCount; part++)
          WordOrderUtil.WriteDWord(accumHoldings, Addresses.NgPartOffset(part), 0, WordOrder);
      }
    }
    public void ClearMesWindowCounters()
    {
      SetDWord(Addresses.HoldingTotalTestCounter, 0);
      SetDWord(Addresses.HoldingOkWorkCounter, 0);
      SetDWord(Addresses.HoldingRcWorkCounter, 0);
      SetDWord(Addresses.HoldingNgWork1Counter, 0);
      SetDWord(Addresses.HoldingNgWork2Counter, 0);
      SetDWord(Addresses.HoldingNgWork3Counter, 0);
      SetDWord(Addresses.HoldingNgWork4Counter, 0);
      SetDWord(Addresses.HoldingNgWork5Counter, 0);
      for (var part = 1; part <= Addresses.NgPartCount; part++)
        SetDWord(Addresses.NgPartOffset(part), 0);
      RecalcPercents();
    }

    public void RecalcPercents()
    {
      lock (sync)
      {
        var total = WordOrderUtil.ReadDWord(holdings, Addresses.HoldingTotalTestCounter, WordOrder);
        var ok  = WordOrderUtil.ReadDWord(holdings,   Addresses.HoldingOkWorkCounter, WordOrder);
        var rc  = WordOrderUtil.ReadDWord(holdings,   Addresses.HoldingRcWorkCounter, WordOrder);
        var ng1 = WordOrderUtil.ReadDWord(holdings,   Addresses.HoldingNgWork1Counter, WordOrder);
        var ng2 = WordOrderUtil.ReadDWord(holdings,   Addresses.HoldingNgWork2Counter, WordOrder);
        var ng3 = WordOrderUtil.ReadDWord(holdings,   Addresses.HoldingNgWork3Counter, WordOrder);
        var ng4 = WordOrderUtil.ReadDWord(holdings,   Addresses.HoldingNgWork4Counter, WordOrder);
        var ng5 = WordOrderUtil.ReadDWord(holdings,   Addresses.HoldingNgWork5Counter, WordOrder);
  
        holdings[Addresses.HoldingOkWorkPercent] = ToPercentRaw(ok, total);
        holdings[Addresses.HoldingRcWorkPercent] = ToPercentRaw(rc, total);
        holdings[Addresses.HoldingNgWork1Percent] = ToPercentRaw(ng1, total);
        holdings[Addresses.HoldingNgWork2Percent] = ToPercentRaw(ng2, total);
        holdings[Addresses.HoldingNgWork3Percent] = ToPercentRaw(ng3, total);
        holdings[Addresses.HoldingNgWork4Percent] = ToPercentRaw(ng4, total);
        holdings[Addresses.HoldingNgWork5Percent] = ToPercentRaw(ng5, total);
      }
    }

    private static ushort ToPercentRaw(uint count, uint total)
    {
      if (total == 0) return 0;
      var raw = count * 10000UL / total;
      if (raw > 10000)
        raw = 10000;
      return (ushort)raw;
    }

    public void SetDWord(ushort offset, uint value)
    {
      lock (sync)
      {
        EnsureHolding((ushort)(offset + 1));
        WordOrderUtil.WriteDWord(holdings, offset, value, WordOrder);
      }
    }

    public uint GetDWord(ushort offset)
    {
      lock (sync)
      {
        EnsureHolding(offset);
        return WordOrderUtil.ReadDWord(holdings, offset, WordOrder);
      }
    }

    public uint IncrementDWord(ushort offset, uint delta)
    {
      lock (sync)
      {
        EnsureHolding((ushort)(offset + 1));
        var newWindowValue = WordOrderUtil.ReadDWord(holdings, offset, WordOrder) + delta;
        WordOrderUtil.WriteDWord(holdings, offset, newWindowValue, WordOrder);
        
        var newAccumValue = WordOrderUtil.ReadDWord(accumHoldings, offset, WordOrder) + delta;
        WordOrderUtil.WriteDWord(accumHoldings, offset, newAccumValue, WordOrder);
        
        return newWindowValue;
      }
    }

    public uint GetAccumDWord(ushort offset)
    {
      lock (sync)
      {
        EnsureHolding((ushort)(offset + 1));
        return WordOrderUtil.ReadDWord(accumHoldings, offset, WordOrder);
      }
    }

    public void SetAccumDWord(ushort offset, uint value)
    {
      lock (sync)
      {
        EnsureHolding((ushort)(offset + 1));
        WordOrderUtil.WriteDWord(accumHoldings, offset, value, WordOrder);
      }
    }

    // 패킷 구조 :  [Function Code 01][Start Address 2바이트][Quantity of Coils 2바이트]
    public void ReadCoils(ushort start, ushort quantity, bool[] destination, int destIndex)
    {
      if(destination == null) throw new ArgumentNullException("destination");

      lock (sync)
      {
        for (var i = 0; i < quantity; i++)
        {
          var offset = start + i;
          EnsureCoil((ushort)offset);
          destination[destIndex + i] = coils[offset];
        }
      }
    }
    public void ReadHoldings(ushort start, ushort quantity, ushort[] destination, int destIndex)
    {
      if (destination == null)
        throw new ArgumentNullException("destination");

      lock (sync)
      {
        for (var i = 0; i < quantity; i++)
        {
          var offset = start + i;
          EnsureHolding((ushort)offset);
          destination[destIndex + i] = holdings[offset];
        }
      }
    }
    private void EnsureCoil(ushort offset)
    {
      if (offset >= coils.Length)
        throw new ArgumentOutOfRangeException("offset", "Coil offset out of range.");
    }

    private void EnsureHolding(ushort offset)
    {
      if (offset >= holdings.Length)
        throw new ArgumentOutOfRangeException("offset", "Holding offset out of range.");
    }
  }
}