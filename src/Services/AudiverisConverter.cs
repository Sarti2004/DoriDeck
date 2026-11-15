using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Notifications;
using Serilog;

namespace DoriDeck.Services;


internal sealed record AudiverisOptions(bool KeepOmr, bool SeparateParts, IReadOnlyList<string> Switches, string? OcrLanguages);

internal sealed record ConversionProgress(int Current, int Total);

internal sealed class AudiverisConverter(string executablePath, ILogger logger) : IDisposable
{
	private static readonly TimeSpan FileTimeout = TimeSpan.FromMinutes(15);

	private const string AsciiStem = "score";

	private const string PdfResolutionConstant = "org.audiveris.omr.image.ImageLoading.pdfResolution";
	private const int DefaultPdfResolution = 300;

	private const string OcrLanguagesConstant = "org.audiveris.omr.text.Language.defaultSpecification";

	private const string SeparatePartsConstant = "org.audiveris.omr.sheet.grid.BarsRetriever.forceSeparateParts";

	private static readonly Regex TooLargeImage = new(@"Too large image: (.+?) pixels \(vs (.+?) max\)");

	private static readonly EnumerationOptions TopLevelIgnoreCase = new()
	{
		MatchCasing = MatchCasing.CaseInsensitive,
		RecurseSubdirectories = false,
	};

	private static readonly Lazy<LocalizationResolver> Resolver = new(() =>
	{
		var catalogs = new LocalizationCatalogRegistry();
		catalogs.Register(Strings.LocalizationCatalog);
		return new LocalizationResolver(catalogs);
	});

	private readonly ILogger _logger = logger.ForContext<AudiverisConverter>();
	private readonly CancellationTokenSource _shutdown = new();
	private IIntegrationContext? _context;
	private int _running;
	private volatile ConversionProgress? _progress;

	public void Attach(IIntegrationContext context) => _context = context;


	public string Status => _progress switch
	{
		null => string.Empty,
		{ Total: > 1 } progress => Text(Strings.Audiveris.States.WorkingCount(
			progress.Current.ToString(CultureInfo.InvariantCulture),
			progress.Total.ToString(CultureInfo.InvariantCulture))),
		_ => Text(Strings.Audiveris.States.Working()),
	};

