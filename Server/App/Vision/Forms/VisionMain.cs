using System;
using System.IO.MemoryMappedFiles;
using System.Windows.Forms;
using Server.Assign;
using Server.Map;
using Server.Mmf;

namespace Server.App.Vision.Forms
{
  public partial class VisionMain : Form
  {
    private MesHeartbeatManager _heartbeat;
    private MemoryMappedFile _mmf;
    private MemoryMappedViewAccessor _accessor;
    private System.Threading.Timer _autoTimer;
    private readonly Random _rng = new Random();
    private ushort _cycleId;

    private enum Row
    {
      CycleId = 0, VisionState, ChannelNgBits, SideNgMask,
      DeltaTotal, DeltaOk, DeltaNg1, DeltaNg2, DeltaNg3, DeltaNg4, DeltaNg5, DeltaRc,
      ApertureNgBits, Count
    }

    private static readonly string[] RowNames =
    {
      "CycleId", "VisionState (0=Idle 1=Run 2=Alarm)", "ChannelNgBits (hex)", "SideNgMask (hex)",
      "DeltaTotal", "DeltaOk", "DeltaNg1", "DeltaNg2", "DeltaNg3", "DeltaNg4", "DeltaNg5", "DeltaRc",
      "ApertureNgBits"
    };

    public VisionMain()
    {
      InitializeComponent();
      btnAuto.Click += BtnAuto_Click;
      btnManual.Click += BtnManual_Click;
      dgvMmf.CellEndEdit += DgvData_CellEndEdit;
      Load += VisionMain_Load;
      FormClosed += VisionMain_FormClosed;
    }

    private void VisionMain_Load(object sender, EventArgs e)
    {
      Text = "비전 MMF 시뮬레이터";
      InitGrid();
      OpenMmf();

      _heartbeat = MesHeartbeatManager.ForVision();
      _heartbeat.Start();

      btnManual.Enabled = false;
    }

    private void VisionMain_FormClosed(object sender, FormClosedEventArgs e)
    {
      _autoTimer?.Dispose();
      _heartbeat?.Stop();
      _heartbeat?.Dispose();
      _accessor?.Dispose();
      _mmf?.Dispose();
    }

    private void InitGrid()
    {
      dgvMmf.Columns.Clear();
      dgvMmf.Columns.Add("colField", "필드");
      dgvMmf.Columns.Add("colValue", "값");
      dgvMmf.Columns[0].ReadOnly = true;
      dgvMmf.Columns[0].Width = 300;
      dgvMmf.Columns[1].Width = 150;
      dgvMmf.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
      dgvMmf.AllowUserToAddRows = false;

      for (var i = 0; i < (int)Row.Count; i++)
        dgvMmf.Rows.Add(RowNames[i], "0");

      SetGridEditable(false);
    }

    private void SetGridEditable(bool editable)
    {
      dgvMmf.Columns[1].ReadOnly = !editable;
    }

    private void OpenMmf()
    {
      try
      {
        _mmf = MemoryMappedFile.CreateOrOpen(MesMmfDefine.MmfVisionInterface, MesMmfDefine.InterfaceByteSize);
        _accessor = _mmf.CreateViewAccessor(0, MesMmfDefine.InterfaceByteSize);
        MesMmfClear.ClearInterfaceRegion(_accessor);
      }
      catch (Exception ex)
      {
        MessageBox.Show("Vision MMF 오픈 실패: " + ex.Message);
      }
    }

    private void BtnAuto_Click(object sender, EventArgs e)
    {
      SetGridEditable(false);
      btnAuto.Enabled = false;
      btnManual.Enabled = true;
      _autoTimer = new System.Threading.Timer(AutoTick, null, 0, 500);
    }

    private void BtnManual_Click(object sender, EventArgs e)
    {
      _autoTimer?.Dispose();
      _autoTimer = null;
      SetGridEditable(true);
      btnAuto.Enabled = true;
      btnManual.Enabled = false;
    }

    private void DgvData_CellEndEdit(object sender, DataGridViewCellEventArgs e)
    {
      WriteFromGrid();
    }

    private void AutoTick(object state)
    {
      _cycleId++;
      if (_cycleId == 0) _cycleId = 1;

      var hasNg = _rng.Next(0, 4) == 0;
      var p = new MesMapS0Publish
      {
        CycleId = _cycleId,
        VisionState = MesInterProcessLayout.VisionState_Run,
        ChannelNgBits = hasNg ? (ushort)(_rng.Next(1, 64) & 0x3F) : (ushort)0,
        SideNgMask = (ushort)0,
        DeltaTotal = 1,
        DeltaOk = hasNg ? (ushort)0 : (ushort)1,
        DeltaNg1 = (hasNg && _rng.Next(0, 2) == 0) ? (ushort)1 : (ushort)0,
      };

      WriteToMmf(p);
      UpdateGrid(p);
    }

