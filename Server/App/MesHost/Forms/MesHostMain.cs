using System;
using System.Windows.Forms;
using Server.App;
using Server.Map;

namespace Server.App.MesHost.Forms
{
  public partial class MesHostMain : Form
  {
    private MesApp _app;
    private System.Windows.Forms.Timer _refreshTimer;
    private int _logLimit = 500;

    public MesHostMain()
    {
      InitializeComponent();
      btnStart.Click += BtnStart_Click;
      btnStop.Click += BtnStop_Click;
      Load += MesHostMain_Load;
      FormClosed += MesHostMain_FormClosed;
    }

    private void MesHostMain_Load(object sender, EventArgs e)
    {
      try
      {
        _app = new MesApp();
        _app.Log += AppendLog;
        lblModbus.Text = string.Format("Modbus: {0}:{1}  UnitId={2}",
          _app.ModbusConfig.ListenIp, _app.ModbusConfig.Port, _app.ModbusConfig.UnitId);
      }
      catch (Exception ex)
      {
        AppendLog("MesApp 초기화 실패: " + ex.Message);
      }

      InitStoreGrid();
    }

    private void MesHostMain_FormClosed(object sender, FormClosedEventArgs e)
    {
      _refreshTimer?.Stop();
      _refreshTimer?.Dispose();
      _app?.Dispose();
    }

    private void BtnStart_Click(object sender, EventArgs e)
    {
      if (_app == null) return;
      try
      {
        _app.Start();
        btnStart.Enabled = false;
        btnStop.Enabled = true;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += (s, _) => RefreshUI();
        _refreshTimer.Start();
        AppendLog("시작됨.");
      }
      catch (Exception ex)
      {
        AppendLog("시작 실패: " + ex.Message);
      }
    }

    private void BtnStop_Click(object sender, EventArgs e)
    {
      _refreshTimer?.Stop();
      _app?.Stop();
      btnStart.Enabled = true;
      btnStop.Enabled = false;
      AppendLog("정지됨.");
    }

    private void InitStoreGrid()
    {
      dgvStore.Columns.Clear();
      dgvStore.Columns.Add("colName", "태그명");
      dgvStore.Columns.Add("colAddr", "주소");
      dgvStore.Columns.Add("colValue", "값");
      dgvStore.Columns.Add("colDesc", "설명");
      dgvStore.Columns[0].Width = 160;
      dgvStore.Columns[1].Width = 70;
      dgvStore.Columns[2].Width = 100;
      dgvStore.Columns[3].Width = 350;
      dgvStore.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

      foreach (var tag in MesMapLayout.Tags)
      {
        if (tag.Area == MesTagArea.Holding)
          dgvStore.Rows.Add(tag.Name, tag.AddressLabel, "0", tag.Description);
      }
    }

    private void RefreshUI()
    {
      if (_app == null) return;

      lblVision.Text = _app.IsVisionPeerAlive ? "비전: ● 연결" : "비전: ○ 미연결";
      lblVision.ForeColor = _app.IsVisionPeerAlive ? System.Drawing.Color.Green : System.Drawing.Color.Gray;
      lblControl.Text = _app.IsControlPeerAlive ? "제어: ● 연결" : "제어: ○ 미연결";
      lblControl.ForeColor = _app.IsControlPeerAlive ? System.Drawing.Color.Green : System.Drawing.Color.Gray;

      RefreshStoreGrid();
    }

    private void RefreshStoreGrid()
    {
      if (_app?.Store == null) return;
      var store = _app.Store;
      var tags = MesMapLayout.Tags;
      var row = 0;

      foreach (var tag in tags)
      {
        if (tag.Area != MesTagArea.Holding) continue;
        if (row >= dgvStore.Rows.Count) break;

        string val;
        if (tag.DataType == MesTagDataType.DWord)
          val = store.GetDWord(tag.Offset).ToString();
        else
          val = store.GetHolding(tag.Offset).ToString();

        dgvStore.Rows[row].Cells[2].Value = val;
        row++;
      }
    }

    private void AppendLog(string msg)
    {
      if (IsDisposed) return;
      if (InvokeRequired) { Invoke(new Action(() => AppendLog(msg))); return; }

      var line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg + Environment.NewLine;
      txtLog.AppendText(line);

      // 너무 길면 앞부분 제거
      if (txtLog.Lines.Length > _logLimit)
      {
        var lines = txtLog.Lines;
        var keep = new string[_logLimit / 2];
        Array.Copy(lines, lines.Length - keep.Length, keep, 0, keep.Length);
        txtLog.Lines = keep;
      }
    }
  }
}
