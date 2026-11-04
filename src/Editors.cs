using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Niuma;

sealed partial class Widget
{
    private static readonly string[] PayLabels = ["时薪", "日薪", "月薪"];
    private static readonly string[] Feelings = ["未选择", "开心", "平静", "有点累", "压力大", "下班快乐"];

    private static string NameOf(TextBox box)
    {
        var name = box.Text.Trim();
        if (name.Length == 0) throw new ArgumentException("请填写名称。");
        return name;
    }

    private Plan RequirePlan() => data.SelectedPlan ?? throw new ArgumentException("请先在「管理计划」中新增一份计划。");

    private static int Count(string input, string label, int minimum, int maximum) =>
        int.TryParse(input, out int value) && value >= minimum && value <= maximum
            ? value : throw new ArgumentException($"{label}须为 {minimum} 至 {maximum} 的整数。");

    private static TimeOnly Time(string input) =>
        TimeOnly.TryParseExact(input.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value : throw new ArgumentException("时间请用 HH:mm 格式。");

    private void ManagePlans()
    {
        var editor = new Editor(this, "上班计划");
        var picker = editor.Choice("选择一份计划", data.Plans, data.Plans.IndexOf(data.SelectedPlan!), nameof(Plan.Name));
        editor.Add("新增计划，或修改选中的计划", Ui.Row(
            Ui.Button("新增计划", () => { EditPlan(editor, null); RefreshPicker(); }, true),
            Ui.Button("编辑选中计划", () =>
            {
                if (picker.SelectedItem is Plan plan) EditPlan(editor, plan);
                RefreshPicker();
            })));
        editor.Add("", Ui.Button("完成", editor.Close));
        editor.ShowDialog();

        void RefreshPicker()
        {
            picker.ItemsSource = data.Plans;
            picker.SelectedItem = data.SelectedPlan;
            RefreshPlans();
            Tick();
        }
    }

    private void EditPlan(Window owner, Plan? plan)
    {
        var form = new Editor(owner, plan == null ? "新增计划" : "编辑计划");
        var name = form.Field("计划名称", plan?.Name ?? "", 32);
        var start = form.Field("默认上班时间", plan?.Start.ToString("HH:mm") ?? "09:00");
        var end = form.Field("默认下班时间 · 早于上班时间表示次日", plan?.End.ToString("HH:mm") ?? "18:00");
        var kind = form.Choice("薪资方式", PayLabels, (int)(plan?.Kind ?? PayKind.Hourly));
        var amount = form.Field("薪资（元）", plan?.Pay.ToString(CultureInfo.InvariantCulture) ?? "");
        var days = form.Field("每月计薪天数 · 仅用于月薪", (plan?.PayDays ?? 22).ToString());
        var overtime = form.Field("加班时薪（元） · 留空用计划默认时薪", plan?.OvertimeRate?.ToString(CultureInfo.InvariantCulture) ?? "");
        form.Finish(() =>
        {
            var first = Time(start.Text);
            var last = Time(end.Text);
            if (first == last) throw new ArgumentException("默认上下班时间不能相同。");
            var kindValue = (PayKind)kind.SelectedIndex;
            var next = new Plan(plan?.Id ?? Guid.NewGuid(), NameOf(name), first, last, kindValue,
                Ui.Amount(amount.Text, "薪资"), kindValue == PayKind.Monthly ? Count(days.Text, "计薪天数", 1, 31) : 22,
                string.IsNullOrWhiteSpace(overtime.Text) ? null : Ui.Amount(overtime.Text, "加班时薪"));
            var list = data.Plans.ToList();
            if (plan == null) list.Add(next);
            else list[list.FindIndex(p => p.Id == plan.Id)] = next;
            Save(data with { Plans = list, Selected = next.Id });
        });
    }

    private void StartNormal()
    {
        var plan = RequirePlan();
        var now = DateTimeOffset.Now;
        var suggested = now.Date + plan.End.ToTimeSpan();
        if (suggested <= now.LocalDateTime) suggested = suggested.AddDays(1);
        var editor = new Editor(this, "开始上班 · " + plan.Name);
        var end = editor.Field("预计下班时间 · YYYY-MM-DD HH:mm", suggested.ToString("yyyy-MM-dd HH:mm"));
        editor.Finish(() =>
        {
            var start = DateTimeOffset.Now;
            var expected = Ui.ParseStamp(end.Text);
            ValidateExpected(start, expected);
            Start(plan, false, start, expected);
        });
    }

    private void StartOvertime() => Start(RequirePlan(), true, DateTimeOffset.Now, null);

    private void Start(Plan plan, bool overtime, DateTimeOffset now, DateTimeOffset? expected)
    {
        Reconcile(now);
        if (data.Active != null) throw new ArgumentException("请先结束正在进行的打卡。");
        Save(data.Put(new Shift(Guid.NewGuid(), plan, overtime, now, expected, null)));
        Tick();
    }

    private void EndShift()
    {
        var now = DateTimeOffset.Now;
        Reconcile(now);
        if (data.Active is { } active)
        {
            if (now <= active.Start) throw new ArgumentException("结束时间必须晚于开始时间。");
            Save(data.Put(active.Close(now)));
        }
        Tick();
    }

    private static void ValidateExpected(DateTimeOffset start, DateTimeOffset expected)
    {
        if (expected <= start || expected - start > TimeSpan.FromDays(1))
            throw new ArgumentException("预计下班须晚于开工，并在开工后 24 小时内。");
    }

    private void EditMood(DateOnly day, Window? owner = null)
    {
        data.Moods.TryGetValue(day, out var mood);
        var editor = new Editor(owner ?? this, $"{day:yyyy-MM-dd} · 今日心情");
        var feeling = editor.Choice("心情", Feelings, Math.Max(0, Array.IndexOf(Feelings, mood?.Feeling)));
        var note = editor.Field("一句话", mood?.Note ?? "", 150);
        editor.Finish(() =>
        {
            var moods = new System.Collections.Generic.Dictionary<DateOnly, Mood>(data.Moods);
            if (feeling.SelectedIndex == 0 && string.IsNullOrWhiteSpace(note.Text)) moods.Remove(day);
            else moods[day] = new Mood((string)feeling.SelectedItem, note.Text.Trim());
            Save(data with { Moods = moods });
        });
    }

    private void Reminders()
    {
        var editor = new Editor(this, "上班提醒");
        var water = editor.Field("喝水间隔（分钟） · 0 为关闭", data.Preferences.Water.ToString(), 4);
        var eyes = editor.Field("护眼间隔（分钟） · 0 为关闭", data.Preferences.Eyes.ToString(), 4);
        editor.Finish(() =>
        {
            Save(data with { Preferences = data.Preferences with
                { Water = Count(water.Text, "喝水间隔", 0, 1440), Eyes = Count(eyes.Text, "护眼间隔", 0, 1440) } });
            ResetReminders(DateTimeOffset.Now);
        });
    }

    private void DrawGoal()
    {
        displayedGoals = data;
        var count = data.Goals.Count;
        nextGoal.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        goalContent.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (count == 0)
        {
            goalTitle.Text = "设个目标，看看还要打多久的工。";
            return;
        }

        goalIndex %= count;
        var goal = data.Goals[goalIndex];
        var now = DateTimeOffset.Now;
        var earned = data.Shifts.Where(s => s.End != null && s.Day >= goal.Since).Sum(s => s.Earned(now));
        var left = Math.Max(0, goal.Amount - earned);
        var fraction = (double)Math.Min(1, earned / goal.Amount);
        var indexLabel = count > 1 ? $"  ·  {goalIndex + 1}/{count}" : "";
        goalTitle.Text = $"{goal.Name}  ·  {Ui.Percent(fraction)}{indexLabel}";
        goalMeter.Set(fraction);
        goalMoney.Text = $"{Ui.Yuan(earned)} / {Ui.Yuan(goal.Amount)}";
        var plan = data.SelectedPlan;
        goalTime.Text = left == 0 ? "目标已完成" : plan?.DefaultRate > 0
            ? $"按「{plan.Name}」还要工作约 {Ui.Hours(left / plan.DefaultRate)}"
            : "选择有时薪的计划可估算剩余工时。";
    }

    private void Goals()
    {
        var editor = new Editor(this, "设置目标");
        var list = Ui.Stack();
        editor.Add("目标进度显示在桌面小窗，按创建当日起的已结算收入累计。", list);
        editor.Add("", Ui.Button("添加目标", () => EditGoal(null), true));
        DrawList();
        editor.ShowDialog();

        void DrawList()
        {
            list.Children.Clear();
            if (data.Goals.Count == 0) list.Children.Add(Ui.Text("还没有目标。", 14, Ui.Muted));
            foreach (var goal in data.Goals)
            {
                list.Children.Add(Ui.Text($"{goal.Name} · {goal.Since:yyyy-MM-dd} 起"));
                list.Children.Add(Ui.Money(Ui.Yuan(goal.Amount)));
                list.Children.Add(Ui.Row(Ui.Button("修改", () => EditGoal(goal)), Ui.Button("删除", () =>
                {
                    if (MessageBox.Show(editor, $"删除「{goal.Name}」？", "确认删除", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        return;
                    try
                    {
                        Save(data with { Goals = [.. data.Goals.Where(g => g.Id != goal.Id)] });
                        DrawGoal();
                        DrawList();
                    }
                    catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
                    { Ui.Error(editor, error); }
                })));
            }
        }

        void EditGoal(Goal? goal)
        {
            var form = new Editor(editor, goal == null ? "添加目标" : "修改目标");
            var name = form.Field("名称 · 例如 iPhone 18、豪车或别墅", goal?.Name ?? "", 40);
            var amount = form.Field("目标金额（元）", goal?.Amount.ToString(CultureInfo.InvariantCulture) ?? "");
            form.Finish(() =>
            {
                var value = Ui.Amount(amount.Text, "目标金额");
                if (value <= 0) throw new ArgumentException("目标金额须大于零。");
                var next = new Goal(goal?.Id ?? Guid.NewGuid(), NameOf(name), value,
                    goal?.Since ?? DateOnly.FromDateTime(DateTime.Today));
                var goals = data.Goals.ToList();
                if (goal == null) goals.Add(next);
                else goals[goals.FindIndex(g => g.Id == goal.Id)] = next;
                Save(data with { Goals = goals });
                goalIndex = goals.FindIndex(g => g.Id == next.Id);
                DrawGoal();
            });
            DrawList();
        }
    }
}