	public static IReadOnlyList<string> FindPendingPdfs(string folder)
	{
		var exports = Directory.GetFiles(folder, "*.mxl", TopLevelIgnoreCase);

		return Directory.GetFiles(folder, "*.pdf", TopLevelIgnoreCase)
			.Where(pdf =>
			{
				var pdfTime = File.GetLastWriteTimeUtc(pdf);
				return !ExportsOf(pdf, exports).Any(export => File.GetLastWriteTimeUtc(export) >= pdfTime);
			})
			.OrderBy(pdf => pdf, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public bool TryStart(IReadOnlyList<string> pdfs, string folder, AudiverisOptions options)
	{
		if (Interlocked.Exchange(ref _running, 1) == 1)
		{
			return false;
		}

		_progress = new ConversionProgress(1, pdfs.Count);

		_ = Task.Run(async () =>
		{
			try
			{
				await ConvertAllAsync(pdfs, folder, options, _shutdown.Token);
			}
			catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
			{
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "PDF to MusicXML conversion failed.");
			}
			finally
			{
				_progress = null;
				Volatile.Write(ref _running, 0);
			}
		});

		return true;
	}

	public Task NotifyAsync(LocalizedText title, LocalizedText message, NotificationSeverity severity) =>
		NotifyAsync(title, message, severity, CancellationToken.None);

	public void Dispose() => _shutdown.Cancel();

	private async Task ConvertAllAsync(IReadOnlyList<string> pdfs, string folder, AudiverisOptions options, CancellationToken cancellationToken)
	{
		var total = pdfs.Count;
		var progress = await ShowProgressAsync(total, cancellationToken);
		var converted = 0;
		var notes = new List<string>();

		try
		{
			for (var index = 0; index < total; index++)
			{
				var pdf = pdfs[index];
				var name = Path.GetFileName(pdf);

				_progress = new ConversionProgress(index + 1, total);
				await UpdateProgressAsync(progress, index, total, name, cancellationToken);

				var (success, detail) = await ConvertAsync(pdf, folder, options, cancellationToken);
				if (success)
				{
					converted++;
				}
				else
				{
					_logger.Warning("Audiveris could not convert {Pdf}: {Reason}", pdf, detail);
				}

				if (detail is not null)
				{
					notes.Add($"{name}: {detail}");
				}
			}
		}
		finally
		{
			await DismissAsync(progress);
		}

		await NotifyAsync(
			Strings.Audiveris.Title(),
			Summary(converted, notes),
			notes.Count == 0 ? NotificationSeverity.Success : NotificationSeverity.Warning,
			cancellationToken);
	}

	private static string Summary(int converted, List<string> notes)
	{
		var lines = new List<string>
		{
			Text(Strings.Audiveris.Converted(converted.ToString(CultureInfo.InvariantCulture))),
		};

		lines.AddRange(notes);

		if (converted > 0 && !AudiverisLocator.HasOcrLanguages())
		{
			lines.Add(Text(Strings.Audiveris.NoOcrLanguages()));
		}

		return string.Join(Environment.NewLine, lines);
	}

	/// Converts one PDF; Detail is why it failed, or which pages were skipped when it succeeded.
	private async Task<(bool Success, string? Detail)> ConvertAsync(
		string pdf,
		string folder,
		AudiverisOptions options,
		CancellationToken cancellationToken)
	{
		var workFolder = Directory.CreateTempSubdirectory("DoriDeck-Audiveris-");

		try
		{
			
			var pdfStem = Path.GetFileNameWithoutExtension(pdf);
			var input = pdf;
			if (!pdf.All(char.IsAscii))
			{
				input = Path.Combine(workFolder.FullName, AsciiStem + ".pdf");
				File.Copy(pdf, input);
			}

			var stem = Path.GetFileNameWithoutExtension(input);
			var book = Path.Combine(workFolder.FullName, stem + ".omr");
			string[] constants =
			[
				.. SwitchArguments(options.Switches),
				.. options.OcrLanguages is null ? [] : new[] { "-constant", $"{OcrLanguagesConstant}={options.OcrLanguages}" },
				.. options.SeparateParts ? new[] { "-constant", $"{SeparatePartsConstant}=true" } : [],
			];

			var (exitCode, output) = await RunAudiverisAsync(
				["-batch", "-export", .. constants, "-output", workFolder.FullName, "--", input],
				cancellationToken);

			
			if (exitCode is not null && ReducedPdfResolution(output) is { } resolution)
			{
				File.Delete(book);

				(exitCode, output) = await RunAudiverisAsync(
					["-batch", "-export", .. constants, "-constant", $"{PdfResolutionConstant}={resolution}",
						"-output", workFolder.FullName, "--", input],
					cancellationToken);
			}

			string? skippedPages = null;
			var exports = Directory.GetFiles(workFolder.FullName, "*.mxl");

			// Audiveris refuses to export a book in which any sheet failed, e.g. the title page of a
			// Dorico PDF. The book it saved still holds the recognized sheets, so export just those.
			var (validSheets, invalidSheets) = SheetsOf(stem, output);
			if (exports.Length == 0 && exitCode is not null && invalidSheets.Count > 0 && validSheets.Count > 0 && File.Exists(book))
			{
				(exitCode, var retryOutput) = await RunAudiverisAsync(
					["-batch", "-export", "-sheets", .. validSheets, "-output", workFolder.FullName, "--", book],
					cancellationToken);

				output += retryOutput;
				exports = Directory.GetFiles(workFolder.FullName, "*.mxl");
				skippedPages = Text(Strings.Audiveris.SkippedPages(string.Join(", ", invalidSheets)));
			}

			if (exports.Length == 0)
			{
				return (false, exitCode is null ? Text(Strings.Audiveris.TimedOut()) : FailureReason(output, exitCode.Value));
			}

			foreach (var stale in ExportsOf(pdf, Directory.GetFiles(folder, "*.mxl", TopLevelIgnoreCase)))
			{
				File.Delete(stale);
			}

			string Restored(string file) => pdfStem + Path.GetFileName(file)[stem.Length..];

			foreach (var export in exports)
			{
				File.Move(export, Path.Combine(folder, Restored(export)), overwrite: true);
			}

			if (options.KeepOmr && File.Exists(book))
			{
				File.Move(book, Path.Combine(folder, Restored(book)), overwrite: true);
			}

			return (true, skippedPages);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return (false, ex.Message);
		}
		finally
		{
			try
			{
				workFolder.Delete(recursive: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_logger.Debug(ex, "Could not delete {Folder}.", workFolder.FullName);
			}
		}
	}

	private async Task<(int? ExitCode, string Output)> RunAudiverisAsync(
		IEnumerable<string> arguments,
		CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo(executablePath, arguments)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		startInfo.Environment["JAVA_TOOL_OPTIONS"] = "-Djava.awt.headless=true";

		using var process = Process.Start(startInfo)
			?? throw new IOException($"Could not start {executablePath}.");

		var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
		var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(FileTimeout);

		int? exitCode;
		try
		{
			await process.WaitForExitAsync(timeout.Token);
			exitCode = process.ExitCode;
		}
		catch (OperationCanceledException)
		{
			process.Kill(entireProcessTree: true);
			cancellationToken.ThrowIfCancellationRequested();
			exitCode = null;
		}

		return (exitCode, await standardOutput + await standardError);
	}

	private static string FailureReason(string output, int exitCode)
	{
		var warning = output
			.Split('\n', StringSplitOptions.TrimEntries)
			.Where(line => line.StartsWith("WARN", StringComparison.Ordinal))
			.Where(line => !line.Contains("OCR languages", StringComparison.Ordinal))
			.Select(line => line[(line.IndexOf("| ", StringComparison.Ordinal) + 1)..].Trim())
			.FirstOrDefault(line => line.Length > 0);

		if (warning is null)
		{
			return Text(Strings.Audiveris.ExitCode(exitCode.ToString(CultureInfo.InvariantCulture)));
		}

		var exceptionMessage = warning.LastIndexOf("Exception: ", StringComparison.Ordinal);
		return exceptionMessage >= 0 ? warning[(exceptionMessage + "Exception: ".Length)..] : warning;
	}

	/// <summary>
	/// Sheet (page) numbers from the log, which tags each sheet's lines "[Name#2]". A sheet fails with
	/// "Sheet Name#1 flagged as invalid." or "Error processing stub ...".
	/// </summary>
	private static (List<string> Valid, List<string> Invalid) SheetsOf(string stem, string output)
	{
		var sheetTag = new Regex(@"\[" + Regex.Escape(stem) + @"#(\d+)\]");
		var all = new SortedSet<int>();
		var invalid = new SortedSet<int>();

		foreach (var line in output.Split('\n'))
		{
			var match = sheetTag.Match(line);
			if (!match.Success)
			{
				continue;
			}

			var sheet = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
			all.Add(sheet);

			if (line.Contains("flagged as invalid", StringComparison.Ordinal) ||
				line.Contains("Error processing stub", StringComparison.Ordinal))
			{
				invalid.Add(sheet);
			}
		}

		return (
			[.. all.Except(invalid).Select(sheet => sheet.ToString(CultureInfo.InvariantCulture))],
			[.. invalid.Select(sheet => sheet.ToString(CultureInfo.InvariantCulture))]);
	}

	private static int? ReducedPdfResolution(string output)
	{
		double? fit = null;
		foreach (Match match in TooLargeImage.Matches(output))
		{
			var pixels = PixelCount(match.Groups[1].Value);
			var max = PixelCount(match.Groups[2].Value);
			if (pixels > 0 && max > 0)
			{
				fit = Math.Min(fit ?? double.MaxValue, max / pixels);
			}
		}

		// The pixel count grows with the square of the resolution; one DPI less absorbs rounding.
		return fit is null ? null : Math.Max(1, (int)(DefaultPdfResolution * Math.Sqrt(fit.Value)) - 1);
	}

	private static double PixelCount(string text) =>
		double.TryParse(new string([.. text.Where(char.IsAsciiDigit)]), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
			? count
			: 0;

	private static IEnumerable<string> SwitchArguments(IEnumerable<string> switches) =>
		switches.SelectMany(name => new[] { "-constant", $"org.audiveris.omr.sheet.ProcessingSwitches.{name}=true" });

	private static string Text(LocalizedText text) =>
		Resolver.Value.Resolve(text, Strings.LocalizationCatalog.DefaultCulture) ?? string.Empty;

	private static IEnumerable<string> ExportsOf(string pdf, IEnumerable<string> exports)
	{
		var stem = Path.GetFileNameWithoutExtension(pdf);

		return exports.Where(export =>
		{
			var name = Path.GetFileNameWithoutExtension(export);
			return name.Equals(stem, StringComparison.OrdinalIgnoreCase) ||
				name.StartsWith(stem + ".mvt", StringComparison.OrdinalIgnoreCase);
		});
	}

	private async Task<INotificationHandle?> ShowProgressAsync(int total, CancellationToken cancellationToken)
	{
		if (_context is null)
		{
			return null;
		}

		try
		{
			return await _context.NotificationService.ShowAsync(
				new NotificationOptions
				{
					Title = Strings.Audiveris.Title(),
					Message = Strings.Audiveris.Starting(),
					Progress = new NotificationProgress(0, total),
					Dismissible = false,
				},
				cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Debug(ex, "Progress notifications are unavailable.");
			return null;
		}
	}

	private async Task UpdateProgressAsync(
		INotificationHandle? progress,
		int processed,
		int total,
		string fileName,
		CancellationToken cancellationToken)
	{
		if (progress is null)
		{
			return;
		}

		try
		{
			await progress.UpdateAsync(
				new NotificationUpdate
				{
					Message = Strings.Audiveris.Converting(
						fileName,
						(processed + 1).ToString(CultureInfo.InvariantCulture),
						total.ToString(CultureInfo.InvariantCulture)),
					Progress = new NotificationProgress(processed, total),
				},
				cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Debug(ex, "Could not update the conversion progress.");
		}
	}

	private async Task DismissAsync(INotificationHandle? progress)
	{
		if (progress is null)
		{
			return;
		}

		try
		{
			await progress.DismissAsync();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not dismiss the conversion progress.");
		}
	}

	private async Task NotifyAsync(
		LocalizedText title,
		LocalizedText message,
		NotificationSeverity severity,
		CancellationToken cancellationToken)
	{
		if (_context is null)
		{
			return;
		}

		try
		{
			await _context.NotificationService.ShowAsync(
				new NotificationOptions { Title = title, Message = message, Severity = severity, Inbox = true },
				cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_context.Notifications.Notify(new UserNotificationRequest
			{
				Title = Text(title),
				Message = Text(message),
				Level = severity switch
				{
					NotificationSeverity.Error => UserNotificationLevel.Error,
					NotificationSeverity.Warning => UserNotificationLevel.Warning,
					_ => UserNotificationLevel.Info,
				},
			});
		}
	}
}
