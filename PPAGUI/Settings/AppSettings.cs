using System;
using System.IO;
using System.Windows.Forms;
using Hmi.Helpers;
using Newtonsoft.Json;

namespace Hmi.Settings
{
    public class AppSettings<T> where T : new()
    {
        private const string DefaultFilename = "Data.xal";
        public void Save(string fileName = DefaultFilename)
        {
            var temp = JsonConvert.SerializeObject(this);
            var tempEncrypted = Encryption.Encrypt(temp);
            try
            {
                File.WriteAllText(fileName, tempEncrypted);
            }
            catch (Exception ex)
            {
                if (ex.HResult == -2147024893) // directory not found
                {
                    var d = Path.GetDirectoryName(fileName);
                    if (d == null) return;
                    Directory.CreateDirectory(d);
                    File.WriteAllText(fileName, tempEncrypted);
                }
            }
        }
        public void Save(object o, string fileName = DefaultFilename)
        {
            var temp = JsonConvert.SerializeObject(o);
            var tempEncrypted = Encryption.Encrypt(temp);
            try
            {
                File.WriteAllText(fileName, tempEncrypted);
            }
            catch (Exception ex)
            {
                if (ex.HResult == -2147024893) // directory not found
                {
                    var d = Path.GetDirectoryName(fileName);
                    if (d == null) return;
                    Directory.CreateDirectory(d);
                    File.WriteAllText(fileName, tempEncrypted);
                }
            }
        }

        public DialogResult ShowForm(string fileName = DefaultFilename)
        {
            using (var f = new AppSettingForm(this, Save, fileName ))
            {
                var d = f.ShowDialog();
                return d;
            }
        }
        public static bool Save(T pSettings, string fileName = DefaultFilename)
        {
            var temp = JsonConvert.SerializeObject(pSettings);
            var tempEncrypted = Encryption.Encrypt(temp);
            try
            {
                File.WriteAllText(fileName, tempEncrypted);
                return true;
            }
            catch (Exception ex)
            {
                PopUp.ShowInformation(ex.Message, "Save");
                return false;
            }
        }

        public static T Load(string fileName = DefaultFilename)
        {
            try
            {
                T t = new T();
                if (File.Exists(fileName))
                {
                    var temp = Encryption.Decrypt(File.ReadAllText(fileName));
                    t = JsonConvert.DeserializeObject<T>(temp);                    
                }

                return t;
            }
            catch (Exception ex)
            {
                PopUp.ShowInformation(ex.Message, "Load "+typeof(T));
                return new T();
            }
        }

        public static bool Exists()
        {
            return File.Exists(DefaultFilename);
        }
        
    }
}
