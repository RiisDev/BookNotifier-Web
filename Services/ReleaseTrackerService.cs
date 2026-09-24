using System.Net.Http.Json;
using BookNotifier_Web.Data;
using BookNotifier_Web.Models;

namespace BookNotifier_Web.Services
{
	// Watches pending_releases (populated by CliRunnerService's GoodReads ingest, via AmazonService)
	// for release dates that have arrived, and posts a Discord reminder. This is Web-side only — the
	// BookNotifier CLI has no concept of Amazon pre-order dates, so it can't do this itself.
	public class ReleaseTrackerService(DatabaseService db, BookNotifierService bookService, SettingsService settings, ILogger<ReleaseTrackerService> logger) : BackgroundService
	{
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (bookService.IsMockMode) return;

			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await CheckReleasesAsync(stoppingToken);
				}
				catch (OperationCanceledException) { break; }
				catch (Exception ex)
				{
					logger.LogError(ex, "ReleaseTrackerService check failed");
				}

				try
				{
					await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
				}
				catch (OperationCanceledException) { break; }
			}
		}

		private async Task CheckReleasesAsync(CancellationToken ct)
		{
			AppSettings s = settings.Clone();
			if (s is { CalendarEnabled: false, AmazonCheckEnabled: false }) return;

			DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
			List<DbPendingRelease> pending = await db.GetPendingReleasesAsync(notifiedOnly: true);

			foreach (DbPendingRelease release in pending)
			{
				ct.ThrowIfCancellationRequested();
				if (release.ReleaseDate is null) continue;
				if (!DateOnly.TryParse(release.ReleaseDate, out DateOnly releaseDay)) continue;
				if (releaseDay > today) continue;

				logger.LogInformation("Release day for '{Title}' — sending notification", release.Title);

				await PostReleaseWebhookAsync(s.WebhookUrl, release, releaseDay, ct);

				bookService.AddEvent(new NotificationEvent
				{
					Title    = release.Title,
					Platform = Platform.GoodReads,
					Url      = release.AmazonUrl ?? release.BookUrl,
					Type     = EventType.BookReleased,
				});

				await db.MarkReleaseNotifiedAsync(release.Id);
			}
		}

		private static async Task PostReleaseWebhookAsync(string webhookUrl, DbPendingRelease release, DateOnly releaseDay, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(webhookUrl)) return;

			string url = release.AmazonUrl ?? release.BookUrl;
			object payload = new
			{
				embeds = new[]
				{
					new
					{
						title = $"📚 {release.Title} is out today!",
						url,
						description = $"Released {releaseDay:MMMM d, yyyy}",
						color = 0xE9722E,
					},
				},
			};

			using HttpClient http = new();
			try { await http.PostAsJsonAsync(webhookUrl, payload, ct); }
			catch { /* best-effort notification */ }
		}
	}
}
