using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Niuma;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\DesktopNiuma.v1", out bool first);
        if (!first)
        {
            MessageBox.Show("桌面牛马已经在运行，请从系统托盘打开。", "桌面牛马");
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            var store = new Store(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "DesktopNiuma", "ledger.json"));
            using var window = new Widget(store);
            app.Run(window);
        }
        catch (Exception error)
        {
            MessageBox.Show($"无法启动桌面牛马。\n{error.Message}", "桌面牛马",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

sealed partial class Widget : Window, IDisposable
{
    private readonly Store store;
    private Ledger data;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly DispatcherTimer timer;
    private readonly TextBlock cash = Ui.Money("", 46);
    private readonly TextBlock state = Ui.Text("", 13, Ui.Accent);
    private readonly TextBlock detail = Ui.Text();
    private readonly TextBlock shiftCash = Ui.Money();
    private readonly TextBlock monthCash = Ui.Money("", 14);
    private readonly TextBlock slogan = Ui.Text("", 13, Ui.Muted);
    private readonly TextBlock notice = Ui.Text("", 13, Ui.Accent);
    private readonly TextBlock progressLabel = Ui.Text("", 13, Ui.Accent);
    private readonly Meter progress = new();
    private readonly StackPanel goalContent = Ui.Stack();
    private readonly TextBlock goalTitle = Ui.Text("", 15);
    private readonly TextBlock goalMoney = Ui.Money("", 14);
    private readonly TextBlock goalTime = Ui.Text("", 12, Ui.Muted);
    private readonly Meter goalMeter = new();
    private readonly Button nextGoal;
    private int goalIndex;
    private Ledger? displayedGoals;
    private readonly ComboBox plans = new() { MinWidth = 155, MaxWidth = 230, DisplayMemberPath = nameof(Plan.Name) };
    private readonly WrapPanel actions = Ui.Row();
    private readonly Button pin;
    private bool selecting;
    private bool quitting;
    private bool closePending;
    private Guid? displayedShift;
    private bool actionInitialized;
    private DateTimeOffset waterDue;
    private DateTimeOffset eyesDue;
    private DateTimeOffset noticeUntil;
    private static readonly string[] Slogans = ["今天的牛马，正在给明天的奶茶打工。",
        "一秒一秒攒，离想买的东西又近一点。", "工位可以坐，肩膀记得放下来。",
        "今天也有认真打工，收工就好好休息。"];

    public Widget(Store store)
    {
        this.store = store;
        data = store.Load();
        Title = "桌面牛马";
        Width = 425;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        ShowInTaskbar = true;
        Topmost = data.Preferences.Topmost;
        var iconUri = new Uri("pack://application:,,,/Assets/Niuma.ico");
        Icon = BitmapFrame.Create(iconUri);
        using (var iconStream = Application.GetResourceStream(iconUri)!.Stream)
            trayIcon = new System.Drawing.Icon(iconStream, 32, 32);

        pin = Ui.Button(Topmost ? "取消置顶" : "置顶", () => Guard(() =>
        {
            Save(data with { Preferences = data.Preferences with { Topmost = !Topmost } });
            Topmost = data.Preferences.Topmost;
            pin!.Content = Topmost ? "取消置顶" : "置顶";
        }));
        var header = new DockPanel { Background = Brushes.Transparent };
        var minimize = Ui.Button("−", () => WindowState = WindowState.Minimized);
        minimize.ToolTip = "最小化到任务栏";
        var close = Ui.Button("×", Close);
        close.ToolTip = "关闭选项";
        var controls = Ui.Row(pin, minimize, close);
        DockPanel.SetDock(controls, Dock.Right);
        header.Children.Add(controls);
        var emblem = new Image { Source = Icon, Width = 25, Height = 25, Margin = new Thickness(0, 0, 7, 0) };
        header.Children.Add(Ui.Row(emblem, Ui.Text("桌面牛马", 20)));
        header.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        plans.SelectionChanged += (_, _) =>
        {
            if (!selecting && plans.SelectedItem is Plan plan)
            {
                Guard(() => Save(data with { Selected = plan.Id }));
                RefreshPlans();
            }
        };
        nextGoal = Ui.Button("下一目标 ›", () =>
        {
            goalIndex++;
            DrawGoal();
        });
        var goalHeader = new DockPanel();
        var goalControls = Ui.Row(nextGoal, Ui.Button("设置目标", () => Guard(Goals)));
        DockPanel.SetDock(goalControls, Dock.Right);
        goalHeader.Children.Add(goalControls);
        goalHeader.Children.Add(Ui.Text("趣味目标", 14, Ui.Accent));
        var goalCard = new Border { Background = Ui.Panel, CornerRadius = new CornerRadius(12),
            Padding = new Thickness(11, 4, 11, 8), Margin = new Thickness(0, 8, 0, 5),
            Child = Ui.Stack(goalHeader, goalTitle, goalContent) };
        goalContent.Children.Add(goalMeter);
        goalContent.Children.Add(goalMoney);
        goalContent.Children.Add(goalTime);
        var body = Ui.Stack(header, Ui.Row(Ui.Text("今日已赚　", 14, Ui.Muted), state), cash,
            detail, shiftCash, progressLabel, progress, Ui.Row(Ui.Text("本月已结算　", 13, Ui.Muted), monthCash),
            Ui.Row(plans, Ui.Button("管理计划", () => Guard(ManagePlans))), actions,
            goalCard, Ui.Row(Ui.Button("月报", () => Guard(Report)),
                Ui.Button("提醒设置", () => Guard(Reminders))), notice, slogan);
        Content = new Border { Padding = new Thickness(19, 13, 19, 15), Background = Ui.Background,
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Child = body };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏小窗", null, (_, _) => Toggle());
        menu.Items.Add("退出", null, (_, _) => Quit());
        tray = new Forms.NotifyIcon { Icon = trayIcon,
            Text = "桌面牛马", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) Toggle(); };

        Closing += (_, args) =>
        {
            if (quitting) return;
            args.Cancel = true;
            if (closePending) return;
            closePending = true;
            Dispatcher.BeginInvoke(() =>
            {
                try { ChooseClose(); }
                finally { closePending = false; }
            });
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 20;
            Top = Math.Max(area.Top, area.Bottom - ActualHeight - 30);
            if (store.Recovered) Hint("本地账本已从上一份备份恢复。");
        };
        SizeChanged += (_, args) =>
        {
            if (!IsLoaded || WindowState != WindowState.Normal) return;
            var area = SystemParameters.WorkArea;
            if (Top < area.Bottom && Top + args.NewSize.Height > area.Bottom)
                Top = Math.Max(area.Top, area.Bottom - args.NewSize.Height);
        };

        RefreshPlans();
        Tick();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    public void Dispose()
    {
        timer.Stop();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        trayIcon.Dispose();
    }

    private void Toggle()
    {
        if (IsVisible && WindowState != WindowState.Minimized) Hide();
        else RestoreWidget();
    }

    private void RestoreWidget()
    {
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ChooseClose()
    {
        RestoreWidget();
        var form = new Editor(this, "关闭桌面牛马");
        bool exit = false;
        form.Add("隐藏到托盘后，提醒和打卡会继续运行。", Ui.Row(
            Ui.Button("最小化到托盘", () => form.DialogResult = true, true),
            Ui.Button("彻底退出", () => { exit = true; form.DialogResult = true; }),
            Ui.Button("取消", form.Close)));
        if (form.ShowDialog() != true) return;
        if (exit) Quit();
        else Hide();
    }

    private void Quit()
    {
        quitting = true;
        Application.Current.Shutdown();
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        { Ui.Error(this, error); }
    }

    private void Save(Ledger next)
    {
        store.Save(next);
        data = next;
    }

    private void RefreshPlans()
    {
        selecting = true;
        plans.ItemsSource = data.Plans;
        plans.SelectedItem = data.SelectedPlan;
        selecting = false;
    }

    private void Reconcile(DateTimeOffset now)
    {
        var updated = data.Reconcile(now);
        if (!ReferenceEquals(data, updated)) Save(updated);
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        try { Reconcile(now); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Hint($"保存结算失败，将重试。{error.Message}"); }

        var today = DateOnly.FromDateTime(now.Date);
        var active = data.Active;
        var dayShifts = data.Shifts.Where(s => s.Day == today).ToArray();
        var recent = data.Shifts.Where(s => s.End != null).OrderByDescending(s => s.End).FirstOrDefault();
        cash.Text = Ui.Yuan(dayShifts.Sum(s => s.Earned(now)));
        monthCash.Text = Ui.Yuan(data.Shifts.Where(s => s.End != null && s.Day.Year == today.Year &&
            s.Day.Month == today.Month).Sum(s => s.Earned(now)));
        state.Text = active == null ? "●  已下班" : active.Overtime ? "●  加班中" : "●  上班中";
        if (active == null)
        {
            detail.Text = recent == null ? "选一份计划，开始今天的打工。" :
                $"最近结算 · {recent.Day:MM-dd} · {recent.Plan.Name} · {(recent.Overtime ? "加班" : "正常班")} {Ui.Hours(recent.Hours(now))}";
            shiftCash.Text = recent == null ? "" : $"本班 {Ui.Yuan(recent.Earned(now))}    当天累计 " +
                Ui.Yuan(data.Shifts.Where(s => s.Day == recent.Day).Sum(s => s.Earned(now)));
        }
        else
        {
            detail.Text = active.Overtime
                ? $"已加班 {Ui.Clock(now - active.Start)}"
                : $"已工作 {Ui.Clock(active.EffectiveEnd(now) - active.Start)}    距下班 {Ui.Clock(active.Expected!.Value - now)}";
            shiftCash.Text = $"本班 {Ui.Yuan(active.Earned(now))}    时薪 {Ui.Yuan(active.Rate)}";
        }

        progressLabel.Visibility = progress.Visibility = active?.Expected != null ? Visibility.Visible : Visibility.Collapsed;
        var fraction = active?.Expected is { } end
            ? Math.Clamp((active.EffectiveEnd(now) - active.Start).TotalSeconds / (end - active.Start).TotalSeconds, 0, 1)
            : 0;
        progress.Set(fraction);
        progressLabel.Text = $"上班进度  {Ui.Percent(fraction)}";
        if (!ReferenceEquals(displayedGoals, data)) DrawGoal();
        slogan.Text = Slogans[(now.Hour * 6 + now.Minute / 10) % Slogans.Length];

        if (!actionInitialized || active?.Id != displayedShift)
        {
            actionInitialized = true;
            displayedShift = active?.Id;
            actions.Children.Clear();
            if (active != null)
                actions.Children.Add(Ui.Button(active.Overtime ? "结束加班" : "提前下班", () => Guard(EndShift), true));
            else
            {
                actions.Children.Add(Ui.Button("开始上班", () => Guard(StartNormal), true));
                actions.Children.Add(Ui.Button("开始加班", () => Guard(StartOvertime)));
                actions.Children.Add(Ui.Button("记心情", () => Guard(() => EditMood(DateOnly.FromDateTime(DateTime.Today)))));
            }
            ResetReminders(now);
        }
        if (active != null && active.EffectiveEnd(now) == now)
        {
            var water = ReminderDue(now, ref waterDue, data.Preferences.Water);
            var eyes = ReminderDue(now, ref eyesDue, data.Preferences.Eyes);
            if (water || eyes)
                Hint(water && eyes ? "喝口水，顺便让眼睛休息一下。" : water ? "该喝口水啦。" : "抬头看看远处，放松一下眼睛。", true);
        }
        if (now >= noticeUntil) notice.Visibility = Visibility.Collapsed;
    }

    private void ResetReminders(DateTimeOffset now)
    {
        waterDue = now.AddMinutes(data.Preferences.Water);
        eyesDue = now.AddMinutes(data.Preferences.Eyes);
    }

    private static bool ReminderDue(DateTimeOffset now, ref DateTimeOffset due, int minutes)
    {
        if (minutes <= 0 || now < due) return false;
        bool fresh = now - due < TimeSpan.FromSeconds(5);
        due = now.AddMinutes(minutes);
        return fresh;
    }

    private void Hint(string text, bool notify = false)
    {
        notice.Text = text;
        notice.Visibility = Visibility.Visible;
        noticeUntil = DateTimeOffset.Now.AddSeconds(30);
        if (notify && (!IsVisible || WindowState == WindowState.Minimized))
            tray.ShowBalloonTip(5000, "桌面牛马", text, Forms.ToolTipIcon.Info);
    }
}
