using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Niuma;

enum PayKind { Hourly, Daily, Monthly }

record Plan(Guid Id, string Name, TimeOnly Start, TimeOnly End, PayKind Kind,
    decimal Pay, int PayDays, decimal? OvertimeRate)
{
    [JsonIgnore]
    public decimal Daily => Kind == PayKind.Monthly ? Pay / PayDays : Pay;
    [JsonIgnore]
    public decimal DefaultRate => Kind == PayKind.Hourly ? Pay : Daily /
        ((End.Ticks - Start.Ticks + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay /
            (decimal)TimeSpan.TicksPerHour);
}

record Shift(Guid Id, Plan Plan, bool Overtime, DateTimeOffset Start,
    DateTimeOffset? Expected, DateTimeOffset? End, decimal? Settled = null)
{
    [JsonIgnore]
    public DateOnly Day => DateOnly.FromDateTime(Start.Date);
    public DateTimeOffset EffectiveEnd(DateTimeOffset now) =>
        End ?? (Expected is { } cutoff && cutoff < now ? cutoff : now);
    public decimal Hours(DateTimeOffset now) => Math.Max(0,
        (EffectiveEnd(now) - Start).Ticks / (decimal)TimeSpan.TicksPerHour);
    [JsonIgnore]
    public decimal Rate => Overtime ? Plan.OvertimeRate ?? Plan.DefaultRate :
        Plan.Kind == PayKind.Hourly ? Plan.Pay : Plan.Daily /
        ((Expected!.Value - Start).Ticks / (decimal)TimeSpan.TicksPerHour);
    public decimal Earned(DateTimeOffset now) =>
        Settled ?? Math.Round(Hours(now) * Rate, 2, MidpointRounding.AwayFromZero);
    public Shift Close(DateTimeOffset end)
    {
        var closed = this with { End = end, Settled = null };
        return closed with { Settled = closed.Earned(end) };
    }
}

record Mood(string Feeling, string Note);
record Goal(Guid Id, string Name, decimal Amount, DateOnly Since);
record Preferences(bool Topmost = false, int Water = 60, int Eyes = 30);
record Ledger(List<Plan> Plans, List<Shift> Shifts, Dictionary<DateOnly, Mood> Moods,
    List<Goal> Goals, Guid? Selected, Preferences Preferences)
{
    public static Ledger Empty => new([], [], [], [], null, new());
    [JsonIgnore]
    public Shift? Active => Shifts.SingleOrDefault(s => s.End == null);
    [JsonIgnore]
    public Plan? SelectedPlan => Plans.FirstOrDefault(p => p.Id == Selected) ?? Plans.FirstOrDefault();

    public Ledger Put(Shift entry)
    {
        DateTimeOffset stop = entry.End ?? DateTimeOffset.MaxValue;
        if (Shifts.Any(s => s.Id != entry.Id && s.Start < stop &&
            entry.Start < (s.End ?? DateTimeOffset.MaxValue)))
            throw new ArgumentException("打卡时段与另一条记录重叠。");
        return this with { Shifts = [.. Shifts.Where(s => s.Id != entry.Id), entry] };
    }

    public Ledger Reconcile(DateTimeOffset now) =>
        Active is { Expected: { } cutoff } shift && now >= cutoff
            ? Put(shift.Close(cutoff)) : this;
}

sealed class Store(string path)
{
    public Ledger Load()
    {
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return Ledger.Empty;
        try { return Read(path); }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
        {
            var recovered = Read(path + ".bak");
            if (File.Exists(path)) File.Move(path, path + "." + Guid.NewGuid() + ".damaged");
            Save(recovered);
            Recovered = true;
            return recovered;
        }
    }

    public bool Recovered { get; private set; }

    private static Ledger Read(string source) =>
        JsonSerializer.Deserialize<Ledger>(File.ReadAllBytes(source))
        ?? throw new InvalidDataException("无法读取本地账本。");

    public void Save(Ledger data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var file = File.Create(temporary))
        {
            JsonSerializer.Serialize(file, data);
            file.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
}
