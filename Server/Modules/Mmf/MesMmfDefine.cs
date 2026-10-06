namespace Server.Mmf
{
  public static class MesMmfDefine
  {
    public const string MmfVisionHeartbeat           = "Vision MES";      // Vision -> MES  heartbeat 채널
    public const string MmfMesTowardVisionHeartbeat  = "MES Vision";      // MES -> Vision  heartbeat 채널
    public const string MmfVisionInterface           = "Vision MES IF";   // Vision -> MES  데이터 채널
    public const string MmfMesTowardVisionInterface  = "MES Vision IF";   // MES -> Vision  데이터 채널

    public const string MmfControlHeartbeat          = "Control MES";     // Control -> MES   heartbeat 채널     
    public const string MmfMesTowardControlHeartbeat = "MES Control";     // MES -> Control   heartbeat 채널
    public const string MmfControlInterface          = "Control MES IF";  // Control -> MES   데이터 채널
    public const string MmfMesTowardControlInterface = "MES Control IF";  // MES -> Control   데이터 채널

    public const int HeartbeatByteSize = 16;
    public const int InterfaceByteSize = 128;
    public const int DefaultHeartbeatIntervalMs = 1000;
  }
}

/*
  MMF (Memory-Mapped File) 통신에 사용되는 이름 상수와 크기 상수를 정의한 파일
  이 문자열이 공유 메모리 블록의 이름표 역할을 합니다. 
  양쪽 프로세스가 같은 이름으로 MemoryMappedFile.OpenExisting("Vision MES") 를 호출하면 같은 메모리 공간에 접근할 수 있습니다.
  
   Heartbeat는 "나 아직 살아있어" 신호입니다. 1초마다 이 블록을 갱신하고, 상대방이 일정 시간 갱신이 없으면 "상대 프로세스가 죽었다"고 판단합니다
*/

/*
   RAM                                                                                                        
    ┌─────────────────────────────────────────────────────┐                                                    
    │  "Vision MES IF"     │  Vision이 씀 / MES가 읽음     │                                                   
    │  "MES Vision IF"     │  MES가 씀   / Vision이 읽음   │                                                   
    ├─────────────────────────────────────────────────────┤                                                    
    │  "Control MES IF"    │  Control이 씀 / MES가 읽음    │                                                   
    │  "MES Control IF"    │  MES가 씀   / Control이 읽음  │                                                   
    ├─────────────────────────────────────────────────────┤                                                    
    │  "Vision MES"        │  Vision 심박 / MES 확인       │                                                   
    │  "MES Vision"        │  MES 심박   / Vision 확인     │                                                   
    │  "Control MES"       │  Control 심박 ...             │                                                   
    │  ...                 │                               │                                                   
    └─────────────────────────────────────────────────────┘                                                    
                                                                                                               
    규칙이 하나 있는데, 자기 구역에만 쓰고, 남의 구역은 읽기만 해요. 그래서 충돌(동시 쓰기)이 없고, lock도 자기
    IF에만 걸면 돼요.                                                                                          
                                                                                                               
    MesMmfDefine.cs의 이름 상수가 바로 이 구역들의 이름표였던 거고, 같은 이름으로                              
    MemoryMappedFile.OpenExisting("Vision MES")을 호출하면 두 프로세스가 동일한 RAM 구역을 공유하게 되는       
    거예요.                                                                                                    
*/

/*
   MMF (Memory-Mapped File) 프로세스 간 통신의 교과서적인 패턴이에요.                         
                                                                                                             
    정석 규칙 3가지:                                                                                           
      1. 각자 자기 구역만 쓴다 — 동시 쓰기 충돌 없음                                                             
      2. 이름으로 구역을 식별한다 — CreateOrOpen / OpenExisting                                                  
      3. 심박(Heartbeat)으로 생존 확인 — 상대가 죽었는지 감지                                                    
                                                                                                               
    왜 MMF를 쓰냐면:                                                                                           
    ┌───────────┬───────────────────────────────────┐                                                          
    │   방식     │               특징                 │                                                          
    ├───────────┼───────────────────────────────────┤                                                          
    │ MMF       │ 같은 PC, 초저지연, 커널 복사 없음      │                                                          
    ├───────────┼───────────────────────────────────┤                                                          
    │ TCP 소켓   │ 다른 PC 가능, 네트워크 스택 거침       │                                                          
    ├───────────┼───────────────────────────────────┤                                                          
    │ 파이프/큐   │ 메시지 단위, 구조화된 흐름             │                                                          
    └───────────┴───────────────────────────────────┘                                                          
                                                                                                               
    Vision/Control/MES가 같은 PC에서 돌아가는 별개 프로세스이기 때문에 MMF가 가장 빠르고 적합한 선택이에요.    
    RAM을 직접 공유하니까 사실상 함수 호출 수준의 속도가 나와요.                                               
*/