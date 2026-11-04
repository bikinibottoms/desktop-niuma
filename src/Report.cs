using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Niuma;

sealed partial class Widget
{
    private sealed record PlanFilter(Guid? Id, string Name);
    private sealed record DayRow(DateOnly Day, string Label);

    private void Report()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = new DateOnly(today.Year, today.Month, 1);
        var editor = new Editor(this, "月报与累计") { Width = 750 };
        var filters = new[] { new PlanFilter(null, "全部计划") }
            .Concat(data.Plans.Select(p => new PlanFilter(p.Id, p.Name))).ToArray();
        var filter = editor.Choice("按计划筛选", filters, 0, nameof(PlanFilter.Name));
        var monthLabel = Ui.Text("", 18);
        var totals = Ui.Stack();
        var cumulative = Ui.Money();
        var days = new ListBox { DisplayMemberPath = nameof(DayRow.Label), MaxHeight = 145 };
        var detail = Ui.Stack();
        var since = new DatePicker { SelectedDate = data.Shifts.Where(s => s.End != null).Select(s => s.Day)
            .DefaultIfEmpty(today).Min().ToDateTime(TimeOnly.MinValue) };
        editor.Add("查看月份", Ui.Row(Ui.Button("‹ 上月", () => ChangeMonth(-1)), monthLabel,
            Ui.Button("下月 ›", () => ChangeMonth(1))));
        editor.Add("月度汇总", totals);
        editor.Add("从某日开始至今 · 含起始日", Ui.Row(since, cumulative));
        editor.Add("逐日明细", days);
        editor.Add("", Ui.Button("补录打卡", () => EditShift(editor, null, SelectedDay()), true));
        editor.Add("", detail);
        filter.SelectionChanged += (_, _) => Refresh();
        since.SelectedDateChanged += (_, _) => DrawCumulative();
        days.SelectionChanged += (_, _) => DrawDay();
        Ledger? displayed = null;
        var refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        refreshTimer.Tick += (_, _) => { if (!ReferenceEquals(displayed, data)) Refresh(); };
        editor.Closed += (_, _) => refreshTimer.Stop();
        Refresh();
        refreshTimer.Start();
        editor.ShowDialog();
        Tick();

        DateOnly SelectedDay() => (days.SelectedItem as DayRow)?.Day ??
            (month.Year == today.Year && month.Month == today.Month ? today : month);

        Shift[] Matching()
        {
            var id = (filter.SelectedItem as PlanFilter)?.Id;
            return data.Shifts.Where(s => s.End != null && (id == null || s.Plan.Id == id)).ToArray();
        }

        void ChangeMonth(int offset)
        {
            if ((offset < 0 && month.Year == 1 && month.Month == 1) ||
                (offset > 0 && month.Year == 9999 && month.Month == 12)) return;
            month = month.AddMonths(offset);
            Refresh();
        }

        void Refresh()
        {
            displayed = data;
            monthLabel.Text = $"　{month:yyyy 年 MM 月}　";
            var now = DateTimeOffset.Now;
            var matching = Matching().Where(s => s.Day.Year == month.Year && s.Day.Month == month.Month).ToArray();
            var grouped = matching.GroupBy(s => s.Day).OrderByDescending(g => g.Key).ToArray();
            var hours = matching.Sum(s => s.Hours(now));
            var pay = matching.Sum(s => s.Earned(now));
            totals.Children.Clear();
            totals.Children.Add(Ui.Text($"{grouped.Length} 个工作日　共 {Ui.Hours(hours)}　加班 {Ui.Hours(matching.Where(s => s.Overtime).Sum(s => s.Hours(now)))}"));
            totals.Children.Add(Ui.Money($"总收入 {Ui.Yuan(pay)}　平均时薪 {(hours > 0 ? Ui.Yuan(pay / hours) : "—")}"));
            var selected = (days.SelectedItem as DayRow)?.Day;
            var rows = grouped.Select(g => new DayRow(g.Key,
                $"{g.Key:MM-dd ddd}　{g.Count()} 段　{Ui.Hours(g.Sum(s => s.Hours(now)))}" +
                (data.Moods.TryGetValue(g.Key, out var mood) ? $"　{mood.Feeling}" : ""))).ToArray();
            days.ItemsSource = rows;
            days.SelectedItem = rows.FirstOrDefault(r => r.Day == selected) ?? rows.FirstOrDefault();
            DrawCumulative();
            DrawDay();
        }

        void DrawCumulative()
        {
            if (since.SelectedDate is not { } date) { cumulative.Text = "　请选择起始日"; return; }
            var start = DateOnly.FromDateTime(date);
            var now = DateTimeOffset.Now;
            var end = DateOnly.FromDateTime(now.Date);
            cumulative.Text = "　" + (start > end ? "起始日晚于今天" :
                Ui.Yuan(Matching().Where(s => s.Day >= start && s.Day <= end).Sum(s => s.Earned(now))));
        }

