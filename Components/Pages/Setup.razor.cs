using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookNotifier_Web.Models;
using BookNotifier_Web.Services;
using LiteroticaApi.AuthClientData;

namespace BookNotifier_Web.Components.Pages
{
	public record WizardStep(string Id, string Title);

	public partial class Setup : IDisposable
	{
		private AppSettings _s = new();
		private List<WizardStep> _steps = [];
		private int _step;
		private bool _saving;
		private bool _webhookError;
		private string? _verifyRunning;
		private readonly Dictionary<string, string> _verifyMsg = new();
		private readonly Dictionary<string, (bool Ok, string Msg)> _verifyResults = new();
		private WizardStep CurrentStep => _steps[_step];

		private static readonly List<(string Key, string Name, string Color)> PlatformDefs =
		[
			("goodreads",   "GoodReads",   PageLogic.PlatColor(Platform.GoodReads)),
			("scribblehub", "ScribbleHub", PageLogic.PlatColor(Platform.ScribbleHub)),
			("royalroad",   "Royal Road",  PageLogic.PlatColor(Platform.RoyalRoad)),
			("literotica",  "Literotica",  PageLogic.PlatColor(Platform.Literotica)),
			("aothree",     "AO3",         PageLogic.PlatColor(Platform.Ao3)),
		];

		// Minute-friendly wrappers around the raw millisecond settings fields, so the wizard
		// doesn't make users type "300000".
		private int GoodReadsRecheckMinutes   { get => _s.GoodReadsRecheckMs   / 60000; set => _s.GoodReadsRecheckMs   = Math.Max(1, value) * 60000; }
		private int ScribbleHubRecheckMinutes { get => _s.ScribbleHubRecheckMs / 60000; set => _s.ScribbleHubRecheckMs = Math.Max(1, value) * 60000; }
		private int RoyalRoadRecheckMinutes   { get => _s.RoyalRoadRecheckMs   / 60000; set => _s.RoyalRoadRecheckMs   = Math.Max(1, value) * 60000; }
		private int LiteroticaRecheckMinutes  { get => _s.LiteroticaRecheckMs  / 60000; set => _s.LiteroticaRecheckMs  = Math.Max(1, value) * 60000; }
		private int Ao3RecheckMinutes         { get => _s.Ao3RecheckMs        / 60000; set => _s.Ao3RecheckMs         = Math.Max(1, value) * 60000; }

		protected override async Task OnInitializedAsync()
		{
			_s = SettingsService.Clone();
			if (_s.SetupComplete)
			{
				Nav.NavigateTo("/", replace: true);
				return;
			}

			// Populate _steps before the first `await` below — Blazor renders once at that suspension
			// point with whatever state exists then, and CurrentStep indexes into _steps unconditionally,
			// so leaving it empty here crashes the very first render on any fresh install.
			RebuildSteps();
			CliRunner.RunProgressChanged += OnRunProgress;

			if (string.IsNullOrWhiteSpace(_s.FlareSolverUrl))
				_s.FlareSolverUrl = await DetectFlareSolverUrlAsync() ?? "";
		}

		public void Dispose() => CliRunner.RunProgressChanged -= OnRunProgress;

		private void OnRunProgress() => InvokeAsync(StateHasChanged);

		private async Task RunNotifierNowAsync() => await CliRunner.RunOnceAsync();

		// docker-compose ships a `flaresolverr` service at this hostname — if it answers, prefill
		// the field instead of leaving users to guess the URL (and the required /v1 suffix).
		private static async Task<string?> DetectFlareSolverUrlAsync()
		{
			const string candidate = "http://flaresolverr:8191";
			try
			{
				using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };
				HttpResponseMessage resp = await http.GetAsync(candidate);
				return resp.IsSuccessStatusCode ? $"{candidate}/v1" : null;
			}
			catch
			{
				return null;
			}
		}

