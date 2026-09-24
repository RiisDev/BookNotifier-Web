namespace BookNotifier_Web.Models
{
	public enum SettingsFieldType { Text, Password }

	public record PlatformFieldDef(
		string Key,
		string Label,
		string EnvKey,
		string Hint = "",
		string Placeholder = "",
		SettingsFieldType Type = SettingsFieldType.Text,
		bool Optional = false,
		bool FullWidth = false
	);

	public class PlatformSettingsConfig
	{
		public required string Name { get; init; }
		public required string Color { get; init; }
		public required string NotifierKey { get; init; }
		public required string RecheckEnvKey { get; init; }
		public int RecheckMs { get; set; }
		public required IReadOnlyList<PlatformFieldDef> Fields { get; init; }
		public Dictionary<string, string> Values { get; set; } = [];

		public required Action<AppSettings, PlatformSettingsConfig> SaveTo { get; init; }
	}
}
