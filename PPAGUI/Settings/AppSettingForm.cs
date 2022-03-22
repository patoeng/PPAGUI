using System;
using System.Windows.Forms;
using Hmi.Helpers;

namespace Hmi.Settings
{
    public partial class AppSettingForm : Form
    {
        public delegate void SaveDelegate(object obj, string filename);
        public AppSettingForm(object setting, SaveDelegate save, string fileName)
        {
            _setting = setting;
            InitializeComponent();
            propertyGrid1.SelectedObject = _setting;
            _save = save;
            _fileName = fileName;
        }

        private readonly object _setting;
        private readonly SaveDelegate _save;
        private readonly string _fileName;
        private void btnOk_Click(object sender, EventArgs e)
        {
            var dlg = PopUp.ShowYesNoQuestion(this,"Are you sure want to save?", "Application Setting");
            if (dlg == DialogResult.Yes)
            {
                _save(_setting,_fileName);
            }
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void AppSettingForm_Load(object sender, EventArgs e)
        {

        }
    }
}
