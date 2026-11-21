using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace CursorBlur;

internal sealed class TouchpadForm : Form
{
    private readonly Button apply = new() { Text = "应用 macOS 风格预设", AutoSize = true };
    private readonly Button restore = new() { Text = "还原预设前设置", AutoSize = true };
    private readonly NumericUpDown speed = new() { Minimum = 1, Maximum = 20, Width = 64, AccessibleName = "触控板指针速度" };
    private readonly Button saveSpeed = new() { Text = "设置速度", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.FromArgb(84, 97, 120) };

    public TouchpadForm(Icon icon)
    {
        Text = "动态模糊光标 · 触控板";
        Icon = icon;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(247, 249, 255);
        ForeColor = Color.FromArgb(32, 39, 62);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(470, 270);
        MinimumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Padding = new Padding(20, 12, 20, 14),
            Dock = DockStyle.Fill,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var description = new Label
        {
            Text = "双指自然滚动和捏合缩放、单指轻点和连点拖拽、双指轻点右键。\n预设关闭角落右键区，启用触控板自己的指针加速。",
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 10),
        };
        var windowsSettings = new Button { Text = "打开 Windows 触控板与手势设置", AutoSize = true };
        var speedLabel = new Label { Text = "触控板指针速度", AutoSize = true, Anchor = AnchorStyles.Left };
        layout.Controls.Add(description);
        layout.Controls.Add(Row(apply, restore));
        layout.Controls.Add(Row(speedLabel, speed, saveSpeed));
        layout.Controls.Add(windowsSettings);
        layout.Controls.Add(status);
        status.Margin = new Padding(3, 8, 3, 3);
        status.MaximumSize = new Size(415, 0);

        apply.Click += (_, _) => Run(() => TouchpadProfile.Apply(), "已应用 macOS 风格预设。");
        restore.Click += (_, _) => Run(() => TouchpadProfile.Restore(), "已还原预设前设置。");
        saveSpeed.Click += (_, _) => Run(() => TouchpadProfile.SetSpeed((int)speed.Value), "已更新触控板指针速度。");
        windowsSettings.Click += (_, _) => Run(
            () => Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad") { UseShellExecute = true })?.Dispose(),
            "可在 Windows 设置里自定义三指和四指手势。");
        Activated += (_, _) => RefreshControls();
        RefreshControls();
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 5) };
        row.Controls.AddRange(controls);
        return row;
    }

    private void RefreshControls()
    {
        bool supported = TouchpadProfile.TryRead(out var options);
        bool matches = supported && TouchpadProfile.Matches(options);
        apply.Enabled = supported && !matches;
        restore.Enabled = supported && TouchpadProfile.HasBackup();
        speed.Enabled = saveSpeed.Enabled = supported;
        if (supported) speed.Value = Math.Clamp(options.CursorSpeed, 1u, 20u);
        status.Text = !supported ? "一键预设需要 Windows 11 24H2 或更新版本，以及精确式触控板。"
            : matches ? "当前设置符合 macOS 风格预设。"
            : "应用预设前会保存当前手势设置。";
    }

    private void Run(Action action, string message)
    {
        try
        {
            action();
            RefreshControls();
            status.Text = message;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException or IOException or SecurityException)
        {
            status.Text = "操作失败。 " + ex.Message;
        }
    }
}
