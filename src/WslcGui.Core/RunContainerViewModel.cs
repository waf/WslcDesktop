using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

using WslcGui.Engine;

namespace WslcGui.Core;

public sealed class EnvironmentEntry : ObservableObject
{
    private string _key = string.Empty;
    private string _value = string.Empty;

    public string Key { get => _key; set => SetProperty(ref _key, value ?? string.Empty); }
    public string Value { get => _value; set => SetProperty(ref _value, value ?? string.Empty); }
}

public sealed class PortEntry : ObservableObject
{
    private string _hostPort = string.Empty;
    private string _containerPort = string.Empty;
    private bool _udp;

    public string HostPort { get => _hostPort; set => SetProperty(ref _hostPort, value ?? string.Empty); }
    public string ContainerPort { get => _containerPort; set => SetProperty(ref _containerPort, value ?? string.Empty); }
    public bool Udp { get => _udp; set => SetProperty(ref _udp, value); }
}

public sealed class MountEntry : ObservableObject
{
    private string _source = string.Empty;
    private string _target = string.Empty;
    private bool _readOnly;

    /// <summary>A Windows folder (bind mount) or a volume name.</summary>
    public string Source { get => _source; set => SetProperty(ref _source, value ?? string.Empty); }
    public string Target { get => _target; set => SetProperty(ref _target, value ?? string.Empty); }
    public bool ReadOnly { get => _readOnly; set => SetProperty(ref _readOnly, value); }
}

/// <summary>The "Run a new container" form.</summary>
public sealed partial class RunContainerViewModel : ObservableObject
{
    private readonly IContainerLifecycle _lifecycle;
    private readonly Func<RunSpec, string>? _describe;
    private string _image;
    private string _name = string.Empty;
    private string _command = string.Empty;
    private bool _autoRemove;
    private bool _isBusy;
    private string? _error;
    private string _commandPreview = string.Empty;

    /// <param name="describe">Renders the equivalent command line for a spec (the engine's "show command"), if available.</param>
    public RunContainerViewModel(IContainerLifecycle lifecycle, string image = "", Func<RunSpec, string>? describe = null)
    {
        _lifecycle = lifecycle;
        _describe = describe;
        _image = image;
        WatchEntries(Environment);
        WatchEntries(Ports);
        WatchEntries(Mounts);
        UpdatePreview();
    }

    public string Image { get => _image; set => SetAndPreview(ref _image, value); }
    public string Name { get => _name; set => SetAndPreview(ref _name, value); }

    /// <summary>Command and arguments, shell-quoted. Empty runs the image's default command.</summary>
    public string Command { get => _command; set => SetAndPreview(ref _command, value); }

    public bool AutoRemove
    {
        get => _autoRemove;
        set
        {
            if (SetProperty(ref _autoRemove, value))
            {
                UpdatePreview();
            }
        }
    }

