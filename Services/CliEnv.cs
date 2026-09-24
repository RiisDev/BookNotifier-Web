using BookNotifier_Web.Models;

namespace BookNotifier_Web.Services
{
	// Maps AppSettings onto the env vars the BookNotifier CLI (RiisDev/BookNotifier) expects.
	// Source of truth: that repo's README "Configuration" table.
	public static class CliEnv
	{
		// Web tracks Ao3 internally as "aothree" (matches its DB/log keys); the CLI's NOTIFIER list wants "ao3".
		public static string ToCliNotifierKey(string webKey) => webKey == "aothree" ? "ao3" : webKey;

		public static Dictionary<string, string> Build(AppSettings s)
		{
			Dictionary<string, string> env = new()
			{
				["NOTIFIER"] = string.Join(",", s.EnabledNotifiers.Select(ToCliNotifierKey)),
				["WEBHOOK"] = s.WebhookUrl,
			};

			if (!string.IsNullOrWhiteSpace(s.FlareSolverUrl)) env["FLARESOLVER_URL"] = s.FlareSolverUrl;

			if (s.EnabledNotifiers.Contains("goodreads"))
			{
				env["GOODREADS_RECHECK_MS"] = s.GoodReadsRecheckMs.ToString();
				env["GOODREADS_USER_ID"] = s.GoodReadsUserId;
				env["GOODREADS_SHELF_TAG"] = s.GoodReadsShelfTag;
			}

			if (s.EnabledNotifiers.Contains("scribblehub"))
			{
				env["SCRIBBLEHUB_RECHECK_MS"] = s.ScribbleHubRecheckMs.ToString();
				env["SCRIBBLEHUB_USERID"] = s.ScribbleHubUserId;
			}

			if (s.EnabledNotifiers.Contains("literotica"))
			{
				env["LITEROTICA_RECHECK_MS"] = s.LiteroticaRecheckMs.ToString();
				env["LITEROTICA_USERNAME"] = s.LiteroticaUsername;
				env["LITEROTICA_PASSWORD"] = s.LiteroticaPassword;
			}

			if (s.EnabledNotifiers.Contains("royalroad"))
			{
				env["ROYALROAD_RECHECK_MS"] = s.RoyalRoadRecheckMs.ToString();
				env["ROYALROAD_USERID"] = s.RoyalRoadUserId;
			}

			if (s.EnabledNotifiers.Contains("aothree"))
			{
				env["AO3_RECHECK_MS"] = s.Ao3RecheckMs.ToString();
				env["AO3_USERNAME"] = s.Ao3Username;
				env["AO3_PSEUDO"] = string.IsNullOrWhiteSpace(s.Ao3Pseudoname) ? s.Ao3Username : s.Ao3Pseudoname;
			}

			return env;
		}
	}
}
