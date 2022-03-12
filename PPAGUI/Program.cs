using OpcenterWikLibrary;
using System;
using System.Windows.Forms;

namespace PPAGUI
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            AppSettings.AssemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Main());
        }
    }
}