    public ObservableCollection<EnvironmentEntry> Environment { get; } = [];
    public ObservableCollection<PortEntry> Ports { get; } = [];
    public ObservableCollection<MountEntry> Mounts { get; } = [];

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    /// <summary>Validation or run error to show in the form.</summary>
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }

    /// <summary>The equivalent command line, or a hint about what's missing.</summary>
    public string CommandPreview { get => _commandPreview; private set => SetProperty(ref _commandPreview, value); }

    /// <summary>Builds the spec, or returns null and lists the problems.</summary>
    public RunSpec? TryBuildSpec(out IReadOnlyList<string> errors)
    {
        var problems = new List<string>();
        var image = _image.Trim();
        if (image.Length == 0)
        {
            problems.Add("Choose an image.");
        }

        var name = _name.Trim();
        if (name.Length > 0 && !ContainerNameRegex().IsMatch(name))
        {
            problems.Add("The name may only contain letters, digits, '_', '.' and '-', and must start with a letter or digit.");
        }

        var command = CommandLineSplitter.TrySplit(_command, out var commandError);
        if (commandError is not null)
        {
            problems.Add(commandError);
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Environment.Where(e => e.Key.Length > 0 || e.Value.Length > 0))
        {
            var key = entry.Key.Trim();
            if (key.Length == 0)
            {
                problems.Add("An environment variable has a value but no name.");
            }
            else if (key.Contains('=', StringComparison.Ordinal))
            {
                problems.Add($"Environment variable name '{key}' can't contain '='.");
            }
            else if (!environment.TryAdd(key, entry.Value))
            {
                problems.Add($"Environment variable '{key}' is set twice.");
            }
        }

        var ports = new List<PortMapping>();
        foreach (var entry in Ports.Where(p => p.HostPort.Length > 0 || p.ContainerPort.Length > 0))
        {
            var containerPort = ParsePort(entry.ContainerPort);
            int? hostPort = entry.HostPort.Trim().Length == 0 ? null : ParsePort(entry.HostPort);
            if (containerPort is null || (entry.HostPort.Trim().Length > 0 && hostPort is null))
            {
                problems.Add($"Port mapping '{entry.HostPort}:{entry.ContainerPort}' needs ports between 1 and 65535.");
                continue;
            }

            ports.Add(new PortMapping(containerPort.Value, hostPort, null, entry.Udp ? PortProtocol.Udp : PortProtocol.Tcp));
        }

        var mounts = new List<VolumeMount>();
        foreach (var entry in Mounts.Where(m => m.Source.Length > 0 || m.Target.Length > 0))
        {
            var source = entry.Source.Trim();
            var target = entry.Target.Trim();
            if (source.Length == 0 || !target.StartsWith('/'))
            {
                problems.Add($"Mount '{source}' needs a source and an absolute container path (starting with '/').");
                continue;
            }

            mounts.Add(new VolumeMount(source, target, entry.ReadOnly));
        }

        errors = problems;
        if (problems.Count > 0)
        {
            return null;
        }

        return new RunSpec(image)
        {
            Name = name.Length > 0 ? name : null,
            Command = command ?? [],
            Environment = environment,
            Ports = ports,
            Mounts = mounts,
            AutoRemove = _autoRemove,
        };
    }

    /// <summary>Validates and runs. Returns the new container ID, or null (with <see cref="Error"/> set) on failure.</summary>
    public async Task<string?> RunAsync(CancellationToken cancellationToken = default)
    {
        var spec = TryBuildSpec(out var errors);
        if (spec is null)
        {
            Error = string.Join("\n", errors);
            return null;
        }

        IsBusy = true;
        Error = null;
        try
        {
            return await _lifecycle.RunAsync(spec, cancellationToken);
        }
        catch (EngineException ex)
        {
            Error = ex.Message;
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static int? ParsePort(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535 ? port : null;

    private void SetAndPreview(ref string field, string? value)
    {
        if (SetProperty(ref field, value ?? string.Empty))
        {
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        var spec = TryBuildSpec(out var errors);
        CommandPreview = spec is null ? errors[0] : _describe?.Invoke(spec) ?? string.Empty;
    }

    private void WatchEntries<T>(ObservableCollection<T> entries)
        where T : INotifyPropertyChanged
    {
        void OnEntryChanged(object? sender, PropertyChangedEventArgs e) => UpdatePreview();

        entries.CollectionChanged += (_, e) =>
        {
            foreach (var item in e.NewItems?.OfType<T>() ?? [])
            {
                item.PropertyChanged += OnEntryChanged;
            }

            foreach (var item in e.OldItems?.OfType<T>() ?? [])
            {
                item.PropertyChanged -= OnEntryChanged;
            }

            if (e.Action != NotifyCollectionChangedAction.Move)
            {
                UpdatePreview();
            }
        };
    }

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]*$")]
    private static partial Regex ContainerNameRegex();
}
