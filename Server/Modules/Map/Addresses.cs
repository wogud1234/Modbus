using System;

namespace Server.Map
{
  /*
    [1-based 주소의 0-based Modbus offset. Coil 000001 → 0, Holding 400001 → 0.]
    ┌────────────────┬───────────────────────────────────────────┬────────────────┐                          
    │      표현       │                   방식                     │      예시       │                          
    ├────────────────┼───────────────────────────────────────────┼────────────────┤                          
    │ 1-based 주소    │ 사람이 읽는 주소 (KepServer 설정 화면 등)      │ 400001, 400017 │                          
    ├────────────────┼───────────────────────────────────────────┼────────────────┤                          
    │ 0-based offset │ 실제 Modbus TCP 패킷 안의 값                 │ 0, 16          │                          
    └────────────────┴───────────────────────────────────────────┴────────────────┘  
                                                                                              
    Holding 1-based 주소  →  0-based offset                                                                  
    400001  →  400001 - 400001 = 0                                                                           
    400017  →  400017 - 400001 = 16                                                                          
    400003  →  400003 - 400001 = 2                                                                           
    ---                                                                                                      
    KepServer → 이 앱 흐름                                                                                   
                                                                                                             
    KepServer가 400017번지를 읽고 싶으면 Modbus TCP 패킷에 16 을 담아서 보냄. 400001을 직접 보내는 게 아님.                                                                                                
                                                                                                             
    KepServer 설정: 400017                                                                                   
           ↓ (400017 - 400001 = 16으로 변환, KepServer가 자동으로 함)                                        
    Modbus 패킷: offset=16                                                                                   
           ↓                                                                                                 
    이 앱: Addresses.HoldingNgPart1 = 16  ← 그래서 이렇게 정의된 것                                          
                                                                                                             
    이 파일의 상수들이 전부 0-based offset인 이유가 바로 이것. 
  */
  public class Addresses
  {
    public const ushort CoilReady = 0;
    public const ushort CoilOpModeNormal = 1;
    public const ushort CoilOpModeManual = 2;
    public const ushort CoilStart = 3;
    public const ushort CoilStop = 4;
    public const ushort CoilDataReq = 5;
    public const ushort CoilDataCmd = 6;

    public const ushort CoilWorkClearCmd = 10; // NSCAN 내부 Work 수량 Clear CMD. 업체 운전 Coil(000001~000007)과 분리. 000011.
    public const ushort CoilWorkClearAck = 11; // Work Clear ACK. CMD rise→1, CMD fall→0. 000012.

    public const ushort CoilHeartbeat = 999;
    public const int CoilCount = 1000;

    public const ushort HoldingTotalTestCounter = 0; // 400001
    public const ushort HoldingOkWorkCounter = 2;    // 400003
    public const ushort HoldingRcWorkCounter = 4;    // 400005
    public const ushort HoldingNgWork1Counter = 6;   // 400007 · Group A
    public const ushort HoldingNgWork2Counter = 8;   // 400009 · Group B
    public const ushort HoldingNgWork3Counter = 10;  // 400011 · Group C
    public const ushort HoldingNgWork4Counter = 12;  // 400013 · Group D
    public const ushort HoldingNgWork5Counter = 14;  // 400015 · Group Side

    public const ushort HoldingNgPart1 = 16;  // 400017 Rupture (파열)
    public const ushort HoldingNgPart2 = 18;  // 400019 Height
    public const ushort HoldingNgPart3 = 20;  // 400021 Aperture (구멍) 직경/진원도
    public const ushort HoldingNgPart4 = 22;  // 400023 Aperture 결함크기
    public const ushort HoldingNgPart5 = 24;  // 400025 Inner1 얼룩
    public const ushort HoldingNgPart6 = 26;  // 400027 Inner1 긁힘
    public const ushort HoldingNgPart7 = 28;  // 400029 Inner1 찍힘
    public const ushort HoldingNgPart8 = 30;  // 400031 Inner2 얼룩
    public const ushort HoldingNgPart9 = 32;  // 400033 Inner2 긁힘
    public const ushort HoldingNgPart10 = 34; // 400035 Inner2 찍힘
    public const ushort HoldingNgPart11 = 36; // 400037 Outer 얼룩
    public const ushort HoldingNgPart12 = 38; // 400039 Outer 긁힘
    public const ushort HoldingNgPart13 = 40; // 400041 Outer 찍힘
    public const ushort HoldingNgPart14 = 42; // 400043 Side 얼룩
    public const ushort HoldingNgPart15 = 44; // 400045 Side 긁힘
    public const ushort HoldingNgPart16 = 46; // 400047 Side 찍힘

    public const int NgPartCount = 16;
    public const int NgPartStride = 2;

    public const int AppearanceTypeCount = 3; // 외관 유형 3종 (얼룩/긁힘/찍힘). 기타 Holding 없음.

    public const ushort HoldingOkWorkPercent = 48;  // 400049
    public const ushort HoldingRcWorkPercent = 50;  // 400051
    public const ushort HoldingNgWork1Percent = 52; // 400053
    public const ushort HoldingNgWork2Percent = 54; // 400055
    public const ushort HoldingNgWork3Percent = 56; // 400057 (표 연속 확장)
    public const ushort HoldingNgWork4Percent = 58; // 400059
    public const ushort HoldingNgWork5Percent = 60; // 400061

    public const int HoldingCount = 62; // 마지막 Percent Word(400061) + 1.

    public const int SideCameras = 3;         // 사이드 카메라 3대
    public const int SideRoiPerCamera = 4;    // 사이드 카메라 한 대당 촬영하는 캔 수
    public const int SideSlotCount = SideCameras * SideRoiPerCamera; // 12
    
    public static ushort NgPartOffset(int partIndex1Based)
    {
      if (partIndex1Based < 1 || partIndex1Based > NgPartCount)
        throw new ArgumentOutOfRangeException(nameof(partIndex1Based));
      return (ushort)(HoldingNgPart1 + (partIndex1Based - 1) * NgPartStride);
    }
  }
}