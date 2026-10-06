namespace Server
{
  partial class FormIndex
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
      this.btnVision = new System.Windows.Forms.Button();
      this.btnControl = new System.Windows.Forms.Button();
      this.btnMesHost = new System.Windows.Forms.Button();
      this.SuspendLayout();
      // 
      // btnVision
      // 
      this.btnVision.Location = new System.Drawing.Point(242, 49);
      this.btnVision.Name = "btnVision";
      this.btnVision.Size = new System.Drawing.Size(127, 52);
      this.btnVision.TabIndex = 0;
      this.btnVision.Text = "비 전";
      this.btnVision.UseVisualStyleBackColor = true;
      // 
      // btnControl
      // 
      this.btnControl.Location = new System.Drawing.Point(375, 49);
      this.btnControl.Name = "btnControl";
      this.btnControl.Size = new System.Drawing.Size(127, 52);
      this.btnControl.TabIndex = 1;
      this.btnControl.Text = "제 어";
      this.btnControl.UseVisualStyleBackColor = true;
      // 
      // btnMesHost
      // 
      this.btnMesHost.Location = new System.Drawing.Point(508, 49);
      this.btnMesHost.Name = "btnMesHost";
      this.btnMesHost.Size = new System.Drawing.Size(127, 52);
      this.btnMesHost.TabIndex = 2;
      this.btnMesHost.Text = "MES";
      this.btnMesHost.UseVisualStyleBackColor = true;
      // 
      // FormIndex
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(882, 501);
      this.Controls.Add(this.btnMesHost);
      this.Controls.Add(this.btnControl);
      this.Controls.Add(this.btnVision);
      this.Name = "FormIndex";
      this.Text = "Index";
      this.ResumeLayout(false);

    }

    #endregion

    private System.Windows.Forms.Button btnVision;
    private System.Windows.Forms.Button btnControl;
    private System.Windows.Forms.Button btnMesHost;
  }
}