using System.Drawing;
using WinForms = System.Windows.Forms;

namespace MiniMixer.Services;

/// <summary>Icono en la bandeja del sistema (área de notificación) con su menú.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _autostartItem;

    public TrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();

        var open = new WinForms.ToolStripMenuItem("Abrir Mini-Mixer") { Font = new Font(WinForms.Control.DefaultFont, FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke();

        _autostartItem = new WinForms.ToolStripMenuItem("Iniciar con Windows")
        {
            CheckOnClick = true,
            Checked = AppSettings.Current.StartWithWindows
        };
        _autostartItem.Click += (_, _) =>
        {
            try { StartupRegistration.SetEnabled(_autostartItem.Checked); }
            catch { _autostartItem.Checked = AppSettings.Current.StartWithWindows; }
        };

        var exit = new WinForms.ToolStripMenuItem("Salir");
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(open);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exit);
        menu.Opening += (_, _) => _autostartItem.Checked = AppSettings.Current.StartWithWindows;

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Mini-Mixer",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.Info);

    private static Icon LoadIcon()
    {
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
        using var stream = info!.Stream;
        return new Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