		private void RebuildSteps()
		{
			_steps =
			[
				new WizardStep("welcome", "Platforms"),
				new WizardStep("webhook", "Notifications")
			];
			foreach ((string key, string name, string _) in PlatformDefs)
				if (_s.EnabledNotifiers.Contains(key))
					_steps.Add(new WizardStep(key, name));
			_steps.Add(new WizardStep("releases", "Release Tracking"));
			_steps.Add(new WizardStep("done", "Done"));
		}

		private async Task Next()
		{
			if (CurrentStep.Id == "welcome")
				RebuildSteps();

			if (CurrentStep.Id == "webhook")
			{
				_webhookError = string.IsNullOrWhiteSpace(_s.WebhookUrl)
				                || !_s.WebhookUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
				if (_webhookError) return;
			}

			if (_step == _steps.Count - 2)
			{
				_saving = true;
				StateHasChanged();
				_s.SetupComplete = true;
				await SettingsService.SaveAsync(_s);
				_saving = false;
			}

			if (_step < _steps.Count - 1)
				_step++;
		}

		private void Back()
		{
			if (_step > 0) _step--;
		}

		private void GoToDashboard() => Nav.NavigateTo("/", replace: true);

		private void TogglePlatform(string key)
		{
			if (_s.EnabledNotifiers.Contains(key)) _s.EnabledNotifiers.Remove(key);
			else _s.EnabledNotifiers.Add(key);
		}

		private async Task VerifyAsync(string stepId)
		{
			_verifyRunning = stepId;
			_verifyMsg[stepId] = "Checking…";
			_verifyResults.Remove(stepId);
			StateHasChanged();

			(bool Ok, string Msg) result = stepId switch
			{
				"webhook" => await VerifyWebhookAsync(),
				"goodreads" => await VerifyGoodReadsAsync(),
				"scribblehub" => await VerifyScribbleHubAsync(),
				"royalroad" => await VerifyRoyalRoadAsync(),
				"literotica" => await VerifyLiteroticaAsync(),
				"aothree" => await VerifyAo3Async(),
				_ => (true, "No check available."),
			};

			_verifyResults[stepId] = result;
			_verifyRunning = null;
			StateHasChanged();
		}

		private async Task<(bool Ok, string Msg)> VerifyWebhookAsync()
		{
			if (string.IsNullOrWhiteSpace(_s.WebhookUrl)) return (false, "Enter a webhook URL first.");
			if (!Regex.IsMatch(_s.WebhookUrl, @"^https://(discord|discordapp|canary\.discord|ptb\.discord)\.com/api/webhooks/\d+/", RegexOptions.IgnoreCase))
				return (false, "That doesn't look like a Discord webhook URL.");
			return await HeadCheckAsync(_s.WebhookUrl,
				"Webhook reachable.",
				"Webhook not found — double-check the URL.");
		}

		private async Task<(bool Ok, string Msg)> VerifyGoodReadsAsync()
		{
			if (string.IsNullOrWhiteSpace(_s.GoodReadsUserId)) return (false, "Enter your GoodReads user ID first.");
			return await HeadCheckAsync(
				$"https://www.goodreads.com/user/show/{Uri.EscapeDataString(_s.GoodReadsUserId)}",
				$"User {_s.GoodReadsUserId} found.",
				"User not found — check your user ID.");
		}

		private async Task<(bool Ok, string Msg)> VerifyRoyalRoadAsync()
		{
			if (string.IsNullOrWhiteSpace(_s.RoyalRoadUserId)) return (false, "Enter your Royal Road user ID first.");
			return await FlareSolverCheckAsync(_s.FlareSolverUrl,
				$"https://www.royalroad.com/profile/{Uri.EscapeDataString(_s.RoyalRoadUserId)}",
				$"Profile {_s.RoyalRoadUserId} found.",
				"Profile not found — check your user ID.");
		}

		// AO3, ScribbleHub and RoyalRoad sit behind Cloudflare — a direct request 503s regardless of whether
		// the account is real, so verifying them has to go through FlareSolverr, same as the CLI does.
		private async Task<(bool Ok, string Msg)> VerifyAo3Async()
		{
			if (string.IsNullOrWhiteSpace(_s.Ao3Username)) return (false, "Enter your AO3 username first.");
			return await FlareSolverCheckAsync(_s.FlareSolverUrl,
				$"https://archiveofourown.org/users/{Uri.EscapeDataString(_s.Ao3Username)}/bookmarks.atom",
				$"Public bookmarks found for {_s.Ao3Username}.",
				"User not found or bookmarks are set to private.");
		}

