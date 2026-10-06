using System.Windows.Forms;
using Server.App.Vision.Forms;
using Server.App.Control.Forms;
using Server.App.MesHost.Forms;

namespace Server
{
  public partial class FormIndex : Form
  {
    public FormIndex()
    {
      InitializeComponent();
      btnVision.Click += (s, e) => new VisionMain().Show();
      btnControl.Click += (s, e) => new ControlMain().Show();
      btnMesHost.Click += (s, e) => new MesHostMain().Show();
    }
  }
}