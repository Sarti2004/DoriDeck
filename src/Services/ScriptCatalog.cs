using System.Text;
using Serilog;

namespace DoriDeck.Services;

/// <summary>
/// Finds the Lua scripts under a folder so <see cref="Actions.DynamicScriptAction"/> instances can be
/// generated for them, and turns a script's relative path into a stable, unique action id.
/// </summary>
internal static class ScriptCatalog
{
	/// <summary>
	/// Lists every <c>.lua</c> script under <paramref name="scriptPath"/>, recursively, as the same
	/// slash-separated relative names the Run Script action's own "Script name" field expects.
	/// </summary>
	public static IReadOnlyList<string> DiscoverScriptNames(string scriptPath, ILogger logger)
	{
		if (!Directory.Exists(scriptPath))
		{
			logger.Warning("Script path does not exist: {ScriptPath}", scriptPath);
			return [];
		}

		try
		{
			return Directory.GetFiles(scriptPath, "*.lua", SearchOption.AllDirectories)
				.Select(file => ToScriptName(scriptPath, file))
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
		{
			// A folder that fails to enumerate should not prevent the plugin from initializing; the
			// dynamic action list simply stays empty until the next scan.
			logger.Error(ex, "Failed to scan the script folder {ScriptPath}.", scriptPath);
			return [];
		}
	}

	/// <summary>
	/// Builds a local action id from a script name, disambiguating it against ids already handed out in
	/// this scan. Ids are persisted with whatever button the user assigns the action to, so the same
	/// script name keeps producing the same id across scans as long as no other script collides with it.
	/// </summary>
	public static string MakeUniqueActionId(string scriptName, HashSet<string> idsUsedThisScan)
	{
		var slug = Slugify(scriptName);
		var candidate = $"run-script-{slug}";

		for (var suffix = 2; !idsUsedThisScan.Add(candidate); suffix++)
		{
			candidate = $"run-script-{slug}-{suffix}";
		}

		return candidate;
	}

	private static string Slugify(string value)
	{
		var builder = new StringBuilder(value.Length);
		var lastWasDash = true;

		foreach (var c in value)
		{
			if (char.IsAsciiLetterOrDigit(c))
			{
				builder.Append(char.ToLowerInvariant(c));
				lastWasDash = false;
			}
			else if (!lastWasDash)
			{
				builder.Append('-');
				lastWasDash = true;
			}
		}

		while (builder.Length > 0 && builder[^1] == '-')
		{
			builder.Length--;
		}

		if (builder.Length == 0 || !char.IsAsciiLetter(builder[0]))
		{
			builder.Insert(0, "script-");
		}

		return builder.ToString();
	}

	private static string ToScriptName(string scriptPath, string file)
	{
		var relativePath = Path.GetRelativePath(scriptPath, file);

		return Path.ChangeExtension(relativePath, null)
			.Replace(Path.DirectorySeparatorChar, '/')
			.Replace(Path.AltDirectorySeparatorChar, '/');
	}
}
