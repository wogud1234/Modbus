namespace Server.Modbus
{
  public class ModbusCodes
  {
    /*
      [기능 코드]	    [동작]	                  [대상 데이터]
      01	        Read Coils	              디지털 출력 (읽기)
      02	        Read Discrete Inputs	    디지털 입력 (읽기)
      03	        Read Holding Registers	  아날로그 출력 레지스터 (읽기)
      04	        Read Input Registers	    아날로그 입력 레지스터 (읽기)
      05	        Write Single Coil	        디지털 출력 1개 (쓰기)
      06	        Write Single Register	    레지스터 1개 (쓰기)
      0F(15)	    Write Multiple Coils	    디지털 출력 여러 개 (쓰기)
      10(16)	    Write Multiple Registers	레지스터 여러 개 (쓰기)
    */
    public static class ModbusFunctionCodes
    {
      public const byte ReadCoils = 0x01;
      public const byte ReadHoldingRegisters = 0x03;
      public const byte WriteSingleCoil = 0x05;
      public const byte WriteSingleRegister = 0x06;
      public const byte WriteMultipleRegisters = 0x10;
    }

    public static class ModbusExceptionCodes
    {
      public const byte IllegalFunction = 0x01;
      public const byte IllegalDataAddress = 0x02;
      public const byte IllegalDataValue = 0x03;
      public const byte SlaveDeviceFailure = 0x04;
    }
  }
}