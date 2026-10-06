using System;
using System.IO.MemoryMappedFiles;
using System.Windows.Forms;
using Server.Mmf;

namespace Server.App.Control.Forms
{
  public partial class ControlMain : Form
  {
    private MesHeartbeatManager _heartbeat;
    private MemoryMappedFile _mmf;
    private MemoryMappedViewAccessor _accessor;
    private System.Threading.Timer _autoTimer;
    private readonly Random _rng = new Random();
    private uint _total, _ok, _rc, _ng1, _ng2;

    private enum Row { LineState = 0, Total, Ok, Rc, Ng1, Ng2, Count }

    private static readonly string[] RowNames =
    {
      "LineState (hex: Ready=1 OpNormal=2 OpManual=4 Start=8 Stop=10)",
      "Total", "OK", "RC", "NG1", "NG2"
    };

    public ControlMain()
    {
      InitializeComponent();
      btnAuto.Click += BtnAuto_Click;
      btnManual.Click += BtnManual_Click;
      dgvMmf.CellEndEdit += DgvData_CellEndEdit;
      Load += ControlMain_Load;
      FormClosed += ControlMain_FormClosed;
    }

    private void ControlMain_Load(object sender, EventArgs e)
    {
      Text = "제어 MMF 시뮬레이터";
      InitGrid();
      OpenMmf();

      _heartbeat = MesHeartbeatManager.ForControl();
      _heartbeat.Start();

      btnManual.Enabled = false;
    }

    private void ControlMain_FormClosed(object sender, FormClosedEventArgs e)
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
      dgvMmf.Columns[0].Width = 400;
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
        _mmf = MemoryMappedFile.CreateOrOpen(MesMmfDefine.MmfControlInterface, MesMmfDefine.InterfaceByteSize);
        _accessor = _mmf.CreateViewAccessor(0, MesMmfDefine.InterfaceByteSize);
        MesMmfClear.ClearInterfaceRegion(_accessor);
      }
      catch (Exception ex)
      {
        MessageBox.Show("Control MMF 오픈 실패: " + ex.Message);
      }
    }

    private void BtnAuto_Click(object sender, EventArgs e)
    {
      SetGridEditable(false);
      btnAuto.Enabled = false;
      btnManual.Enabled = true;
      _autoTimer = new System.Threading.Timer(AutoTick, null, 0, 1000);
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
      _total++;
      if (_rng.Next(0, 10) < 7) _ok++;
      else if (_rng.Next(0, 2) == 0) _ng1++;
      else _rc++;

      var lineState = (ushort)(MesInterProcessLayout.LineState_Ready
                               | MesInterProcessLayout.LineState_OpNormal
                               | MesInterProcessLayout.LineState_Start);

      WriteToMmf(lineState, _total, _ok, _rc, _ng1, _ng2);
      UpdateGrid(lineState, _total, _ok, _rc, _ng1, _ng2);
    }

    private void WriteToMmf(ushort lineState, uint total, uint ok, uint rc, uint ng1, uint ng2)
    {
      if (_accessor == null) return;
      _accessor.Write(MesInterProcessLayout.Offset_Version, MesInterProcessLayout.SchemaVersion);
      _accessor.Write(MesInterProcessLayout.CtrlIf_Offset_LineState, lineState);
      WriteDWord(MesInterProcessLayout.CtrlIf_Offset_TotalLow, total);
      WriteDWord(MesInterProcessLayout.CtrlIf_Offset_OkLow, ok);
      WriteDWord(MesInterProcessLayout.CtrlIf_Offset_RcLow, rc);
      WriteDWord(MesInterProcessLayout.CtrlIf_Offset_Ng1Low, ng1);
      WriteDWord(MesInterProcessLayout.CtrlIf_Offset_Ng2Low, ng2);
      _accessor.Write(MesInterProcessLayout.CtrlIf_Offset_PublishFlags, MesInterProcessLayout.PublishFlag_Counters);
    }

    private void WriteDWord(int lowOffset, uint value)
    {
      _accessor.Write(lowOffset, (ushort)(value & 0xFFFF));
      _accessor.Write(lowOffset + 2, (ushort)(value >> 16));
    }

    private void WriteFromGrid()
    {
      if (_accessor == null) return;
      try
      {
        var lineState = ParseHexRow(Row.LineState);
        var total = ParseUIntRow(Row.Total);
        var ok = ParseUIntRow(Row.Ok);
        var rc = ParseUIntRow(Row.Rc);
        var ng1 = ParseUIntRow(Row.Ng1);
        var ng2 = ParseUIntRow(Row.Ng2);
        WriteToMmf(lineState, total, ok, rc, ng1, ng2);
      }
      catch { }
    }

    private ushort ParseHexRow(Row row)
    {
      var s = (dgvMmf.Rows[(int)row].Cells[1].Value ?? "0").ToString().Trim();
      if (s.StartsWith("0x") || s.StartsWith("0X"))
      {
        ushort v;
        return ushort.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v) ? v : (ushort)0;
      }
      ushort r;
      return ushort.TryParse(s, out r) ? r : (ushort)0;
    }

    private uint ParseUIntRow(Row row)
    {
      var s = (dgvMmf.Rows[(int)row].Cells[1].Value ?? "0").ToString().Trim();
      uint r;
      return uint.TryParse(s, out r) ? r : 0;
    }

    private void UpdateGrid(ushort lineState, uint total, uint ok, uint rc, uint ng1, uint ng2)
    {
      if (IsDisposed) return;
      if (InvokeRequired) { Invoke(new Action(() => UpdateGrid(lineState, total, ok, rc, ng1, ng2))); return; }
      if (dgvMmf.Rows.Count < (int)Row.Count) return;

      dgvMmf.Rows[(int)Row.LineState].Cells[1].Value = "0x" + lineState.ToString("X4");
      dgvMmf.Rows[(int)Row.Total].Cells[1].Value = total;
      dgvMmf.Rows[(int)Row.Ok].Cells[1].Value = ok;
      dgvMmf.Rows[(int)Row.Rc].Cells[1].Value = rc;
      dgvMmf.Rows[(int)Row.Ng1].Cells[1].Value = ng1;
      dgvMmf.Rows[(int)Row.Ng2].Cells[1].Value = ng2;
    }
  }
}
