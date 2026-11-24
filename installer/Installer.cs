using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal static class Installer
{
    private const string MAGIC = "HUSTSFX7Z01\0";
    private static Form form;
    private static TextBox txtPath;
    private static Label lblHint;
    private static Label lblStatus;
    private static ProgressBar progress;
    private static CheckBox chkShortcut;
    private static Button btnInstall;
    private static bool running;

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        form = new Form
        {
            Text = "华中大体育 · 安装向导",
            ClientSize = new Size(560, 330),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            Icon = ExtractIcon(),
        };

        var lblPath = new Label { Text = "安装路径（将在其下创建 HustSports 文件夹，内含程序与数据两个子文件夹）", Left = 16, Top = 16, AutoSize = true };
        txtPath = new TextBox { Left = 16, Top = 40, Width = 420, Text = @"E:\HustSports" };
        var btnBrowse = new Button { Text = "浏览…", Left = 444, Top = 38, Width = 96 };
        lblHint = new Label { Left = 16, Top = 70, Width = 524, Height = 32, ForeColor = Color.FromArgb(160, 61, 46), Text = "" };
        chkShortcut = new CheckBox { Text = "创建桌面快捷方式", Left = 16, Top = 106, AutoSize = true, Checked = true };
        var lblInfo = new Label
        {
            Left = 16, Top = 134, Width = 524, Height = 48,
            Text = "HustSports\\HUST-Sports 存放程序与卸载程序；HustSports\\HUST-Sports-Data 存放账号与学期数据。\n已存在的数据目录不会被覆盖或清空。",
        };
        progress = new ProgressBar { Left = 16, Top = 192, Width = 524, Height = 18 };
        lblStatus = new Label { Left = 16, Top = 216, Width = 524, Text = "就绪" };
        btnInstall = new Button { Text = "安装", Left = 360, Top = 252, Width = 84 };
        var btnCancel = new Button { Text = "取消", Left = 456, Top = 252, Width = 84 };

        btnBrowse.Click += (s, e) =>
        {
            using (var dlg = new FolderBrowserDialog { Description = "选择安装路径", ShowNewFolderButton = true })
            {
                if (Directory.Exists(txtPath.Text)) dlg.SelectedPath = txtPath.Text;
                if (dlg.ShowDialog(form) == DialogResult.OK) txtPath.Text = dlg.SelectedPath;
            }
        };
        txtPath.TextChanged += (s, e) => UpdateHint();
        btnInstall.Click += (s, e) => StartInstall();
        btnCancel.Click += (s, e) => { if (!running) form.Close(); };

        form.Controls.AddRange(new Control[] { lblPath, txtPath, btnBrowse, lblHint, chkShortcut, lblInfo, progress, lblStatus, btnInstall, btnCancel });
        UpdateHint();
        Application.Run(form);
    }

    private static Icon ExtractIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return null; }
    }

    private static void UpdateHint()
    {
        string p = txtPath.Text.Trim();
        bool bad = p.Length == 0 || p.IndexOf(' ') >= 0 || HasNonAscii(p);
        lblHint.Text = bad
            ? "推荐选择不含空格、不含中文等非 ASCII 字符的路径（如 E:\\Games）。当前路径仍可继续安装。"
            : "路径符合要求。";
        lblHint.ForeColor = bad ? Color.FromArgb(160, 61, 46) : Color.FromArgb(23, 109, 96);
    }

    private static bool HasNonAscii(string s)
    {
        foreach (char c in s) if (c > 127) return true;
        return false;
    }

    private static void SetStatus(string text)
    {
        if (form.InvokeRequired) { form.BeginInvoke(new Action<string>(SetStatus), text); return; }
        lblStatus.Text = text;
    }

    private static void SetProgress(int value)
    {
        if (form.InvokeRequired) { form.BeginInvoke(new Action<int>(SetProgress), value); return; }
        if (value < 0) { progress.Style = ProgressBarStyle.Marquee; return; }
        progress.Style = ProgressBarStyle.Blocks;
        progress.Value = Math.Max(0, Math.Min(100, value));
    }

    private static void StartInstall()
    {
        if (running) return;
        string root = txtPath.Text.Trim();
        if (root.Length == 0) { MessageBox.Show(form, "请先填写安装路径。", "安装", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        string installRoot = Path.Combine(root, "HustSports");
        string appDir = Path.Combine(installRoot, "HUST-Sports");
        string dataDir = Path.Combine(installRoot, "HUST-Sports-Data");
        if (Directory.Exists(appDir))
        {
            if (MessageBox.Show(form, "目标程序文件夹已存在：\n" + appDir + "\n继续将覆盖其中的程序文件（数据目录保留）。是否继续？", "覆盖确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        }
        running = true;
        btnInstall.Enabled = false;
        txtPath.Enabled = false;
        bool shortcut = chkShortcut.Checked;
        new Thread(() => DoInstall(root, installRoot, appDir, dataDir, shortcut)) { IsBackground = true }.Start();
    }

    private static Stream OpenResource(string name)
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
    }

    private static void CopyResourceTo(string name, string target)
    {
        using (Stream src = OpenResource(name))
        {
            if (src == null) throw new Exception("缺少内置资源 " + name);
            using (FileStream dst = File.Create(target))
            {
                byte[] buf = new byte[65536];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
            }
        }
    }

    private static long ExtractArchiveTo(string targetFile)
    {
        string self = Application.ExecutablePath;
        using (FileStream fs = File.OpenRead(self))
        {
            fs.Seek(-20, SeekOrigin.End);
            byte[] footer = new byte[20];
            ReadFull(fs, footer);
            string magic = System.Text.Encoding.ASCII.GetString(footer, 8, 12);
            if (magic != MAGIC) throw new Exception("安装包数据缺失（脚注校验失败），请重新生成安装包。");
            long len = BitConverter.ToInt64(footer, 0);
            fs.Seek(-20 - len, SeekOrigin.End);
            using (FileStream dst = File.Create(targetFile))
            {
                byte[] buf = new byte[1048576];
                long left = len;
                while (left > 0)
                {
                    int want = (int)Math.Min(buf.Length, left);
                    int n = fs.Read(buf, 0, want);
                    if (n <= 0) throw new Exception("安装包数据不完整。");
                    dst.Write(buf, 0, n);
                    left -= n;
                }
            }
            return len;
        }
    }

    private static void ReadFull(FileStream fs, byte[] buf)
    {
        int done = 0;
        while (done < buf.Length)
        {
            int n = fs.Read(buf, done, buf.Length - done);
            if (n <= 0) throw new Exception("安装包数据不完整。");
            done += n;
        }
    }

    private static void DoInstall(string root, string installRoot, string appDir, string dataDir, bool shortcut)
    {
        string temp = Path.Combine(Path.GetTempPath(), "HUSTSFX_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            SetStatus("读取内置安装数据…");
            SetProgress(-1);
            string archive = Path.Combine(temp, "payload.7z");
            long size = ExtractArchiveTo(archive);
            SetStatus("内置数据 " + (size / 1073741824.0).ToString("0.0") + " GiB，准备解压…");

            string sevenExe = Path.Combine(temp, "7z.exe");
            string sevenDll = Path.Combine(temp, "7z.dll");
            CopyResourceTo("7zexeres", sevenExe);
            CopyResourceTo("7zdllres", sevenDll);

            Directory.CreateDirectory(installRoot);
            Directory.CreateDirectory(appDir);
            SetStatus("解压程序文件到 " + appDir);
            SetProgress(0);
            var psi = new ProcessStartInfo(sevenExe, "x \"" + archive + "\" -o\"" + appDir + "\" -y -bsp1")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var regex = new Regex(@"(\d{1,3})%");
            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    Match m = regex.Match(line);
                    if (m.Success)
                    {
                        int v;
                        if (int.TryParse(m.Groups[1].Value, out v)) SetProgress(v);
                    }
                }
                p.WaitForExit();
                if (p.ExitCode != 0) throw new Exception("解压失败（7z exit " + p.ExitCode + "）" + (err.Length > 0 ? "：" + err.Trim() : ""));
            }
            SetProgress(100);

            SetStatus("创建数据目录…");
            Directory.CreateDirectory(dataDir);

            if (shortcut)
            {
                SetStatus("创建桌面快捷方式…");
                CreateShortcut(appDir);
            }

            SetStatus("注册系统卸载项…");
            RegisterUninstall(appDir);

            SetStatus("安装完成");
            form.BeginInvoke(new Action(() =>
            {
                running = false;
                btnInstall.Enabled = true;
                txtPath.Enabled = true;
                var launch = MessageBox.Show(form, "安装完成！\n程序目录：" + appDir + "\n数据目录：" + dataDir + "\n\n是否立即启动？", "安装完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (launch == DialogResult.Yes)
                {
                    try { Process.Start(Path.Combine(appDir, "启动器.exe")); } catch { }
                }
            }));
        }
        catch (Exception ex)
        {
            SetStatus("安装失败：" + ex.Message);
            form.BeginInvoke(new Action(() =>
            {
                running = false;
                btnInstall.Enabled = true;
                txtPath.Enabled = true;
                MessageBox.Show(form, "安装失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    private static void RegisterUninstall(string appDir)
    {
        try
        {
            Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\HUST-Sports");
            key.SetValue("DisplayName", "华中大体育");
            key.SetValue("DisplayVersion", "2026.11.24");
            key.SetValue("Publisher", "HUST Sports Console");
            key.SetValue("InstallLocation", appDir);
            key.SetValue("DisplayIcon", Path.Combine(appDir, "卸载程序.exe"));
            key.SetValue("UninstallString", "\"" + Path.Combine(appDir, "卸载程序.exe") + "\"");
            key.SetValue("NoModify", 1);
            key.SetValue("NoRepair", 1);
            key.Close();
        }
        catch { /* 注册表失败不影响安装 */ }
    }

    private static void CreateShortcut(string appDir)
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = Path.Combine(desktop, "华中大体育.lnk");
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcutObj = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type scType = shortcutObj.GetType();
            scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcutObj, new object[] { Path.Combine(appDir, "启动器.exe") });
            scType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcutObj, new object[] { appDir });
            string icon = Path.Combine(appDir, "electron-app", "assets", "ic_launcher.ico");
            if (File.Exists(icon)) scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcutObj, new object[] { icon });
            scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcutObj, null);
        }
        catch { /* 快捷方式失败不影响安装 */ }
    }
}