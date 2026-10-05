using DoriDeck.Services;
using DoriDeck.Services.MacOS;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface.Commands;
using ScoreInterface.Requests;
using Serilog;
using Serilog.Core;

namespace DoriDeck.Actions;

/// <summary>
/// Created a piano reduction of a choir score by creating a cue for each specified voice
/// </summary>
internal sealed class ChoirReductionAction(DoricoSession session, IKeyboardService keyboard)
	: DoriDeckActionBase(session), IActionDefinition
{
	private const string VoicesParameter = "voices";
	private const string DefaultVoices = "Soprano,Alto,Tenor,Bass";

	private static readonly char[] VoiceSeparators = [',', '\r', '\n'];
	private static readonly string[] DownStemPrefixes = ["alto", "bass"];
	private static readonly string[] UpperStaffPrefixes = ["soprano", "alto"];

	public string Id => "choir-reduction";

	public LocalizedText Name => Strings.Actions.ChoirReduction.Name();

	public LocalizedText Description => Strings.Actions.ChoirReduction.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(
			VoicesParameter,
			label: Strings.Actions.ChoirReduction.Voices.Label(),
			description: Strings.Actions.ChoirReduction.Voices.Description(),
			placeholder: DefaultVoices,
			required: false),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session, keyboard);

	private sealed class Executor(DoricoSession session, IKeyboardService keyboard) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var voicesText = context.Parameters.TryGetValue(VoicesParameter, out var value) ? value?.ToString() : null;
			var voices = (string.IsNullOrWhiteSpace(voicesText) ? DefaultVoices : voicesText)
				.Split(VoiceSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where(voice => voice.Length > 0)
				.ToArray();

			if (voices.Length == 0)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required(Strings.Actions.ChoirReduction.Voices.Label()));
			}

			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			var token = context.CancellationToken;

			if (OperatingSystem.IsMacOS() && !NSRunningApplicationInterop.ActivateDorico())
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			await dorico.SetLayoutOptionsAsync([new OptionValue("cueLayoutOptions.showCues", "false")], cancellationToken: token);
			await Task.Delay(300, token);
			await dorico.SetNotationOptionsAsync([new OptionValue("omitBarRestsBeneathCues", "false")], cancellationToken: token);
			await Task.Delay(300, token);

			await dorico.SetEngravingOptionsAsync(
				[
					new OptionValue("cueEngravingOptions.showLyrics", "false"),
					new OptionValue("cueEngravingOptions.defaultCueClefValue", "1"),
					new OptionValue("cueEngravingOptions.cueVisualScale", "1"),
				],
				token);
			await Task.Delay(300, token);

			for (var i = 0; i < voices.Length; i++)
			{
				var voice = voices[i];

				await dorico.SendRequestAsync(new Command($"NoteInput.CreateCue?Definition={voice}&UseLocalOverride=0"), token);
				await Task.Delay(250, token);

				var stemDirection = IsStemsDown(voice) ? "kForceStemsDown" : "kForceStemsUp";
				await dorico.SendRequestAsync(new Command($"UI.InvokePropertyChangeValue?Type=kCueVoiceDirection&Value={stemDirection}"), token);
				await Task.Delay(100, token);

				if (IsTenorVoice(voice))
				{
					await dorico.SendRequestAsync(new Command("UI.InvokePropertyChangeValue?Type=kCueOctaveShift&Value=-1"), token);
					await Task.Delay(100, token);
				}

				var isLast = i == voices.Length - 1;
				if (!isLast)
				{
					keyboard.PressChord(DoriDeckKey.Shift, DoriDeckKey.DownArrow);
					await Task.Delay(100, token);
					keyboard.Press(DoriDeckKey.DownArrow);
					await Task.Delay(100, token);

					if (i == 0 || IsUpperStaff(voices[i + 1]))
					{
						keyboard.Press(DoriDeckKey.UpArrow);
						await Task.Delay(100, token);
					}
				}
			}

			await dorico.SetLayoutOptionsAsync([new OptionValue("cueLayoutOptions.showCues", "true")], cancellationToken: token);
			await Task.Delay(300, token);
			await dorico.SetNotationOptionsAsync([new OptionValue("omitBarRestsBeneathCues", "true")], cancellationToken: token);
			await Task.Delay(300, token);

			return ActionResult.Success();
		}

		private static bool IsTenorVoice(string name) => name.StartsWith("tenor", StringComparison.OrdinalIgnoreCase);

		private static bool IsStemsDown(string name) =>
			DownStemPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

		private static bool IsUpperStaff(string name) =>
			UpperStaffPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
	}
}
