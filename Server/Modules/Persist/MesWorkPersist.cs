using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using Server.Map;
using Server.Register;

namespace NscanMes.Core.Persist
{
    /// <summary>
    ///   MES Work Holding 파일 누적 (CreateFromFile). 라이브 named MMF와 분리.
    ///   이슈#118. 파일명 기본 <c>mes-work-accum.bin</c>.
    /// </summary>
    public sealed class MesWorkPersist : IDisposable
  {
    public const uint MagicNsWk = 0x4B57534E;
    public const uint LayoutVersion = 1;
    public const string DefaultFileName = "mes-work-accum.bin";

    private const int HeaderBytes = 8;
    private const int WorkDWordCount = 8;
    private const int PayloadDWordCount = WorkDWordCount + Addresses.NgPartCount;
    public const int FileByteSize = HeaderBytes + PayloadDWordCount * 4;

    private static readonly ushort[] WorkOffsets =
    {
      Addresses.HoldingTotalTestCounter,
      Addresses.HoldingOkWorkCounter,
      Addresses.HoldingRcWorkCounter,
      Addresses.HoldingNgWork1Counter,
      Addresses.HoldingNgWork2Counter,
      Addresses.HoldingNgWork3Counter,
      Addresses.HoldingNgWork4Counter,
      Addresses.HoldingNgWork5Counter
    };

    private readonly object sync = new object();
    private MemoryMappedFile mmf;
    private MemoryMappedViewAccessor view;

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public MesWorkPersist(string path)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentNullException("path");

      FilePath = path;
      var dir = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);

      var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
      if (file.Length < FileByteSize)
        file.SetLength(FileByteSize);
        
      /*
        [CreateOrOpen vs CreateFromFile]
        
          프로세스 간 통신용        →  CreateOrOpen  (이름 있는 RAM 영역)
          혼자 파일 읽고 쓰는 용도   →  CreateFromFile (실제 파일을 MMF로 매핑)
          
              디스크 (mes-work-accum.bin)                                                                                                  
                      ↓ CreateFromFile                                                                                                     
              RAM (MMF 영역)                                                                                                               
                      ↓ Write()/Read()                                                                                                     
              마치 배열처럼 접근                                                                                                           
                      ↓ Flush()                                                                                                            
              디스크에 반영                                                                                                                
      */
      mmf = MemoryMappedFile.CreateFromFile(
        file,
        null,
        FileByteSize,
        MemoryMappedFileAccess.ReadWrite,
        HandleInheritability.None,
        false);
      view = mmf.CreateViewAccessor(0, FileByteSize);
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public string FilePath { get; }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    public void Dispose()
    {
      lock (sync)
      {
        if (view != null)
        {
          try
          {
            view.Flush();
          }
          catch
          {
          }

          view.Dispose();
          view = null;
        }

        if (mmf != null)
        {
          mmf.Dispose();
          mmf = null;
        }
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>
    ///   매직이 맞으면 설비 누적(Lifetime) 카운터에 복원. 신규/손상 파일이면 헤더만 기록하고 false.
    ///   MES 공개 윈도우 카운터(400001~)는 건드리지 않는다 — 재시작 후에는 항상 빈 윈도우로 시작하고,
    ///   다음 Data_CMD가 오면 그 시점부터 새로 채워진다.
    /// </summary>
    public bool TryLoadInto(Store store)
    {
      if (store == null)
        throw new ArgumentNullException("store");

      lock (sync)
      {
        var magic = view.ReadUInt32(0);
        var ver = view.ReadUInt32(4);
        if (magic != MagicNsWk || ver != LayoutVersion)
        {
          WriteHeaderUnlocked();
          view.Flush();
          return false;
        }

        for (var i = 0; i < WorkOffsets.Length; i++)
          store.SetAccumDWord(WorkOffsets[i], view.ReadUInt32(HeaderBytes + i * 4));

        for (var part = 1; part <= Addresses.NgPartCount; part++)
        {
          var slot = WorkDWordCount + (part - 1);
          store.SetAccumDWord(Addresses.NgPartOffset(part), view.ReadUInt32(HeaderBytes + slot * 4));
        }

        return true;
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>설비 누적(Lifetime) 카운터를 저장 — 대시보드 등 내부 화면이 재시작 후에도 이어서 보이도록.</summary>
    public void SaveFrom(Store store)
    {
      if (store == null)
        throw new ArgumentNullException("store");

      lock (sync)
      {
        WriteHeaderUnlocked();
        for (var i = 0; i < WorkOffsets.Length; i++)
          view.Write(HeaderBytes + i * 4, store.GetAccumDWord(WorkOffsets[i]));

        for (var part = 1; part <= Addresses.NgPartCount; part++)
        {
          var slot = WorkDWordCount + (part - 1);
          view.Write(HeaderBytes + slot * 4, store.GetAccumDWord(Addresses.NgPartOffset(part)));
        }

        view.Flush();
      }
    }

    //------------------------------------------------------------------------------------------------------------------------------------------------
    private void WriteHeaderUnlocked()
    {
      view.Write(0, MagicNsWk);
      view.Write(4, LayoutVersion);
    }
  }
}