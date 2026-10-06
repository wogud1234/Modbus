namespace Server.App.Vision.Forms
{
  partial class VisionMain
  {
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null))
      {
        components.Dispose();
      }
      base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
      this.btnAuto = new System.Windows.Forms.Button();
      this.btnManual = new System.Windows.Forms.Button();
      this.dgvMmf = new System.Windows.Forms.DataGridView();
      ((System.ComponentModel.ISupportInitialize)(this.dgvMmf)).BeginInit();
      this.SuspendLayout();
      // 
      // btnAuto
      // 
      this.btnAuto.Location = new System.Drawing.Point(696, 12);
      this.btnAuto.Name = "btnAuto";
      this.btnAuto.Size = new System.Drawing.Size(125, 46);
      this.btnAuto.TabIndex = 0;
      this.btnAuto.Text = "자 동";
      this.btnAuto.UseVisualStyleBackColor = true;
      // 
      // btnManual
      // 
      this.btnManual.Location = new System.Drawing.Point(565, 12);
      this.btnManual.Name = "btnManual";
      this.btnManual.Size = new System.Drawing.Size(125, 46);
      this.btnManual.TabIndex = 1;
      this.btnManual.Text = "수 동";
      this.btnManual.UseVisualStyleBackColor = true;
      // 
      // dgvMmf
      // 
      this.dgvMmf.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
      this.dgvMmf.Location = new System.Drawing.Point(12, 62);
      this.dgvMmf.Name = "dgvMmf";
      this.dgvMmf.RowHeadersWidth = 51;
      this.dgvMmf.RowTemplate.Height = 27;
      this.dgvMmf.Size = new System.Drawing.Size(809, 520);
      this.dgvMmf.TabIndex = 2;
      //
      // VisionMain
      //
      this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(833, 600);
      this.Controls.Add(this.dgvMmf);
      this.Controls.Add(this.btnManual);
      this.Controls.Add(this.btnAuto);
      this.Name = "VisionMain";
      this.Text = "VisionMain";
      ((System.ComponentModel.ISupportInitialize)(this.dgvMmf)).EndInit();
      this.ResumeLayout(false);

    }

    #endregion

    private System.Windows.Forms.Button btnAuto;
    private System.Windows.Forms.Button btnManual;
    private System.Windows.Forms.DataGridView dgvMmf;
  }
}