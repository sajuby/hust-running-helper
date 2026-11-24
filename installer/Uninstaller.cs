using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal static class Uninstaller
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentDir = Directory.GetParent(appDir);
        string root = parentDir == null ? null : parentDir.FullName;
        if (root == null) { MessageBox.Show("无法定位安装目录。", "卸载", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        string dataDir = Path.Combine(root, "HUST-Sports-Data");

        var form = new Form
        {
            Text = "华中大体育 · 卸载",
            ClientSize = new Size(480, 210),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
        };
        var lbl = new Label
        {
            Left = 16, Top = 16, Width = 448, Height = 60,
            Text = "将停止本安装目录下的模拟器与控制服务，删除程序文件夹 HUST-Sports 与桌面快捷方式。\n数据目录：" + dataDir,
        };
        var chkKeep = new CheckBox { Left = 16, Top = 84, AutoSize = true, Text = "保留数据目录（账号与学期次数不删除）", Checked = true };
        var btnOk = new Button { Text = "卸载", Left = 288, Top = 128, Width = 84 };
        var btnCancel = new Button { Text = "取消", Left = 380, Top = 128, Width = 84 };
        btnCancel.Click += (s, e) => form.Close();
        btnOk.Click += (s, e) =>
        {
            bool keep = chkKeep.Checked;
            form.Hide();
            try
            {
                StopOwnedProcesses(root);
                RemoveShortcut();
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\HUST-Sports", false); } catch { }
                if (!keep && Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
                // 自身位于 app 目录内，退出后由 cmd 删除 app 目录
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + appDir + "\" & rmdir \"" + root + "\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                MessageBox.Show("卸载完成。" + (keep ? "数据目录已保留。" : "数据目录已删除。"), "卸载", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("卸载失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Environment.Exit(0);
        };
        form.Controls.AddRange(new Control[] { lbl, chkKeep, btnOk, btnCancel });
        Application.Run(form);
    }

    private static void StopOwnedProcesses(string root)
    {
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string name = p.ProcessName.ToLowerInvariant();
                if (name != "electron" && name != "python" && name != "pythonw" && name != "emulator" && name != "qemu-system-x86_64" && name != "crashpad_handler") continue;
                string file = null;
                try { if (p.MainModule != null) file = p.MainModule.FileName; } catch { }
                if (file != null && file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
            }
            catch { /* 无权限或已退出的进程忽略 */ }
        }
    }

    private static void RemoveShortcut()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = Path.Combine(desktop, "华中大体育.lnk");
            if (File.Exists(lnk)) File.Delete(lnk);
        }
        catch { }
    }
}