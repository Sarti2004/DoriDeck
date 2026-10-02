using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using DoriDeck;
using DoriDeck.Services;
using Lea;
using DoriDeck.PianoFolder.Notes;
using Microsoft.Extensions.DependencyInjection;
using ScoreInterface;
using ScoreInterface.Comms;


var builder = MacroDeckPlugin.CreatePlugin(args);

builder.Services
	.AddSingleton<IEventAggregator, EventAggregator>()
	.AddSingleton<IClientWebSocketWrapper, ClientWebSocketWrapper>()
	.AddSingleton<IScoreInterfaceCommsContext, ScoreInterfaceCommsContext>()
	.AddSingleton<IScoreInterfaceRemote, ScoreInterfaceRemote>()
	.AddSingleton<IKeyboardService, KeyboardService>()
	.AddSingleton<IApplicationFocusService, ApplicationFocusService>()
	.AddSingleton<DoricoSession>()
	.AddSingleton<INoteOutput, DoricoNoteOutput>()
	.AddSingleton<INoteDispatcher, NoteDispatcher>();

var plugin = builder
	.UseMacroDeckLogging()
	.UseLocalization(Strings.LocalizationCatalog)
	.RegisterIntegration<PluginIntegration>()
	.RegisterIntegration<DoriDeck.PianoFolder.PluginIntegration>()
	.Build();

await plugin.RunAsync();
