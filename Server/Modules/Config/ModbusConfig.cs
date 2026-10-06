using System;
using Server.Map;
using Server.Register;

namespace Server.Config
{
  public class ModbusConfig
  {
    public string ListenIp { get; set; }
    public int Port { get; set; }
    public byte UnitId { get; set; }
    public WordOrder WordOrder { get; set; }
    public ushort[] AllowWriteCoilOffsets { get; set; }
    public string[] AllowMasterIps { get; set; }

    public ModbusConfig()
    {
      ListenIp = "127.0.0.1";
      Port = 8010;
      UnitId = 1;
      WordOrder = WordOrder.ABCD;
      AllowWriteCoilOffsets = new[] { Addresses.CoilDataCmd, Addresses.CoilWorkClearCmd };
      AllowMasterIps = null;
    }

    public static ModbusConfig CreateLocalTest()
    {
      return new ModbusConfig
      {
        AllowMasterIps = new[] { "127.0.0.1", "::1" }
      };
    }
    
    public static ModbusConfig CreateFieldDefault()
    {
      return new ModbusConfig
      {
        ListenIp                = "10.0.2.164",
        Port = 8010,
        UnitId = 1,
        WordOrder = WordOrder.ABCD,
        AllowWriteCoilOffsets = new[] { Addresses.CoilDataCmd, Addresses.CoilWorkClearCmd },
        AllowMasterIps = new[] { "10.0.2.1", "10.0.2.2" }
      };
    }

    public bool IsCoilWriteAllowed(ushort offset)
    {
      if (AllowWriteCoilOffsets == null) return false;
      for(var i =0; i<AllowWriteCoilOffsets.Length; i++)
        if (AllowWriteCoilOffsets[i] == offset)
          return true;
      return false;
    }

    public bool IsMasterIpAllowed(string remoteIp)
    {
      if (AllowMasterIps == null || AllowMasterIps.Length == 0) return false;
      if (string.IsNullOrEmpty(remoteIp)) return false;

      var ip = remoteIp.Trim();
      if(ip.StartsWith("::fff:", StringComparison.OrdinalIgnoreCase))
        ip = ip.Substring(7);

      for (var i = 0; i < AllowMasterIps.Length; i++)
      {
        var allowed = AllowMasterIps[i];
        if (string.IsNullOrEmpty(allowed)) continue;
        if (string.Equals(ip, allowed.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
      }
      return false;
    }
  }
}