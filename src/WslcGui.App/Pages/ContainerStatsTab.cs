using System.Collections.ObjectModel;
using System.Globalization;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;

using WslcGui.Core;

namespace WslcGui.App.Pages;

/// <summary>CPU, memory, network and disk charts for one container, from the shared stats monitor.</summary>
internal sealed class ContainerStatsTab : UserControl
{
    private readonly ContainerDetailsViewModel _vm;
    private readonly ObservableCollection<ObservablePoint> _cpu = [];
    private readonly ObservableCollection<ObservablePoint> _memory = [];
    private readonly ObservableCollection<ObservablePoint> _netRx = [];
    private readonly ObservableCollection<ObservablePoint> _netTx = [];
    private readonly ObservableCollection<ObservablePoint> _diskRead = [];
    private readonly ObservableCollection<ObservablePoint> _diskWrite = [];
    private readonly TextBlock _summary = new();

    public ContainerStatsTab(ContainerDetailsViewModel vm)
    {
        _vm = vm;
        vm.StatsUpdated += Update;
        Build();
        Update();
    }

    protected override Element OnBuild() =>
        new DockPanel().Padding(0, 8, 0, 0).Children(
            _summary.DockTop().Margin(0, 0, 0, 8),
            new Grid().Rows("*, *").Columns("*, *").Children(
                Chart("CPU", Percent, Line(_cpu, "CPU")).Margin(0, 0, 6, 6),
                Chart("Memory", Bytes, Line(_memory, "Used")).Column(1).Margin(6, 0, 0, 6),
                Chart("Network", BytesPerSecond, Line(_netRx, "Received"), Line(_netTx, "Sent")).Row(1).Margin(0, 6, 6, 0),
                Chart("Disk", BytesPerSecond, Line(_diskRead, "Read"), Line(_diskWrite, "Written")).Row(1).Column(1).Margin(6, 6, 0, 0)));

    private static string Percent(double value) => value.ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Bytes(double value) => Formatting.Bytes((long)Math.Max(value, 0));

    private static string BytesPerSecond(double value) => Bytes(value) + "/s";

    private void Update()
    {
        var history = _vm.StatsHistory;
        if (history.Count == 0)
        {
            _summary.Text = _vm.Row.IsRunning
                ? "Collecting resource usage…"
                : "Resource usage is shown while the container is running.";
            return;
        }

        var start = history[0].Time;
        double X(StatsSample s) => (s.Time - start).TotalSeconds;

        Replace(_cpu, history.Select(s => new ObservablePoint(X(s), s.CpuPercent)));
        Replace(_memory, history.Select(s => new ObservablePoint(X(s), s.MemoryBytes)));

        // Network and disk counters are cumulative; chart the rate between samples.
        var rates = history.Zip(history.Skip(1), (a, b) => (Time: X(b), Seconds: Math.Max((b.Time - a.Time).TotalSeconds, 0.001), A: a, B: b)).ToList();
        Replace(_netRx, rates.Select(r => new ObservablePoint(r.Time, Rate(r.A.NetworkRxBytes, r.B.NetworkRxBytes, r.Seconds))));
        Replace(_netTx, rates.Select(r => new ObservablePoint(r.Time, Rate(r.A.NetworkTxBytes, r.B.NetworkTxBytes, r.Seconds))));
        Replace(_diskRead, rates.Select(r => new ObservablePoint(r.Time, Rate(r.A.BlockReadBytes, r.B.BlockReadBytes, r.Seconds))));
        Replace(_diskWrite, rates.Select(r => new ObservablePoint(r.Time, Rate(r.A.BlockWriteBytes, r.B.BlockWriteBytes, r.Seconds))));

        var last = history[^1];
        _summary.Text = string.Create(CultureInfo.InvariantCulture,
            $"CPU {last.CpuPercent:0.0}%   ·   Memory {Formatting.Bytes(last.MemoryBytes)} of {Formatting.Bytes(last.MemoryLimitBytes)}   ·   " +
            $"Network {Formatting.Bytes(last.NetworkRxBytes)} in / {Formatting.Bytes(last.NetworkTxBytes)} out   ·   " +
            $"Disk {Formatting.Bytes(last.BlockReadBytes)} read / {Formatting.Bytes(last.BlockWriteBytes)} written   ·   {last.Pids} processes");
    }

    private static double Rate(long before, long after, double seconds) => Math.Max(after - before, 0) / seconds;

    private static void Replace(ObservableCollection<ObservablePoint> target, IEnumerable<ObservablePoint> points)
    {
        target.Clear();
        foreach (var point in points)
        {
            target.Add(point);
        }
    }

    private static LineSeries<ObservablePoint> Line(ObservableCollection<ObservablePoint> points, string name) => new(points)
    {
        Name = name,
        GeometrySize = 0,
        LineSmoothness = 0,
        AnimationsSpeed = TimeSpan.Zero,
    };

    private static DockPanel Chart(string title, Func<double, string> labeler, params LineSeries<ObservablePoint>[] series) =>
        new DockPanel().Children(
            new TextBlock().Text(title).SemiBold().DockTop().Margin(4, 0, 0, 4),
            new CartesianChart
            {
                Series = series,
                LegendPosition = series.Length > 1 ? LegendPosition.Bottom : LegendPosition.Hidden,
                XAxes = [new Axis { Labeler = seconds => string.Create(CultureInfo.InvariantCulture, $"{seconds:0}s"), MinStep = 10 }],
                YAxes = [new Axis { MinLimit = 0, Labeler = labeler }],
                AnimationsSpeed = TimeSpan.Zero,
            });
}
