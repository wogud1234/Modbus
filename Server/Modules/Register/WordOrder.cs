using System;

namespace Server.Register
{
  public enum WordOrder
  {
    ABCD = 0,
    CDAB = 1
  }

  public class WordOrderUtil
  {
    public static void WriteDWord(ushort[] holdings, int offset, uint value, WordOrder order)
    {
      if(holdings == null)
        throw new ArgumentNullException("holdings");
      if(offset < 0 || offset + 1 > holdings.Length)
        throw new ArgumentOutOfRangeException("offset");

      var high = (ushort)((value >> 16) & 0xFFFF);
      var low = (ushort)((value & 0xFFFF));

      if (order == WordOrder.ABCD)
      {
        holdings[offset] = high;
        holdings[offset + 1] = low;
      }
      else
      {
        holdings[offset] = low;
        holdings[offset + 1] = high;
      }
    }

    public static uint ReadDWord(ushort[] holdings, int offset, WordOrder order)
    {
      if(holdings == null)
        throw new ArgumentNullException("holdings");
      if(offset < 0 || offset + 1 > holdings.Length)
        throw new ArgumentOutOfRangeException("offset");

      var w0 = holdings[offset];
      var w1 = holdings[offset + 1];

      if (order == WordOrder.ABCD)
        return ((uint)w0 << 16) | w1;

      return ((uint)w1 << 16) | w0;
    }
  }
}