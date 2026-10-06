using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Server.Config;
using Server.Handshake;
using Server.Register;

namespace Server.Modbus
{
  public class ModbusTcpSlave : IDisposable
  {
    public event Action<string> Log;

    private readonly object sync = new object();
    private readonly ModbusConfig config;
    private readonly DataCmdHandler handshake;

    private Thread accept;
    private TcpListener listener;
    private volatile bool running;

    public Store Store { get; set; }
    public WorkClearHandler WorkClear { get; set; }

    public ModbusTcpSlave(ModbusConfig config, Store store, DataCmdHandler handshake)
    {
      if (config == null) throw new ArgumentNullException("config");
      if (store == null) throw new ArgumentNullException("store");
      if (handshake == null) throw new ArgumentNullException("handshake");

      this.config = config;
      Store = store;
      this.handshake = handshake;
      WorkClear = new WorkClearHandler(store);
    }
// 192.168.219.100
    public void Start()
    {
      if (running) return;

      IPAddress ip;
      if (string.IsNullOrWhiteSpace(config.ListenIp)
          || string.Equals(config.ListenIp, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
          || string.Equals(config.ListenIp, "*", StringComparison.OrdinalIgnoreCase))
        ip = IPAddress.Any;
      else
        ip = IPAddress.Parse(config.ListenIp);

      listener = new TcpListener(ip, config.Port);
      listener.Start();
      running = true;

      accept = new Thread(AcceptLoop)
      {
        IsBackground = true,
        Name = "ModbusTcpSlave.Accept"
      };
      accept.Start();
      Log?.Invoke($"Listening {ip}:{config.Port} UnitId={config.UnitId}");
    }

    private void AcceptLoop()
    {
      while (running)
      {
        try
        {
          TcpClient client = listener.AcceptTcpClient();
          ThreadPool.QueueUserWorkItem(HandleClient, client);
        }
        catch (SocketException)
        {
          if (!running)
            break;
        }
        catch (ObjectDisposedException)
        {
          break;
        }
        catch (Exception e)
        {
          Log?.Invoke($"Accept error: {e.Message}");
        }
      }
    }

    private void HandleClient(object state)
    {
      TcpClient client = (TcpClient)state;
      string remote = "?";
      try
      {
        remote = client.Client.RemoteEndPoint != null ? client.Client.RemoteEndPoint.ToString() : "?";
        var remoteIp = ExtractIp(client.Client.RemoteEndPoint as IPEndPoint);
        if (!config.IsMasterIpAllowed(remoteIp))
        {
          Log?.Invoke($"Client rejected (allowMasterIps): {remote}");
          try
          {
            client.Close();
          }
          catch
          {
          }

          return;
        }

        Log?.Invoke($"Client connected: {remote}");

        using (client)
        using (NetworkStream stream = client.GetStream())
        {
          client.NoDelay = true;
          byte[] header = new byte[7];

          while (running && client.Connected)
          {
            if (!ReadExact(stream, header, 0, 7))
              break;

            var protocolId = (ushort)((header[2] << 8) | header[3]);
            var length = (ushort)((header[4] << 8) | header[5]);
            var unitId = header[6];

            if (protocolId != 0 || length < 1)
              break;

            var pduLen = length - 1;
            if (pduLen < 1 || pduLen > 256)
              break;

            var pdu = new byte[pduLen];
            if (!ReadExact(stream, pdu, 0, pduLen))
              break;

            var transactionId = (ushort)((header[0] << 8) | header[1]);

            if (unitId != config.UnitId && unitId != 0)
              continue;

            byte[] responsePdu = ProcessPdu(pdu);
            WriteMbap(stream, transactionId, unitId, responsePdu);
          }
        }
      }
      catch (IOException)
      {
      }
      catch (Exception e)
      {
        Log?.Invoke($"Client error ({remote}): {e.Message}");
      }
      finally
      {
        Log?.Invoke($"Client disconnected: {remote}");
      }
    }

    private byte[] ProcessPdu(byte[] pdu)
    {
      if (pdu == null || pdu.Length < 1)
        return ExceptionPdu(0, ModbusCodes.ModbusExceptionCodes.IllegalFunction);

      byte fc = pdu[0];

      try
      {
        switch (fc)
        {
          case ModbusCodes.ModbusFunctionCodes.ReadCoils: // FC01
            return HandleReadCoils(pdu);
          case ModbusCodes.ModbusFunctionCodes.ReadHoldingRegisters: // FC03
            return HandleReadHoldings(pdu);
          case ModbusCodes.ModbusFunctionCodes.WriteSingleCoil: // FC05
            return HandleWriteSingleCoil(pdu);
          case ModbusCodes.ModbusFunctionCodes.WriteSingleRegister: // FC06
            return HandleWriteSingleRegister(pdu);
          case ModbusCodes.ModbusFunctionCodes.WriteMultipleRegisters: // FC10
            return HandleWriteMultipleRegisters(pdu);
          default:
            return ExceptionPdu(fc, ModbusCodes.ModbusExceptionCodes.IllegalFunction);
        }
      }
      catch (ArgumentOutOfRangeException)
      {
        return ExceptionPdu(fc, ModbusCodes.ModbusExceptionCodes.IllegalDataAddress);
      }
      catch (InvalidOperationException)
      {
        return ExceptionPdu(fc, ModbusCodes.ModbusExceptionCodes.IllegalDataValue);
      }
      catch (Exception)
      {
        return ExceptionPdu(fc, ModbusCodes.ModbusExceptionCodes.SlaveDeviceFailure);
      }
    }

    private byte[] HandleReadCoils(byte[] pdu)
    {
      if (pdu.Length < 5)
        return ExceptionPdu(ModbusCodes.ModbusFunctionCodes.ReadCoils,
          ModbusCodes.ModbusExceptionCodes.IllegalDataValue);

      var start = (ushort)((pdu[1] << 8) | pdu[2]);
      var quantity = (ushort)((pdu[3] << 8) | pdu[4]);

      if (quantity < 1 || quantity > 2000)
        return ExceptionPdu(ModbusCodes.ModbusFunctionCodes.ReadCoils,
          ModbusCodes.ModbusExceptionCodes.IllegalDataValue);
      if (start + quantity > Store.CoilCount)
        return ExceptionPdu(ModbusCodes.ModbusFunctionCodes.ReadCoils,
          ModbusCodes.ModbusExceptionCodes.IllegalDataAddress);

      var bits = new bool[quantity];
      Store.ReadCoils(start, quantity, bits, 0);

      // Modbus의 Coil 같은 1비트 데이터 quantity개 중 8개씩을 바이트 배열의 한 칸에 담기
      var byteCount = (quantity + 7) / 8;
      var response = new byte[2 + byteCount];
      response[0] = ModbusCodes.ModbusFunctionCodes.ReadCoils;
      response[1] = (byte)byteCount;

      for (var i = 0; i < quantity; i++)
        if (bits[i])
          response[2 + i / 8] |= (byte)(1 << (i % 8));

      /*
        예시로 Coil 10개, bits = [ON, OFF, ON, ON, OFF, ON, OFF, ON, ON, OFF]
        ---

        시작 상태
        response[2] = 0000 0000
        response[3] = 0000 0000

        ---

        i=0, bits[0]=ON
        1 << (0 % 8) = 1 << 0 = 0000 0001

        response[2]  0000 0000
                 |=  0000 0001
                 =   0000 0001

        ---
        i=1, bits[1]=OFF → skip

        ---

        i=2, bits[2]=ON
        1 << (2 % 8) = 1 << 2 = 0000 0100

        response[2]  0000 0001
                 |=  0000 0100
                 =   0000 0101

        ---

        i=3, bits[3]=ON
        1 << (3 % 8) = 1 << 3 = 0000 1000

        response[2]  0000 0101
                 |=  0000 1000
                 =   0000 1101

        ---

        i=4, bits[4]=OFF → skip

        ---

        i=5, bits[5]=ON
        1 << (5 % 8) = 1 << 5 = 0010 0000

        response[2]  0000 1101
                 |=  0010 0000
                 =   0010 1101

        ---

        i=6, bits[6]=OFF → skip

        ---

        i=7, bits[7]=ON
        1 << (7 % 8) = 1 << 7 = 1000 0000

        response[2]  0010 1101
                 |=  1000 0000
                 =   1010 1101   ← response[2] 완성

        ---

        i=8, bits[8]=ON  ← 바이트 경계 넘어감
        2 + (8 / 8) = 2 + 1 → response[3] 으로 이동
        1 << (8 % 8) = 1 << 0 = 0000 0001

        response[3]  0000 0000
                 |=  0000 0001
                 =   0000 0001

        ---

        i=9, bits[9]=OFF → skip

        ---

        최종
        response[2] = 1010 1101  (Coil 7,5,3,2,0 이 ON)
        response[3] = 0000 0001  (Coil 8 이 ON
      */
      return response;
    }

    private byte[] HandleReadHoldings(byte[] pdu)
    {
      if (pdu.Length < 5)
        return ExceptionPdu(ModbusCodes.ModbusFunctionCodes.ReadHoldingRegisters,
          ModbusCodes.ModbusExceptionCodes.IllegalDataValue);

      var start = (ushort)((pdu[1] << 8) | pdu[2]);
      var quantity = (ushort)((pdu[3] << 8) | pdu[4]);

      if (quantity < 1 || quantity > 125)
        return ExceptionPdu(ModbusCodes.ModbusFunctionCodes.ReadHoldingRegisters,
          ModbusCodes.ModbusExceptionCodes.IllegalDataValue);
      if (start + quantity > Store.HoldingCount)
        return ExceptionPdu(ModbusCodes.ModbusFunctionCodes.ReadHoldingRegisters,
          ModbusCodes.ModbusExceptionCodes.IllegalDataAddress);

      var values = new ushort[quantity];
      Store.ReadHoldings(start, quantity, values, 0);

      var response = new byte[2 + quantity * 2];
      response[0] = ModbusCodes.ModbusFunctionCodes.ReadHoldingRegisters;
      response[1] = (byte)(quantity * 2);

      for (var i = 0; i < quantity; i++)
      {
        response[2 + i * 2] = (byte)(values[i] >> 8);
        response[3 + i * 2] = (byte)(values[i] & 0xFF);
      }

      return response;
    }

    private byte[] HandleWriteSingleCoil(byte[] pdu)
    {
      if (pdu.Length < 5)
        return ExceptionPdu(ModbusCodes.ModbusFunctionCodes.WriteSingleCoil,
          ModbusCodes.ModbusExceptionCodes.IllegalDataValue);

      var offset = (ushort)((pdu[1] << 8) | pdu[2]);
      var raw = (ushort)((pdu[3] << 8) | pdu[4]);

      if (raw != 0x0000 && raw != 0xFF00)
        return ExceptionPdu(ModbusCodes.ModbusFunctionCodes.WriteSingleCoil,
          ModbusCodes.ModbusExceptionCodes.IllegalDataValue);

      if (offset >= Store.CoilCount)
        return ExceptionPdu(ModbusCodes.ModbusFunctionCodes.WriteSingleCoil,
          ModbusCodes.ModbusExceptionCodes.IllegalDataAddress);

      if (config.IsCoilWriteAllowed(offset) == false)
        return ExceptionPdu(ModbusCodes.ModbusFunctionCodes.WriteSingleCoil,
          ModbusCodes.ModbusExceptionCodes.IllegalDataAddress);

      var value = raw == 0xFF00;
      Store.SetCoil(offset, value);
      handshake.OnCoilWritten(offset, value);
      if (WorkClear != null)
        WorkClear.OnCoilWritten(offset, value);

      // echo request
      return new[]
      {
        ModbusCodes.ModbusFunctionCodes.WriteSingleCoil,
        pdu[1], pdu[2], pdu[3], pdu[4]
      };
    }

    // 단일 레지스터 쓰기 (FC 06) 처리
    private byte[] HandleWriteSingleRegister(byte[] pdu)
    {
      if (pdu.Length < 5)
        return ExceptionPdu(0x06, ModbusCodes.ModbusExceptionCodes.IllegalDataValue);

      var offset = (ushort)((pdu[1] << 8) | pdu[2]);
      var value = (ushort)((pdu[3] << 8) | pdu[4]);

      if (offset >= Store.HoldingCount)
        return ExceptionPdu(0x06, ModbusCodes.ModbusExceptionCodes.IllegalDataAddress);

      // 실제 메모리(RegisterStore)에 값 기록
      Store.SetHolding(offset, value);

      // Request를 그대로 Echo (정상 응답 규격)
      return new byte[] { 0x06, pdu[1], pdu[2], pdu[3], pdu[4] };
    }

    // 다중 레지스터 쓰기 (FC 16) 처리
    private byte[] HandleWriteMultipleRegisters(byte[] pdu)
    {
      if (pdu.Length < 6)
        return ExceptionPdu(0x10, ModbusCodes.ModbusExceptionCodes.IllegalDataValue);

      var start = (ushort)((pdu[1] << 8) | pdu[2]);
      var quantity = (ushort)((pdu[3] << 8) | pdu[4]);
      var byteCount = pdu[5];

      if (pdu.Length < 6 + byteCount)
        return ExceptionPdu(0x10, ModbusCodes.ModbusExceptionCodes.IllegalDataValue);

      if (start + quantity > Store.HoldingCount)
        return ExceptionPdu(0x10, ModbusCodes.ModbusExceptionCodes.IllegalDataAddress);

      // 실제 메모리(RegisterStore)에 값들 기록
      for (var i = 0; i < quantity; i++)
      {
        var value = (ushort)((pdu[6 + i * 2] << 8) | pdu[7 + i * 2]);
        Store.SetHolding((ushort)(start + i), value);
      }

      // [FC] + [시작 주소(2)] + [수량(2)] 응답
      return new byte[] { 0x10, pdu[1], pdu[2], pdu[3], pdu[4] };
    }

    private static void WriteMbap(NetworkStream stream, ushort transactionId, byte unitId, byte[] pdu)
    {
      var length = (ushort)(1 + pdu.Length);
      var frame = new byte[6 + length];
      frame[0] = (byte)(transactionId >> 8);
      frame[1] = (byte)(transactionId & 0xFF);
      frame[2] = 0;
      frame[3] = 0;
      frame[4] = (byte)(length >> 8);
      frame[5] = (byte)(length & 0xFF);
      frame[6] = unitId;
      Buffer.BlockCopy(pdu, 0, frame, 7, pdu.Length);
      stream.Write(frame, 0, frame.Length);
    }

    private static byte[] ExceptionPdu(byte functionCode, byte exceptionCode)
    {
      /*
        [0x80의 의미]

        Modbus 프로토콜 약속입니다. 슬레이브가 에러 응답을 보낼 때 기능 코드에 0x80을 OR해서 돌려보내도록 명세에 정해져
        있어요.

        정상 응답: 기능 코드 그대로    예) FC 03 → 0x03
        에러 응답: 기능 코드 | 0x80   예) FC 03 → 0x83

        왜 | 연산자를 쓰나

        0x80은 2진수로 10000000입니다. OR 연산은 최상위 비트(MSB, Most Significant Bit)만 1로 켜고 나머지는 그대로
        유지합니다.

        FC 03  =  0000 0011
        0x80   =  1000 0000
        OR     =  1000 0011  =  0x83
      */
      return new[] { (byte)(functionCode | 0x80), exceptionCode };
    }

    private static bool ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
    {
      var readTotal = 0;
      while (readTotal < count)
      {
        var n = stream.Read(buffer, offset + readTotal, count - readTotal); // 블로킹(blocking) 호출. 데이터가 올 때까지 그 자리에서 기다림.
        if (n <= 0) return false;
        readTotal += n;
      }

      return true;
    }

    private static string ExtractIp(IPEndPoint ep)
    {
      if (ep == null || ep.Address == null)
        return string.Empty;
      var addr = ep.Address;
      if (addr.IsIPv4MappedToIPv6)
        addr = addr.MapToIPv4();
      return addr.ToString();
    }

    public void Dispose()
    {
      Stop();
    }

    public void Stop()
    {
      running = false;

      try
      {
        if (listener != null)
          listener.Stop();
      }
      catch
      {
      }

      if (accept != null && accept.IsAlive)
        accept.Join(1000);

      listener = null;
      Log?.Invoke("Stopped");
    }
  }
}