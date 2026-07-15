# DoriDeck

An unofficial **Macro Deck 2** extension that provides an interface for running Dorico commands and scripts directly from Macro Deck.

This plugin is intended to make common Dorico workflows faster by allowing users to trigger recorded macros, execute Dorico API commands, and connect Macro Deck buttons to Dorico-related actions.

[![YouTube](http://i.ytimg.com/vi/PUcVK2MQWWE/maxresdefault.jpg)](https://www.youtube.com/watch?v=PUcVK2MQWWE)

## Project Note

I am not a power Dorico user. I use Dorico casually from time to time, mainly for small projects, especially choir-related work. This extension was created to make my own workflow faster and more comfortable, and it currently works very well for my needs.

That said, your workflow may be different. If you are missing a feature, have an improvement idea, or would like to suggest better icons or UI changes, feel free to contact me or open an issue. Contributions and suggestions are welcome.

## Why Macro Deck?

* Any grid size.
* Unlimited folders and profiles.
* Simple to set up and use, with no overwhelming interfaces
* It’s FREE!.

## Features

* Run recorded Dorico macros from Macro Deck.
* Execute Dorico commands via the Dorico.Net library.

## Planned Features

* **Advanced command configuration**
  Interface for assigning arguments to commands.

## Compatibility
Macro Deck 2.15.* required (2.15.0-b9)

> This plugin has been tested with Dorico Pro 5.1.81 on Windows 11 with Macro Deck 2.15.0-b9.

## Installation

### Install from the Macro Deck Extension Store

Not available yet.

1. Open Macro Deck on your PC.
2. Go to `Extensions`.
3. ~~Open the `Online` Extension Store tab.~~
4. ~~Search for `DoriDeck`.~~
5. Click `Install`.

### Manual Installation

1. Download the extension package. [Download](https://github.com/Sarti2004/DoriDeck/releases/download/V0.0.5-beta/Sarti2004.DoriDeck.macroDeckPlugin)
2. Open Macro Deck on your PC.
3. Go to `Extensions`.
4. Click `Install from file`.
5. Select the downloaded extension package.

You can also download the notation icon pack from [here](https://github.com/Sarti2004/MusicNotationIcons/releases/download/V0.0.3/Sarti2004.MusicNotation.macroDeckIconPack).

## Usage

After installing the plugin, open Macro Deck and add one of the available Dorico actions to a button.

### Available Actions

| Action          | Description                                                                                  | Configuration |
| --------------- | -------------------------------------------------------------------------------------------- | ------------- |
| **Run Script**  | Executes a recorded Dorico macro.                                                            | Script name   |
| **Run Command** | Executes a Dorico command through the API. Examples of available commands can be found [here](commands.md). | Command text  |
| **Command Sequence** | Executes a set of Dorico commands through the API. Examples of available commands can be found [here](commands.md). | Command text  |
| **Connect**     | Creates a connection between Macro Deck and Dorico.                                          | None          |

## Limitations
* Macro Deck currently works on Windows only; it is not available for macOS.
* Dorico must be running to execute actions successfully.
* Some actions may depend on your Dorico version.
* DoriDeck uses Dorico.Net, which depends on an unofficial Dorico API that can change, break, or be disabled at any moment.

## Disclaimer

This project is an independent third-party plugin and is not affiliated with, endorsed by, sponsored by, or otherwise associated with Steinberg Media Technologies GmbH.

Dorico is a trademark or registered trademark of Steinberg Media Technologies GmbH in the United States, Europe, and other countries. All product names, trademarks, and registered trademarks are the property of their respective owners.

Use this plugin at your own risk. The authors and contributors are not responsible for any loss of data, interruption of workflow, software malfunction, or other damages resulting from the use of this plugin. Users are encouraged to back up their Dorico projects before using automation tools or running custom commands.


## Third-Party Licenses

This plugin uses awesome 3rd party libraries:

* [Dorico.Net](https://github.com/scott-janssens/Dorico.Net) — MIT License

## Contributing

Contributions, bug reports, and feature requests are welcome.

When reporting an issue, please include:

* Macro Deck version
* Dorico version
* Windows version
* Steps to reproduce the issue
* Expected behavior
* Actual behavior
