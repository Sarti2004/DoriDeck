using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using Serilog;

namespace DoriDeck.MidiTrigger;

internal sealed class MidiInputListener : IDisposable
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

	private readonly ILogger _logger;
	private readonly object _sync = new();
	private readonly Dictionary<string, InputDevice> _openDevices = new(StringComparer.Ordinal);
	// Devices that failed to open are logged once, not on every poll.
	private readonly HashSet<string> _failedDevices = new(StringComparer.Ordinal);
	private readonly Timer _pollTimer;

	private IReadOnlySet<string> _wantedDevices = new HashSet<string>(StringComparer.Ordinal);
	private bool _enabled = true;
	private bool _disposed;

	public MidiInputListener(ILogger logger)
	{
		_logger = logger.ForContext<MidiInputListener>();
		_pollTimer = new Timer(_ => Refresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}

	public event Action<string, int>? NotePressed;

	public static IReadOnlyList<string> GetDeviceNames()
	{
		try
		{
			var devices = InputDevice.GetAll();
			try
			{
				return [.. devices.Select(device => device.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
			}
			finally
			{
				foreach (var device in devices)
				{
					device.Dispose();
				}
			}
		}
		catch (Exception ex) when (ex is MidiDeviceException or DllNotFoundException or EntryPointNotFoundException)
		{
			return [];
		}
	}

	public void Start() => _pollTimer.Change(TimeSpan.Zero, PollInterval);

	/// if false every port is closed and OS releases it so another app can use it.
	public bool Enabled
	{
		get
		{
			lock (_sync)
			{
				return _enabled;
			}
		}
		set
		{
			lock (_sync)
			{
				_enabled = value;
			}

			Refresh();
		}
	}

	public void SetDevices(IReadOnlySet<string> deviceNames)
	{
		lock (_sync)
		{
			_wantedDevices = deviceNames;
			_failedDevices.IntersectWith(deviceNames);
		}

		Refresh();
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			// Switched off, or no binding uses MIDI: hold no ports and skip enumerating devices altogether.
			if (!_enabled || _wantedDevices.Count == 0)
			{
				foreach (var name in _openDevices.Keys.ToList())
				{
					Close(name);
				}

				return;
			}

			var connected = new HashSet<string>(GetDeviceNames(), StringComparer.Ordinal);

			foreach (var name in _openDevices.Keys.ToList())
			{
				if (!_wantedDevices.Contains(name) || !connected.Contains(name))
				{
					Close(name);
				}
			}

			foreach (var name in _wantedDevices)
			{
				if (connected.Contains(name) && !_openDevices.ContainsKey(name))
				{
					Open(name);
				}
				else if (!connected.Contains(name))
				{
					// Allow a fresh log line once the device is back.
					_failedDevices.Remove(name);
				}
			}
		}
	}

	private void Open(string name)
	{
		InputDevice? device = null;
		try
		{
			device = InputDevice.GetByName(name);
			device.EventReceived += (_, e) => OnEventReceived(name, e.Event);
			device.StartEventsListening();
			_openDevices[name] = device;
			_failedDevices.Remove(name);
			_logger.Information("Listening to MIDI input {Device}.", name);
		}
		catch (Exception ex)
		{
			device?.Dispose();
			if (_failedDevices.Add(name))
			{
				_logger.Warning(ex, "Could not open MIDI input {Device}", name);
			}
		}
	}

	private void Close(string name)
	{
		if (_openDevices.Remove(name, out var device))
		{
			try
			{
				device.StopEventsListening();
			}
			catch (MidiDeviceException)
			{
				// Already gone (unplugged); disposing below is all that is left to do.
			}

			device.Dispose();
			_logger.Information("Stopped listening to MIDI input {Device}.", name);
		}
	}

	private void OnEventReceived(string deviceName, MidiEvent midiEvent)
	{
		// A note-on with velocity 0 is a note-off by convention.
		if (midiEvent is NoteOnEvent noteOn && noteOn.Velocity > 0)
		{
			NotePressed?.Invoke(deviceName, noteOn.NoteNumber);
		}
	}

	public void Dispose()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_pollTimer.Dispose();
			foreach (var name in _openDevices.Keys.ToList())
			{
				Close(name);
			}
		}
	}
}