        void DrawDay()
        {
            detail.Children.Clear();
            if (days.SelectedItem is not DayRow row)
            {
                detail.Children.Add(Ui.Text("本月暂无已结算打卡。", 13, Ui.Muted));
                return;
            }

            var now = DateTimeOffset.Now;
            var shifts = Matching().Where(s => s.Day == row.Day).OrderBy(s => s.Start).ToArray();
            var hours = shifts.Sum(s => s.Hours(now));
            foreach (var (label, overtime) in new[] { ("正常班", false), ("加班", true) })
            {
                var entries = shifts.Where(s => s.Overtime == overtime).ToArray();
                detail.Children.Add(Ui.Row(Ui.Text($"{label} {Ui.Hours(entries.Sum(s => s.Hours(now)))}　"),
                    Ui.Money(Ui.Yuan(entries.Sum(s => s.Earned(now))))));
            }

            detail.Children.Add(Ui.Money($"当天累计 {Ui.Yuan(shifts.Sum(s => s.Earned(now)))}　平均时薪 " +
                (hours > 0 ? Ui.Yuan(shifts.Sum(s => s.Earned(now)) / hours) : "—")));
            if (data.Moods.TryGetValue(row.Day, out var mood))
                detail.Children.Add(Ui.Text($"{mood.Feeling}　{mood.Note}", 14, Ui.Muted));
            detail.Children.Add(Ui.Button("编辑心情", () => { EditMood(row.Day, editor); Refresh(); }));

            foreach (var shift in shifts)
            {
                detail.Children.Add(Ui.Row(Ui.Text($"{shift.Plan.Name} · {(shift.Overtime ? "加班" : "正常班")}　" +
                    $"{shift.Start:MM-dd HH:mm} → {shift.End:MM-dd HH:mm}　"), Ui.Money(Ui.Yuan(shift.Earned(now)))));
                detail.Children.Add(Ui.Row(Ui.Button("修改", () => EditShift(editor, shift, row.Day)),
                    Ui.Button("删除", () =>
                    {
                        if (MessageBox.Show(editor, "删除这段打卡？", "确认删除", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            try { Save(data with { Shifts = [.. data.Shifts.Where(s => s.Id != shift.Id)] }); Refresh(); }
                            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
                            { Ui.Error(editor, error); }
                        }
                    })));
            }
        }

        void EditShift(Window owner, Shift? shift, DateOnly day)
        {
            if (shift == null && data.Plans.Count == 0) { Ui.Error(owner, new ArgumentException("请先新增上班计划。")); return; }
            var form = new Editor(owner, shift == null ? "补录打卡" : "修改打卡");
            var available = shift == null ? data.Plans.ToArray() : [shift.Plan];
            var plan = form.Choice("计划", available, Math.Max(0, Array.IndexOf(available, data.SelectedPlan)), nameof(Plan.Name));
            var kind = form.Choice("班次", new[] { "正常班", "加班" }, shift?.Overtime == true ? 1 : 0);
            plan.IsEnabled = kind.IsEnabled = shift == null;
            var start = form.Field("实际开始 · YYYY-MM-DD HH:mm", shift == null ? $"{day:yyyy-MM-dd} 09:00" : Ui.Stamp(shift.Start));
            var end = form.Field("实际结束 · YYYY-MM-DD HH:mm", shift?.End is { } stop ? Ui.Stamp(stop) : $"{day:yyyy-MM-dd} 18:00");
            var expected = form.Field("预计下班 · 正常班留空时使用实际结束", shift?.Expected is { } cutoff ? Ui.Stamp(cutoff) : "");
            kind.SelectionChanged += (_, _) => expected.IsEnabled = kind.SelectedIndex == 0;
            expected.IsEnabled = kind.SelectedIndex == 0;
            form.Finish(() => Save(Prepare().Updated),
                () => form.Status("结算金额 " + Ui.Yuan(Prepare().Entry.Settled!.Value)));
            Refresh();

            (Ledger Updated, Shift Entry) Prepare()
            {
                var now = DateTimeOffset.Now;
                Reconcile(now);
                var first = shift != null && start.Text == Ui.Stamp(shift.Start) ? shift.Start : Ui.ParseStamp(start.Text);
                var last = shift?.End is { } previousEnd && end.Text == Ui.Stamp(previousEnd) ? previousEnd : Ui.ParseStamp(end.Text);
                if (last <= first || last > now) throw new ArgumentException("实际结束须晚于开始，且不能在未来。");
                bool overtime = kind.SelectedIndex == 1;
                DateTimeOffset? planned = null;
                if (!overtime)
                {
                    planned = shift?.Expected is { } previousExpected && expected.Text == Ui.Stamp(previousExpected)
                        ? previousExpected : string.IsNullOrWhiteSpace(expected.Text) ? last : Ui.ParseStamp(expected.Text);
                    ValidateExpected(first, planned.Value);
                    if (last > planned) throw new ArgumentException("正常班的实际结束不能晚于预计下班，加班请另记。");
                }

                var entry = new Shift(shift?.Id ?? Guid.NewGuid(), (Plan)plan.SelectedItem, overtime, first, planned, null).Close(last);
                return (data.Put(entry), entry);
            }
        }
    }
}
