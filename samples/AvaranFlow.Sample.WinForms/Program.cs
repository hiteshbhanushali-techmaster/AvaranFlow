using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using AvaranFlow.Core;
using AvaranFlow.Sinks.File;

namespace ExceptionMaster.Sample.WinForms
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Configure ExceptionMaster for WinForms
            string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "winforms_logs");
            AvaranFlow.Core.ExceptionMaster.Configure(options =>
            {
                options.EnableAsyncDispatch = true;
                options.Sinks.Add(new FileSink(new FileSinkOptions
                {
                    LogDirectory = logDirectory,
                    FileNamePattern = "winforms-{Date}.log"
                }));
            });

            // Wire up global Windows Forms unhandled exception hooks
            Application.ThreadException += (sender, e) =>
            {
                AvaranFlow.Core.ExceptionMaster.Log(e.Exception, category: "UI.ThreadException");
                MessageBox.Show($"Exception captured safely by ExceptionMaster:\n\n{e.Exception.Message}",
                    "ExceptionMaster Notification", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    AvaranFlow.Core.ExceptionMaster.Log(ex, category: "AppDomain.UnhandledException", isFatal: true);
                }
            };

            Application.Run(new MainForm());

            AvaranFlow.Core.ExceptionMaster.Shutdown(TimeSpan.FromSeconds(2));
        }
    }

    public class MainForm : Form
    {
        public MainForm()
        {
            Text = "ExceptionMaster WinForms Sample";
            Width = 450;
            Height = 250;
            StartPosition = FormStartPosition.CenterScreen;

            var button = new Button
            {
                Text = "Trigger Simulated UI Exception",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };

            button.Click += (s, e) =>
            {
                throw new InvalidOperationException("Simulated WinForms button click failure handled by ExceptionMaster");
            };

            Controls.Add(button);
        }
    }
}
