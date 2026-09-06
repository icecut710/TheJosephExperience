using System.Drawing;
using System.Windows.Forms;

namespace JosephExperience.Services;

public class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private bool _disposed;
    private bool _shown;

    public TrayService()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon();

        _menu = new ContextMenuStrip();
        _menu.Items.Add("The Joseph Experience", null, (_, _) => OpenRequested?.Invoke());
        _menu.Items.Add("Celebrate", null, (_, _) => OpenRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Enable Celebrations", null, (_, _) => ToggleEnabledRequested?.Invoke());
        _menu.Items.Add("Test Joseph", null, (_, _) => TestRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.Text = "The Joseph Experience";
        _notifyIcon.DoubleClick += (_, _) =>
        {
            if (_menu.Visible)
            {
                _menu.Close();
            }
            OpenRequested?.Invoke();
        };
    }

    public event Action? OpenRequested;
    public event Action? ToggleEnabledRequested;
    public event Action? TestRequested;
    public event Action? ExitRequested;

    public void Show(string iconPath, string toolTip)
    {
        if (_shown) return; // already shown — do not create a second tray icon

        _notifyIcon.Icon = CreateIcon(iconPath);
        _notifyIcon.Text = toolTip;
        _notifyIcon.Visible = true;
        _shown = true;
    }

    private static System.Drawing.Icon CreateIcon(string? iconPath)
    {
        try
        {
            if (!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))
            {
                return new System.Drawing.Icon(iconPath);
            }
        }
        catch
        {
            // Fall through to fallback.
        }

        // Fallback: try the bundled AppIcon.ico from the assembly-embedded resource
        try
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly();
            var resName = "JosephExperience.Assets.AppIcon.ico";
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream is not null)
            {
                var buf = new byte[stream.Length];
                stream.Read(buf, 0, buf.Length);
                using var ms = new System.IO.MemoryStream(buf);
                return new System.Drawing.Icon(ms);
            }
        }
        catch
        {
            // Fall through to drawn icon.
        }
        try
        {
            var bmp = new System.Drawing.Bitmap(64, 64);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                using var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x4F, 0x8C, 0xFF));
                g.FillEllipse(bg, 2, 2, 60, 60);
                using var font = new System.Drawing.Font("Segoe UI", 22, System.Drawing.FontStyle.Bold);
                using var fg = new System.Drawing.SolidBrush(System.Drawing.Color.White);
                using var sf = new System.Drawing.StringFormat
                {
                    Alignment = System.Drawing.StringAlignment.Center,
                    LineAlignment = System.Drawing.StringAlignment.Center
                };
                g.DrawString("GJ", font, fg, new System.Drawing.RectangleF(0, 0, 64, 64), sf);
            }
            using var ico = System.Drawing.Icon.FromHandle(bmp.GetHicon());
            return (System.Drawing.Icon)ico.Clone();
        }
        catch
        {
            return System.Drawing.SystemIcons.Information;
        }
    }

    public void UpdateEnabledState(bool enabled)
    {
        if (_menu.Items.Count > 3 && _menu.Items[3] is ToolStripMenuItem item)
        {
            item.Checked = enabled;
        }
    }

    public void UpdateSoundStatus(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
             _notifyIcon.Text = "The Joseph Experience";
        }
        else
        {
            _notifyIcon.Text = $"The Joseph Experience\n{status}";
        }
    }

    public void Hide()
    {
        _notifyIcon.Visible = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}