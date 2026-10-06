using System;
using System.IO.MemoryMappedFiles;

namespace Server.Mmf
{
  public class MesMmfClear
  {
    public static void ZeroRegion(MemoryMappedViewAccessor accessor, int byteSize)
    {
      if (accessor == null)
        throw new ArgumentNullException("accessor");
      if (byteSize < 0)
        throw new ArgumentOutOfRangeException("byteSize");

      for (var i = 0; i < byteSize; i++)
        accessor.Write(i, (byte)0);
    }

    public static void ClearHeartbeatRegion(MemoryMappedViewAccessor accessor)
    {
      ZeroRegion(accessor, MesMmfDefine.HeartbeatByteSize);
    }

    public static void ClearInterfaceRegion(MemoryMappedViewAccessor accessor)
    {
      ZeroRegion(accessor, MesMmfDefine.InterfaceByteSize);
      accessor.Write(MesInterProcessLayout.Offset_Version, MesInterProcessLayout.SchemaVersion);
    }
  }
}