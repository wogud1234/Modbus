namespace Server.App.MesHost.Forms
{
  partial class MesHostMain
  {
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null))
        components.Dispose();
      base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
      this.btnStart = new System.Windows.Forms.Button();
      this.btnStop = new System.Windows.Forms.Button();
      this.lblVision = new System.Windows.Forms.Label();
      this.lblControl = new System.Windows.Forms.Label();
      this.lblModbus = new System.Windows.Forms.Label();
      this.dgvStore = new System.Windows.Forms.DataGridView();
      this.lblLog = new System.Windows.Forms.Label();
      this.txtLog = new System.Windows.Forms.TextBox();
      ((System.ComponentModel.ISupportInitialize)(this.dgvStore)).BeginInit();
      this.SuspendLayout();
      // btnStart
      this.btnStart.Location = new System.Drawing.Point(12, 12);
      this.btnStart.Name = "btnStart";
      this.btnStart.Size = new System.Drawing.Size(110, 40);
      this.btnStart.TabIndex = 0;
      this.btnStart.Text = "시작";
      this.btnStart.UseVisualStyleBackColor = true;
      // btnStop
      this.btnStop.Location = new System.Drawing.Point(130, 12);
      this.btnStop.Name = "btnStop";
      this.btnStop.Size = new System.Drawing.Size(110, 40);
      this.btnStop.Enabled = false;
      this.btnStop.TabIndex = 1;
      this.btnStop.Text = "정지";
      this.btnStop.UseVisualStyleBackColor = true;
      // lblVision
      this.lblVision.AutoSize = false;
      this.lblVision.Location = new System.Drawing.Point(260, 12);
      this.lblVision.Name = "lblVision";
      this.lblVision.Size = new System.Drawing.Size(200, 20);
      this.lblVision.TabIndex = 2;
      this.lblVision.Text = "비전: ○ 미연결";
      // lblControl
      this.lblControl.AutoSize = false;
      this.lblControl.Location = new System.Drawing.Point(260, 35);
      this.lblControl.Name = "lblControl";
      this.lblControl.Size = new System.Drawing.Size(200, 20);
      this.lblControl.TabIndex = 3;
      this.lblControl.Text = "제어: ○ 미연결";
      // lblModbus
      this.lblModbus.AutoSize = false;
      this.lblModbus.Location = new System.Drawing.Point(470, 22);
      this.lblModbus.Name = "lblModbus";
      this.lblModbus.Size = new System.Drawing.Size(350, 20);
      this.lblModbus.TabIndex = 4;
      this.lblModbus.Text = "Modbus: -";
      // dgvStore
      this.dgvStore.AllowUserToAddRows = false;
      this.dgvStore.AllowUserToDeleteRows = false;
      this.dgvStore.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
      this.dgvStore.Location = new System.Drawing.Point(12, 62);
      this.dgvStore.Name = "dgvStore";
      this.dgvStore.ReadOnly = true;
      this.dgvStore.RowHeadersWidth = 51;
      this.dgvStore.RowTemplate.Height = 24;
      this.dgvStore.Size = new System.Drawing.Size(960, 750);
      this.dgvStore.TabIndex = 5;
      // lblLog
      this.lblLog.AutoSize = true;
      this.lblLog.Location = new System.Drawing.Point(12, 822);
      this.lblLog.Name = "lblLog";
      this.lblLog.Size = new System.Drawing.Size(30, 15);
      this.lblLog.TabIndex = 6;
      this.lblLog.Text = "로그:";
      // txtLog
      this.txtLog.Location = new System.Drawing.Point(12, 840);
      this.txtLog.Multiline = true;
      this.txtLog.Name = "txtLog";
      this.txtLog.ReadOnly = true;
      this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
      this.txtLog.Size = new System.Drawing.Size(960, 130);
      this.txtLog.TabIndex = 7;
      // MesHostMain
      this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(984, 990);
      this.Controls.Add(this.txtLog);
      this.Controls.Add(this.lblLog);
      this.Controls.Add(this.dgvStore);
      this.Controls.Add(this.lblModbus);
      this.Controls.Add(this.lblControl);
      this.Controls.Add(this.lblVision);
      this.Controls.Add(this.btnStop);
      this.Controls.Add(this.btnStart);
      this.Name = "MesHostMain";
      this.Text = "MES Host";
      ((System.ComponentModel.ISupportInitialize)(this.dgvStore)).EndInit();
      this.ResumeLayout(false);
      this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.Button btnStart;
    private System.Windows.Forms.Button btnStop;
    private System.Windows.Forms.Label lblVision;
    private System.Windows.Forms.Label lblControl;
    private System.Windows.Forms.Label lblModbus;
    private System.Windows.Forms.DataGridView dgvStore;
    private System.Windows.Forms.Label lblLog;
    private System.Windows.Forms.TextBox txtLog;
  }
}
