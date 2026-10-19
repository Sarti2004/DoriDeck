using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DoriDeck.Services;
using DoriDeck.Services.MacOS;
using DoriDeck.Services.Windows;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface.Commands;
using Serilog;

namespace DoriDeck.Actions;

/// Opens Change Paragraph Style dialog, picks a style and confirms it
internal sealed class ChangeParagraphStyleAction(DoricoSession session, IKeyboardService keyboard, ILogger logger) :
	DoriDeckActionBase(session),
	IActionDefinition,
	IDynamicOptionsActionDefinition
{
	private const string StyleParameter = "style";
	private const int DialogOpenDelayMilliseconds = 100;
	private const int KeyDelayMilliseconds = 20;
	private const int UiAutomationAttempts = 10;

	private readonly ILogger _logger = logger.ForContext<ChangeParagraphStyleAction>();

	public string Id => "change-paragraph-style";

	public LocalizedText Name => Strings.Actions.ChangeParagraphStyle.Name();

	public LocalizedText Description => Strings.Actions.ChangeParagraphStyle.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(
			StyleParameter,
			label: Strings.Actions.ChangeParagraphStyle.Style.Label(),
			description: Strings.Actions.ChangeParagraphStyle.Style.Description(),
			placeholder: Strings.Actions.ChangeParagraphStyle.Style.Placeholder(),
			required: true),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOs;

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName != StyleParameter)
		{
			return new DynamicOptionsResult
			{
				Options = [],
			};
		}

		IReadOnlyList<string> styles = [];
		try
		{
			if (await GetConnectedRemoteAsync(Session, cancellationToken) != null)
			{
				styles = await Session.GetParagraphStylesAsync(cancellationToken).WaitAsync(HostCallWaitLimit, cancellationToken);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "Failed to load the Dorico paragraph styles.");
		}

		var filter = context.Filter?.Trim() ?? string.Empty;
		var options = styles
			.Where(style => string.IsNullOrEmpty(filter) || style.Contains(filter, StringComparison.OrdinalIgnoreCase))
			.Select(style => new ActionParameterOption
			{
				Value = style,
				Label = style,
			})
			.ToList();

		if (!string.IsNullOrEmpty(filter) &&
			!options.Any(option => string.Equals(option.Value, filter, StringComparison.Ordinal)))
		{
			options.Insert(0, new ActionParameterOption
			{
				Value = filter,
				Label = filter,
			});
		}

		return new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = true,
		};
	}

	public IActionExecutor CreateExecutor() => new Executor(Session, keyboard);

	private sealed class Executor(DoricoSession session, IKeyboardService keyboard) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var style = context.Parameters.TryGetValue(StyleParameter, out var value) ? value?.ToString() : null;
			if (string.IsNullOrWhiteSpace(style))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required(Strings.Actions.ChangeParagraphStyle.Style.Label()));
			}

			style = await session.ResolveVariablesAsync(style);

			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			var token = context.CancellationToken;

			// Keystrokes go to the focused window, so Dorico must be in front before the dialog opens.
			if (OperatingSystem.IsMacOS() && !NSRunningApplicationInterop.ActivateDorico())
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}
			else if (OperatingSystem.IsWindows())
			{
				WindowsApplicationInterop.ActivateDorico();
			}

			await dorico.SendRequestAsync(new Command("Edit.ChangeParagraphStyle"), token);
			await Task.Delay(DialogOpenDelayMilliseconds, token);

			await PressAsync(DoriDeckKey.Return, token);
			if (!(OperatingSystem.IsWindows() && session.DoricoVersion == "5" && await SelectWithUiAutomationAsync(style, token)))
			{
				keyboard.EnterText(style);
			}
			await Task.Delay(KeyDelayMilliseconds, token);
			await PressAsync(DoriDeckKey.Return, token);

			// Move focus from the style list to the OK button and press it.
			if (session.DoricoVersion == "5")
			{
				await PressAsync(DoriDeckKey.Tab, token);
			}
			else
			{
				await PressAsync(DoriDeckKey.Tab, token);
				await PressAsync(DoriDeckKey.Tab, token);
			}
			await PressAsync(DoriDeckKey.Return, token);

			return ActionResult.Success();
		}

		// The dropdown popup appears asynchronously after Return, so retry until its items are exposed.
		[SupportedOSPlatform("windows")]
		private static async Task<bool> SelectWithUiAutomationAsync(string style, CancellationToken cancellationToken)
		{
			for (var attempt = 0; attempt < UiAutomationAttempts; attempt++)
			{
				try
				{
					if (WindowsUiAutomation.SelectDropdownItem(style))
					{
						return true;
					}
				}
				catch (COMException)
				{
					// The popup can disappear or rebuild between lookups; try again.
				}

				await Task.Delay(KeyDelayMilliseconds, cancellationToken);
			}

			return false;
		}

		private async Task PressAsync(DoriDeckKey key, CancellationToken cancellationToken)
		{
			keyboard.Press(key);
			await Task.Delay(KeyDelayMilliseconds, cancellationToken);
		}
	}
}
