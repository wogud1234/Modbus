namespace Server.App.Control.Forms
{
  partial class ControlMain
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
      this.dgvMmf = new System.Windows.Forms.DataGridView();
      this.btnManual = new System.Windows.Forms.Button();
      this.btnAuto = new System.Windows.Forms.Button();
      ((System.ComponentModel.ISupportInitialize)(this.dgvMmf)).BeginInit();
      this.SuspendLayout();
      // 
      // dgvMmf
      // 
      this.dgvMmf.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
      this.dgvMmf.Location = new System.Drawing.Point(12, 58);
      this.dgvMmf.Name = "dgvMmf";
      this.dgvMmf.RowHeadersWidth = 51;
      this.dgvMmf.RowTemplate.Height = 27;
      this.dgvMmf.Size = new System.Drawing.Size(809, 340);
      this.dgvMmf.TabIndex = 5;
      //
      // btnManual
      // 
      this.btnManual.Location = new System.Drawing.Point(565, 8);
      this.btnManual.Name = "btnManual";
      this.btnManual.Size = new System.Drawing.Size(125, 46);
      this.btnManual.TabIndex = 4;
      this.btnManual.Text = "수 동";
      this.btnManual.UseVisualStyleBackColor = true;
      // 
      // btnAuto
      // 
      this.btnAuto.Location = new System.Drawing.Point(696, 8);
      this.btnAuto.Name = "btnAuto";
      this.btnAuto.Size = new System.Drawing.Size(125, 46);
      this.btnAuto.TabIndex = 3;
      this.btnAuto.Text = "자 동";
      this.btnAuto.UseVisualStyleBackColor = true;
      // 
      // ControlMain
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(835, 420);
      this.Controls.Add(this.dgvMmf);
      this.Controls.Add(this.btnManual);
      this.Controls.Add(this.btnAuto);
      this.Name = "ControlMain";
      this.Text = "ControlMain";
      ((System.ComponentModel.ISupportInitialize)(this.dgvMmf)).EndInit();
      this.ResumeLayout(false);

    }

    #endregion

    private System.Windows.Forms.DataGridView dgvMmf;
    private System.Windows.Forms.Button btnManual;
    private System.Windows.Forms.Button btnAuto;
  }
}