		private async Task<(bool Ok, string Msg)> VerifyScribbleHubAsync()
		{
			if (string.IsNullOrWhiteSpace(_s.ScribbleHubUserId)) return (false, "Enter your ScribbleHub user ID first.");
			return await FlareSolverCheckAsync(_s.FlareSolverUrl,
				$"https://www.scribblehub.com/profile/{Uri.EscapeDataString(_s.ScribbleHubUserId)}/",
				$"User {_s.ScribbleHubUserId} found.",
				"User not found — check your user ID.");
		}

		private async Task<(bool Ok, string Msg)> VerifyLiteroticaAsync()
		{
			if (string.IsNullOrWhiteSpace(_s.LiteroticaUsername) || string.IsNullOrWhiteSpace(_s.LiteroticaPassword))
				return (false, "Enter username and password first.");

			_verifyMsg["literotica"] = "Signing in…";
			StateHasChanged();

			try
			{
				AuthClient auth = new();
				bool ok = await auth.LoginAsync(_s.LiteroticaUsername, _s.LiteroticaPassword);
				return ok
					? (true, $"Signed in as {_s.LiteroticaUsername}.")
					: (false, "Login failed — check your credentials.");
			}
			catch (Exception ex)
			{
				return (false, ex.Message);
			}
		}

		private static async Task<(bool Ok, string Msg)> HeadCheckAsync(string url, string successMsg,
			string notFoundMsg)
		{
			try
			{
				using HttpClient http = new();
				http.Timeout = TimeSpan.FromSeconds(10);
				http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; BookNotifier/1.0)");

				HttpResponseMessage resp = await http.SendAsync(
					new HttpRequestMessage(HttpMethod.Head, url),
					HttpCompletionOption.ResponseHeadersRead);

				if (resp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
					resp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead);

				return resp.IsSuccessStatusCode
					? (true, successMsg)
					: (false, resp.StatusCode == System.Net.HttpStatusCode.NotFound
						? notFoundMsg
						: $"HTTP {(int)resp.StatusCode}");
			}
			catch (TaskCanceledException)
			{
				return (false, "Timed out.");
			}
			catch (HttpRequestException ex)
			{
				return (false, ex.Message);
			}
		}

		// Routes a check through FlareSolverr (POST {cmd:"request.get", url, maxTimeout}) instead of
		// hitting the target directly — same contract BookNotifier's own FlareSolverClient uses.
		private static async Task<(bool Ok, string Msg)> FlareSolverCheckAsync(string flareSolverUrl, string targetUrl,
			string successMsg, string notFoundMsg)
		{
			if (string.IsNullOrWhiteSpace(flareSolverUrl))
				return (false, "FlareSolverr URL is required to verify this platform.");

			try
			{
				using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(65) };
				HttpResponseMessage resp = await http.PostAsJsonAsync(flareSolverUrl,
					new { cmd = "request.get", url = targetUrl, maxTimeout = 60000 });
				string json = await resp.Content.ReadAsStringAsync();
				using JsonDocument doc = JsonDocument.Parse(json);

				string status = doc.RootElement.TryGetProperty("status", out JsonElement st) ? st.GetString() ?? "" : "";
				if (status != "ok")
				{
					string msg = doc.RootElement.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? "" : "";
					return (false, string.IsNullOrEmpty(msg) ? "FlareSolverr returned an error." : $"FlareSolverr error: {msg}");
				}

				int solvedStatus = doc.RootElement.GetProperty("solution").GetProperty("status").GetInt32();
				return solvedStatus == 200
					? (true, successMsg)
					: (false, $"{notFoundMsg} (via FlareSolverr, HTTP {solvedStatus})");
			}
			catch (Exception ex)
			{
				return (false, $"FlareSolverr request failed: {ex.Message}");
			}
		}
	}
}
