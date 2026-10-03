using System.Globalization;
using System.Text;

using WslcDesktop.Engine;

namespace WslcDesktop.Core;

/// <summary>
/// A container's log stream for display. Lines are collected in batches and handed to the view as text through
/// <see cref="TextReset"/> and <see cref="TextAppended"/>, so the view never re-renders per line.
/// </summary>
public sealed class LogsViewModel(ILogSource logs, string containerId, TimeProvider? timeProvider = null) : ObservableObject, IDisposable
{
    /// <summary>Lines kept in memory; older lines are dropped.</summary>
    public const int MaxLines = 5000;

    /// <summary>How many past lines to load when opening.</summary>
    public const int InitialTail = 1000;

    /// <summary>
    /// The history arrives as all stdout then all stderr (S1 §2.10), so the first lines are held this long and
    /// sorted by timestamp before being shown.
    /// </summary>
    public static readonly TimeSpan InitialBatchDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Live lines are batched for this long, which keeps the view responsive under heavy output.</summary>
    public static readonly TimeSpan LiveBatchDelay = TimeSpan.FromMilliseconds(100);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly List<LogLine> _lines = [];
    private readonly List<LogLine> _pending = [];
    private CancellationTokenSource? _stream;
    private bool _flushScheduled;
    private bool _initialBatch;
    private bool _showTimestamps;
    private bool _isPaused;
    private string _filter = string.Empty;
    private bool _isStreaming;
    private string? _statusText;

    /// <summary>The whole visible text was replaced (filter, timestamps, trimming, restart).</summary>
    public event Action<string>? TextReset;

    /// <summary>Text to append at the end of the view.</summary>
    public event Action<string>? TextAppended;

    public bool ShowTimestamps
    {
        get => _showTimestamps;
        set
        {
            if (SetProperty(ref _showTimestamps, value))
            {
                Rerender();
            }
        }
    }

    /// <summary>Only show lines containing this text (case-insensitive).</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? string.Empty))
            {
                Rerender();
            }
        }
    }

    /// <summary>While paused, new lines are collected but the view isn't updated (so text can be selected).</summary>
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (SetProperty(ref _isPaused, value) && !value)
            {
                Rerender();
            }
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        private set => SetProperty(ref _isStreaming, value);
    }

    /// <summary>A note shown above the log ("Container stopped", errors), or null.</summary>
    public string? StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public int LineCount => _lines.Count;

    /// <summary>(Re)starts following the logs from the last <see cref="InitialTail"/> lines.</summary>
    public void Start()
    {
        Stop();
        _lines.Clear();
        _pending.Clear();
        _initialBatch = true;
        StatusText = null;
        Rerender();

        _stream = new CancellationTokenSource();
        _ = ReadAsync(_stream);
    }

    public void Stop()
    {
        if (_stream is { } stream)
        {
            _stream = null;
            stream.Cancel();
            stream.Dispose();
        }

        IsStreaming = false;
    }

    public void Dispose() => Stop();

    /// <summary>All lines currently kept, as shown (for copy / save).</summary>
    public string GetText() => Render(_lines);

    private async Task ReadAsync(CancellationTokenSource stream)
    {
        IsStreaming = true;
        try
        {
            await foreach (var line in logs.ReadAsync(containerId, new LogOptions(Follow: true, Tail: InitialTail, Timestamps: true), stream.Token))
            {
                _pending.Add(line);
                if (!_flushScheduled)
                {
                    _flushScheduled = true;
                    _ = FlushLaterAsync(stream, _initialBatch ? InitialBatchDelay : LiveBatchDelay);
                }
            }

            // "logs -f" ends by itself when the container stops.
            Flush();
            StatusText = "The container stopped. Logs will continue if it starts again.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (EngineException ex)
        {
            Flush();
            StatusText = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_stream, stream) || _stream is null)
            {
                IsStreaming = false;
            }
        }
    }

    private async Task FlushLaterAsync(CancellationTokenSource stream, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _time, stream.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        Flush();
    }

    private void Flush()
    {
        _flushScheduled = false;
        _initialBatch = false;
        if (_pending.Count == 0)
        {
            return;
        }

        // Within a batch, order by timestamp: stdout and stderr arrive on separate pipes.
        var batch = _pending.OrderBy(line => line.Timestamp ?? DateTimeOffset.MinValue).ToList();
        _pending.Clear();
        _lines.AddRange(batch);
        OnPropertyChanged(nameof(LineCount));

        if (_lines.Count > MaxLines)
        {
            // Drop 10% at once so trimming (a full re-render) is rare.
            _lines.RemoveRange(0, _lines.Count - (MaxLines * 9 / 10));
            Rerender();
        }
        else if (!_isPaused)
        {
            var text = Render(batch);
            if (text.Length > 0)
            {
                TextAppended?.Invoke(text);
            }
        }
    }

    private void Rerender()
    {
        if (!_isPaused)
        {
            TextReset?.Invoke(Render(_lines));
        }
    }

    private string Render(IEnumerable<LogLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var text = AnsiText.Strip(line.Text);
            if (_filter.Length > 0 && !text.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_showTimestamps && line.Timestamp is { } timestamp)
            {
                builder.Append(timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("  ");
            }

            builder.Append(text).Append('\n');
        }

        return builder.ToString();
    }
}
