namespace DoriDeck.Services;

internal static class AudiverisLocator
{
	public static string? FindExecutable()
	{
		var candidates = OperatingSystem.IsMacOS()
			? MacOSCandidates()
			: OperatingSystem.IsWindows()
				? WindowsCandidates()
				: [];

		return candidates.Concat(PathCandidates()).FirstOrDefault(File.Exists);
	}

	public static bool HasOcrLanguages()
	{
		string[] folders =
		[
			Environment.GetEnvironmentVariable("TESSDATA_PREFIX") ?? string.Empty,
			Path.Combine(AudiverisDataFolder(), "tessdata"),
		];

		return folders.Any(folder =>
			Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.traineddata").Any());
	}

	private static IEnumerable<string> MacOSCandidates()
	{
		const string launcher = "Audiveris.app/Contents/MacOS/Audiveris";

		yield return Path.Combine("/Applications", launcher);
		yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", launcher);
	}

	private static IEnumerable<string> WindowsCandidates()
	{
		Environment.SpecialFolder[] roots =
		[
			Environment.SpecialFolder.ProgramFiles,
			Environment.SpecialFolder.ProgramFilesX86,
			Environment.SpecialFolder.LocalApplicationData,
		];

		foreach (var root in roots)
		{
			var folder = Environment.GetFolderPath(root);
			if (folder.Length == 0)
			{
				continue;
			}

			yield return Path.Combine(folder, "Audiveris", "Audiveris.exe");
			yield return Path.Combine(folder, "Programs", "Audiveris", "Audiveris.exe");
		}
	}

	private static IEnumerable<string> PathCandidates()
	{
		var fileName = OperatingSystem.IsWindows() ? "Audiveris.exe" : "audiveris";
		var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

		return path
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(folder => Path.Combine(folder, fileName));
	}

	private static string AudiverisDataFolder() =>
		OperatingSystem.IsMacOS()
			? Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				"Library", "Application Support", "AudiverisLtd", "audiveris")
			: Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"AudiverisLtd", "audiveris");
}
