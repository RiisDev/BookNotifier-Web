namespace BookNotifier_Web.Models
{
	public class AppSettings
	{
		public bool SetupComplete { get; set; }
		public List<string> EnabledNotifiers { get; set; } = [];
		public string WebhookUrl { get; set; } = "";
		public string FlareSolverUrl { get; set; } = "";

		public int GoodReadsRecheckMs { get; set; } = 300000;
		public string GoodReadsUserId { get; set; } = "";
		public string GoodReadsShelfTag { get; set; } = "to-read";

		public int ScribbleHubRecheckMs { get; set; } = 60000;
		public string ScribbleHubUserId { get; set; } = "";

		public int LiteroticaRecheckMs { get; set; } = 600000;
		public string LiteroticaUsername { get; set; } = "";
		public string LiteroticaPassword { get; set; } = "";

		public int RoyalRoadRecheckMs { get; set; } = 300000;
		public string RoyalRoadUserId { get; set; } = "";

		public int Ao3RecheckMs { get; set; } = 3_600_000;
		public string Ao3Username { get; set; } = "";
		public string Ao3Pseudoname { get; set; } = "";

		public bool AmazonCheckEnabled { get; set; }

		public bool CalendarEnabled { get; set; }

		public AppSettings Clone() => new()
		{
			SetupComplete = SetupComplete,
			EnabledNotifiers = [.. EnabledNotifiers],
			WebhookUrl = WebhookUrl,
			FlareSolverUrl = FlareSolverUrl,
			GoodReadsRecheckMs = GoodReadsRecheckMs,
			GoodReadsUserId = GoodReadsUserId,
			GoodReadsShelfTag = GoodReadsShelfTag,
			ScribbleHubRecheckMs = ScribbleHubRecheckMs,
			ScribbleHubUserId = ScribbleHubUserId,
			LiteroticaRecheckMs = LiteroticaRecheckMs,
			LiteroticaUsername = LiteroticaUsername,
			LiteroticaPassword = LiteroticaPassword,
			RoyalRoadRecheckMs = RoyalRoadRecheckMs,
			RoyalRoadUserId = RoyalRoadUserId,
			Ao3RecheckMs = Ao3RecheckMs,
			Ao3Username = Ao3Username,
			Ao3Pseudoname = Ao3Pseudoname,
			AmazonCheckEnabled = AmazonCheckEnabled,
			CalendarEnabled = CalendarEnabled,
		};
	}
}