    private void WriteToMmf(MesMapS0Publish p)
    {
      if (_accessor == null) return;
      _accessor.Write(MesInterProcessLayout.Offset_Version, MesInterProcessLayout.SchemaVersion);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_CycleId, p.CycleId);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_VisionState, p.VisionState);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_ChannelNgBits, p.ChannelNgBits);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_SideNgMask, p.SideNgMask);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_DeltaTotal, p.DeltaTotal);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_DeltaOk, p.DeltaOk);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_DeltaNg1, p.DeltaNg1);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_DeltaNg2, p.DeltaNg2);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_DeltaNg3, p.DeltaNg3);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_DeltaNg4, p.DeltaNg4);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_DeltaNg5, p.DeltaNg5);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_DeltaRc, p.DeltaRc);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_ApertureNgBits, p.ApertureNgBits);
      _accessor.Write(MesInterProcessLayout.VisIf_Offset_PublishFlags, MesInterProcessLayout.PublishFlag_NewResult);
    }

    private void WriteFromGrid()
    {
      if (_accessor == null) return;
      try
      {
        var p = new MesMapS0Publish
        {
          CycleId = ParseRow(Row.CycleId),
          VisionState = ParseRow(Row.VisionState),
          ChannelNgBits = ParseRow(Row.ChannelNgBits),
          SideNgMask = ParseRow(Row.SideNgMask),
          DeltaTotal = ParseRow(Row.DeltaTotal),
          DeltaOk = ParseRow(Row.DeltaOk),
          DeltaNg1 = ParseRow(Row.DeltaNg1),
          DeltaNg2 = ParseRow(Row.DeltaNg2),
          DeltaNg3 = ParseRow(Row.DeltaNg3),
          DeltaNg4 = ParseRow(Row.DeltaNg4),
          DeltaNg5 = ParseRow(Row.DeltaNg5),
          DeltaRc = ParseRow(Row.DeltaRc),
          ApertureNgBits = ParseRow(Row.ApertureNgBits),
        };
        WriteToMmf(p);
      }
      catch { }
    }

    private ushort ParseRow(Row row)
    {
      var cell = dgvMmf.Rows[(int)row].Cells[1].Value;
      if (cell == null) return 0;
      var s = cell.ToString().Trim();
      if (s.StartsWith("0x") || s.StartsWith("0X"))
      {
        ushort v;
        return ushort.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v) ? v : (ushort)0;
      }
      ushort r;
      return ushort.TryParse(s, out r) ? r : (ushort)0;
    }

    private void UpdateGrid(MesMapS0Publish p)
    {
      if (IsDisposed) return;
      if (InvokeRequired) { Invoke(new Action(() => UpdateGrid(p))); return; }
      if (dgvMmf.Rows.Count < (int)Row.Count) return;

      dgvMmf.Rows[(int)Row.CycleId].Cells[1].Value = p.CycleId;
      dgvMmf.Rows[(int)Row.VisionState].Cells[1].Value = p.VisionState;
      dgvMmf.Rows[(int)Row.ChannelNgBits].Cells[1].Value = "0x" + p.ChannelNgBits.ToString("X2");
      dgvMmf.Rows[(int)Row.SideNgMask].Cells[1].Value = "0x" + p.SideNgMask.ToString("X4");
      dgvMmf.Rows[(int)Row.DeltaTotal].Cells[1].Value = p.DeltaTotal;
      dgvMmf.Rows[(int)Row.DeltaOk].Cells[1].Value = p.DeltaOk;
      dgvMmf.Rows[(int)Row.DeltaNg1].Cells[1].Value = p.DeltaNg1;
      dgvMmf.Rows[(int)Row.DeltaNg2].Cells[1].Value = p.DeltaNg2;
      dgvMmf.Rows[(int)Row.DeltaNg3].Cells[1].Value = p.DeltaNg3;
      dgvMmf.Rows[(int)Row.DeltaNg4].Cells[1].Value = p.DeltaNg4;
      dgvMmf.Rows[(int)Row.DeltaNg5].Cells[1].Value = p.DeltaNg5;
      dgvMmf.Rows[(int)Row.DeltaRc].Cells[1].Value = p.DeltaRc;
      dgvMmf.Rows[(int)Row.ApertureNgBits].Cells[1].Value = p.ApertureNgBits;
    }
  }
}
