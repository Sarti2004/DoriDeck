using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface.Commands;
using ScoreInterface.Requests;

namespace DoriDeck.Actions;

/// Creates a rehearsal mark, need to set font to Finale first
internal sealed class SectionHeaderAction(DoricoSession session) : DoriDeckActionBase(session), IActionDefinition
{
	private const string TextParameter = "text";
	private const string NewLineParameter = "newLine";

	public string Id => "section-header";

	public LocalizedText Name => Strings.Actions.SectionHeader.Name();

	public LocalizedText Description => Strings.Actions.SectionHeader.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(
			TextParameter,
			label: Strings.Actions.SectionHeader.Text.Label(),
			description: Strings.Actions.SectionHeader.Text.Description(),
			placeholder: Strings.Actions.SectionHeader.Text.Placeholder(),
			required: true),
		ActionParameter.Toggle(
			NewLineParameter,
			label: Strings.Actions.SectionHeader.NewLine.Label(),
			description: Strings.Actions.SectionHeader.NewLine.Description(),
			defaultValue: true),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session);

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var text = context.Parameters.TryGetValue(TextParameter, out var textValue) ? textValue?.ToString() : null;
			if (string.IsNullOrWhiteSpace(text))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required(Strings.Actions.SectionHeader.Text.Label()));
			}

			var newLine = !context.Parameters.TryGetValue(NewLineParameter, out var newLineValue) ||
				newLineValue is not bool newLineBool || newLineBool;

			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			var token = context.CancellationToken;
			var newLineToken = newLine ? @"\r\n" : " ";

			var enclosureType = (await dorico.GetEngravingOptionsAsync(token))?
				.FirstOrDefault(option => option.Path == "rehearsalMarkOptions.enclosureType")
				?.CurrentValue;

			if (enclosureType != "kNone")
			{
				await dorico.SetEngravingOptionsAsync(
					[
						new OptionValue("rehearsalMarkOptions.enclosureType", "kNone"),
						new OptionValue("rehearsalMarkOptions.noteRelativeHorizontalAlignment", "kLeft"),
						new OptionValue("rehearsalMarkOptions.minimumDistanceFromStave", "5"),
					],
					token);
				await Task.Delay(20, token);
			}

			await dorico.SendRequestAsync(new Command("NoteInput.CreateRehearsalMark"), token);
			await Task.Delay(20, token);

			// Dorico's rehearsal mark popover only picks up the prefix property change once the field
			// has already been touched once; the first send primes it and the second one is the one
			// that sticks.
			await dorico.SendRequestAsync(new Command("UI.InvokePropertyChangeValue?Type=kRehearsalMarkCustomPrefix&Value="), token);
			await Task.Delay(20, token);
			await dorico.SendRequestAsync(new Command("UI.InvokePropertyChangeValue?Type=kRehearsalMarkCustomPrefix&Value="), token);
			await Task.Delay(10, token);

			await dorico.SendRequestAsync(new Command($"UI.InvokePropertyChangeValue?Type=kRehearsalMarkCustomSuffix&Value={newLineToken}{text}"), token);

			return ActionResult.Success();
		}
	}
}
