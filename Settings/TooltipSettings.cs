using System.Drawing;
using System.Windows.Forms;

namespace LiveSplit.VoxSplitter {
    public partial class TooltipSettings : UserControl {

        public TooltipSettings() {
            InitializeComponent();
        }

        public Bitmap GetImage() => (Bitmap)PictureBox.Image;
        public void SetImage(Bitmap bmp, int index = 0) {
            if(bmp != null) {
                TableLayoutPanel.ColumnStyles[1].Width = bmp.Width / (index+1);
                TableLayoutPanel.RowStyles[1].Height = bmp.Height / (index+1);

                if(TableLayoutPanel.Width == TableLayoutPanel.ColumnStyles[1].Width) {
                    if(PictureBox.Margin.Bottom != 0) {
                        PictureBox.Margin = new Padding(0);
                    }
                } else {
                    if(PictureBox.Margin.Bottom != 10) {
                        PictureBox.Margin = new Padding(0, 0, 0, 10);
                    }
                }
            } else {
                TableLayoutPanel.ColumnStyles[1].Width = 0;
                TableLayoutPanel.RowStyles[1].Height = 0;
            }

            if(PictureBox.Image != null && !PictureBox.Image.Equals(bmp)) {
                PictureBox.Image.Dispose();
            }

            PictureBox.Image = bmp;
        }
        public string GetName() => (string)LabelText.Tag;
        public void SetName(string value) => LabelText.Tag = value;
        public void SetText(string value) => LabelText.Text = value;
    }

    public class PopupForm : Form {
        public PopupForm() {
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Fill;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            DoubleBuffered = true;

            Deactivate += (s, e) => Hide();
        }
    }

    public class TooltipForm : PopupForm {
        protected override bool ShowWithoutActivation => true;
    }
}