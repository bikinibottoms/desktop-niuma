using System;
using System.IO;
using System.Linq;
using Niuma;

var day = new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.FromHours(8));
var plan = new Plan(Guid.NewGuid(), "正职", new(9, 0), new(18, 0), PayKind.Daily, 300, 22, 0);
var shift = new Shift(Guid.NewGuid(), plan, false, day, day.AddHours(8), null);
var ledger = Ledger.Empty with { Plans = [plan], Selected = plan.Id };
ledger = ledger.Put(shift);
Check(shift.Earned(day.AddHours(4)) == 150, "daily rate uses actual start");
Check(shift.Close(day.AddHours(3)).Settled == 112.50m, "early finish");
Check(shift with { Expected = day.AddHours(7) } is { Rate: > 42m }, "temporary deadline changes rate");
var after = ledger.Reconcile(day.AddDays(2));
Check(after.Active == null && after.Shifts.Single().End == day.AddHours(8)
    && after.Shifts.Single().Settled == 300, "reopen closes once at deadline");
Check(ReferenceEquals(after, after.Reconcile(day.AddDays(3))), "idempotent reconciliation");
var overtime = new Shift(Guid.NewGuid(), plan, true, day.AddHours(13), null, null);
var next = after.Put(overtime);
Check(next.Reconcile(day.AddDays(2)).Active == overtime, "overtime survives restart and midnight");
next = next.Put(overtime.Close(day.AddHours(17)));
Check(next.Shifts.All(s => s.Day.Month == 10) && next.Shifts.Sum(s => s.Settled) == 300
    && next.Shifts.Single(s => s.Overtime).Hours(day) == 4, "overnight attribution and zero overtime rate");
Check(next.Shifts.Select(s => s.Day).Distinct().Count() == 1, "multiple shifts count as one day");
try
{
    next.Put(shift with { Id = Guid.NewGuid() });
    throw new Exception("overlap accepted");
}
catch (ArgumentException) { }
var monthly = plan with { Pay = 6600, Kind = PayKind.Monthly, OvertimeRate = null };
Check((shift with { Plan = monthly }).Close(day.AddHours(8)).Settled == 300, "monthly normalization");
next = next with { Plans = [plan with { Pay = 600 }] };
Check(next.Shifts.Single(s => !s.Overtime).Settled == 300, "editing plan preserves settled pay");
var corrected = shift.Close(day.AddHours(2));
next = next.Put(corrected);
Check(next.Shifts.Sum(s => s.Settled) == 75, "correction replaces the old record");
var directory = Path.Combine(Path.GetTempPath(), "niuma-check-" + Guid.NewGuid());
var file = Path.Combine(directory, "ledger.json");
var store = new Store(file);
store.Save(after);
store.Save(next);
Check(store.Load().Shifts.Count == 2, "atomic round trip");
File.WriteAllText(file, "{broken");
Check(store.Load().Shifts.Count == 1 && store.Recovered, "backup recovery");
foreach (var entry in Directory.GetFiles(directory)) File.Delete(entry);
Directory.Delete(directory);
Console.WriteLine("Ledger checks passed.");

static void Check(bool result, string name)
{
    if (!result) throw new Exception("Failed: " + name);
}
