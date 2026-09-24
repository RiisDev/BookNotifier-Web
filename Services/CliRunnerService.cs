using System.Diagnostics;
using System.Text.Json;
using BookNotifier_Web.Components.Pages;
using BookNotifier_Web.Data;
using BookNotifier_Web.Models;

namespace BookNotifier_Web.Services
{
	// Launches the BookNotifier CLI (RiisDev/BookNotifier) as a subprocess, routes its stdout/stderr into
	// the log tables, and periodically ingests its JSON caches (<cli-dir>/data/*.json) into our SQLite schema
	// so the dashboard has something to show. See BookNotifier/TODO.md for the data-richness gap this leaves.
	public class CliRunnerService(DatabaseService db, BookNotifierService bookService, SettingsService settings, AmazonService amazon) : BackgroundService
	{
		private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

		public sealed record PlatformRunState(Platform Platform, string Status, string? Detail);

		private readonly Lock _runLock = new();
		public bool IsRunningOnce { get; private set; }
		public IReadOnlyList<PlatformRunState> RunStates { get; private set; } = [];
		public event Action? RunProgressChanged;

		// Triggered from the Setup wizard's "Run Notifier Now" button: runs the CLI once
		// (--runonce, exits when done) so the library fills up without waiting for a restart
		// or the CLI's own recheck intervals, then ingests whatever it produced.
		public async Task RunOnceAsync()
		{
			lock (_runLock)
			{
				if (IsRunningOnce) return;
				IsRunningOnce = true;
			}

			AppSettings s = settings.Clone();
			RunStates = PageLogic.Platforms
				.Where(p => s.EnabledNotifiers.Contains(PageLogic.PlatKey(p)))
				.Select(p => new PlatformRunState(p, "pending", null))
				.ToList();
			RunProgressChanged?.Invoke();

			try
			{
				string? cliDir = FindCliDir();
				if (cliDir is null)
				{
					await db.AddLogAsync("cli", "ERROR", "CliRunnerService.RunOnce", "BookNotifier CLI not found.");
					RunStates = RunStates.Select(r => r with { Status = "error", Detail = "CLI not found" }).ToList();
					return;
				}

				if (s.EnabledNotifiers.Count == 0 || string.IsNullOrWhiteSpace(s.WebhookUrl))
				{
					await db.AddLogAsync("cli", "WARN", "CliRunnerService.RunOnce", "No notifiers enabled / no webhook set.");
					return;
				}

				string dll = Path.Combine(cliDir, "BookNotifier.dll");
				ProcessStartInfo psi = new("dotnet")
				{
					WorkingDirectory = cliDir,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
				};
				psi.ArgumentList.Add(dll);
				psi.ArgumentList.Add("--runonce");
				foreach ((string k, string v) in CliEnv.Build(s)) psi.Environment[k] = v;

				using Process proc = new() { StartInfo = psi };
				proc.OutputDataReceived += (_, e) => { if (e.Data is null) return; _ = RouteLogAsync(e.Data); UpdateRunState(e.Data); };
				proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) _ = RouteLogAsync(e.Data, forceError: true); };

				proc.Start();
				proc.BeginOutputReadLine();
				proc.BeginErrorReadLine();
				await proc.WaitForExitAsync();

				bool changed = await IngestAllAsync(Path.Combine(cliDir, "data"), settings.Clone(), CancellationToken.None);
				if (changed) await bookService.InitializeAsync();

				RunStates = RunStates.Select(r => r.Status is "pending" or "running" ? r with { Status = "done" } : r).ToList();
			}
			catch (Exception ex)
			{
				await db.AddLogAsync("cli", "ERROR", "CliRunnerService.RunOnce", ex.Message);
				RunStates = RunStates.Select(r => r with { Status = "error", Detail = ex.Message }).ToList();
			}
			finally
			{
				lock (_runLock) IsRunningOnce = false;
				RunProgressChanged?.Invoke();
			}
		}

		private void UpdateRunState(string line)
		{
			string status =
				line.Contains("Check complete.", StringComparison.OrdinalIgnoreCase) ? "done" :
				line.Contains("Error", StringComparison.OrdinalIgnoreCase)           ? "error" :
				line.Contains("Running...", StringComparison.OrdinalIgnoreCase)      ? "running" :
				"";
			if (status.Length == 0) return;

			bool matched = false;
			List<PlatformRunState> updated = RunStates.Select(r =>
			{
				string cliKey = PageLogic.PlatKey(r.Platform) == "aothree" ? "ao3" : PageLogic.PlatKey(r.Platform);
				if (!line.Contains($"[{cliKey}]", StringComparison.OrdinalIgnoreCase)) return r;
				matched = true;
				return r with { Status = status, Detail = line };
			}).ToList();
			if (!matched) return;

			RunStates = updated;
			RunProgressChanged?.Invoke();
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (bookService.IsMockMode) return;

			string? cliDir = FindCliDir();
			if (cliDir is null)
			{
				await db.AddLogAsync("cli", "ERROR", "CliRunnerService",
					"BookNotifier CLI not found. Build ../BookNotifier or set BOOKNOTIFIER_CLI_PATH.");
				return;
			}

			await Task.WhenAll(
				RunProcessLoopAsync(cliDir, stoppingToken),
				RunIngestLoopAsync(cliDir, stoppingToken)
			);
		}

		private async Task RunProcessLoopAsync(string cliDir, CancellationToken ct)
		{
			AppSettings s = settings.Clone();
			if (s.EnabledNotifiers.Count == 0 || string.IsNullOrWhiteSpace(s.WebhookUrl))
			{
				await db.AddLogAsync("cli", "WARN", "CliRunnerService", "No notifiers enabled / no webhook set — CLI not started.");
				return;
			}

			string dll = Path.Combine(cliDir, "BookNotifier.dll");
			while (!ct.IsCancellationRequested)
			{
				ProcessStartInfo psi = new("dotnet")
				{
					WorkingDirectory = cliDir,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
				};
				psi.ArgumentList.Add(dll);
				foreach ((string k, string v) in CliEnv.Build(s)) psi.Environment[k] = v;

				using Process proc = new() { StartInfo = psi, EnableRaisingEvents = true };
				proc.OutputDataReceived += (_, e) => { if (e.Data is not null) _ = RouteLogAsync(e.Data); };
				proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) _ = RouteLogAsync(e.Data, forceError: true); };

				try
				{
					proc.Start();
					proc.BeginOutputReadLine();
					proc.BeginErrorReadLine();
					await proc.WaitForExitAsync(ct);
					await db.AddLogAsync("cli", "WARN", "CliRunnerService", $"BookNotifier CLI exited (code {proc.ExitCode}) — restarting in 30s.");
				}
				catch (OperationCanceledException)
				{
					if (!proc.HasExited) proc.Kill(entireProcessTree: true);
					break;
				}
				catch (Exception ex)
				{
					await db.AddLogAsync("cli", "ERROR", "CliRunnerService", $"Failed to run BookNotifier CLI: {ex.Message}");
				}

				try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
				catch (OperationCanceledException) { break; }
			}
		}

		private async Task RouteLogAsync(string line, bool forceError = false)
		{
			string platform =
				line.Contains("GoodReads", StringComparison.OrdinalIgnoreCase)  ? "goodreads" :
				line.Contains("Scribble", StringComparison.OrdinalIgnoreCase)   ? "scribblehub" :
				line.Contains("RoyalRoad", StringComparison.OrdinalIgnoreCase)  ? "royalroad" :
				line.Contains("Literotica", StringComparison.OrdinalIgnoreCase) ? "literotica" :
				line.Contains("Ao3", StringComparison.OrdinalIgnoreCase)        ? "aothree" :
				"cli";
			string level = forceError || line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ? "ERROR" : "INFO";
			try { await db.AddLogAsync(platform, level, "BookNotifier CLI", line); }
			catch { /* best-effort logging */ }
		}

		private async Task RunIngestLoopAsync(string cliDir, CancellationToken ct)
		{
			string dataDir = Path.Combine(cliDir, "data");
			while (!ct.IsCancellationRequested)
			{
				try
				{
					AppSettings s = settings.Clone();
					bool changed = await IngestAllAsync(dataDir, s, ct);
					if (changed) await bookService.InitializeAsync();

					foreach (Platform p in PageLogic.Platforms)
						bookService.SetPlatformStatus(p, s.EnabledNotifiers.Contains(PageLogic.PlatKey(p)), false, null, GetRecheckMs(p, s));
				}
				catch (Exception ex)
				{
					await db.AddLogAsync("cli", "ERROR", "CliRunnerService.Ingest", ex.Message);
				}

				try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
				catch (OperationCanceledException) { break; }
			}
		}

		private async Task<bool> IngestAllAsync(string dataDir, AppSettings s, CancellationToken ct)
		{
			bool changed = false;
			changed |= await IngestGoodReadsAsync(dataDir, s, ct);
			changed |= await IngestScribbleHubAsync(dataDir, ct);
			changed |= await IngestRoyalRoadAsync(dataDir, ct);
			changed |= await IngestAo3Async(dataDir, ct);
			changed |= await IngestLiteroticaAsync(dataDir, ct);
			return changed;
		}

		private static int GetRecheckMs(Platform p, AppSettings s) => p switch
		{
			Platform.GoodReads => s.GoodReadsRecheckMs,
			Platform.ScribbleHub => s.ScribbleHubRecheckMs,
			Platform.RoyalRoad => s.RoyalRoadRecheckMs,
			Platform.Literotica => s.LiteroticaRecheckMs,
			Platform.Ao3 => s.Ao3RecheckMs,
			_ => 300_000
		};

		private async Task<bool> IngestGoodReadsAsync(string dataDir, AppSettings s, CancellationToken ct)
		{
			List<CliGoodReadsBook>? books = await ReadJsonAsync<List<CliGoodReadsBook>>(Path.Combine(dataDir, "goodreads.json"), ct);
			if (books is null) return false;

			bool changed = false;
			string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
			foreach (CliGoodReadsBook b in books)
			{
				if (string.IsNullOrWhiteSpace(b.Url)) continue;
				string title = Decode(b.Title);
				string author = Decode(b.AuthorName);
				(long id, bool isNew) = await db.UpsertBookAsync(new DbBook(0, now, title, b.Url, b.CoverUrl, author, "goodreads", null, b.Status));
				if (!isNew) continue;
				changed = true;
				bookService.AddEvent(new NotificationEvent
				{
					Title = title, Author = author, Platform = Platform.GoodReads, Url = b.Url,
					Type = EventType.NewBook,
					Description = b.SeriesName is null ? "New book added" : $"New entry in {Decode(b.SeriesName)}",
				});

				if (!s.AmazonCheckEnabled) continue;
				(string? amazonUrl, DateOnly? releaseDate) = await amazon.LookupAsync(title, author, ct);
				if (amazonUrl is null) continue;
				await db.UpsertPendingReleaseAsync(new DbPendingRelease(0, id, title, b.Url, amazonUrl,
					releaseDate?.ToString("yyyy-MM-dd"), false, false, now));
			}
			return changed;
		}

		private async Task<bool> IngestScribbleHubAsync(string dataDir, CancellationToken ct)
		{
			List<CliScribbleBook>? stories = await ReadJsonAsync<List<CliScribbleBook>>(Path.Combine(dataDir, "scribblehub.json"), ct);
			if (stories is null) return false;

			bool changed = false;
			string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
			foreach (CliScribbleBook story in stories)
			{
				if (string.IsNullOrWhiteSpace(story.Link)) continue;
				string name = Decode(story.Name);
				(long id, bool isNewBook) = await db.UpsertBookAsync(new DbBook(0, now, name, story.Link, story.CoverUrl, "", "scribblehub", null, story.Status));
				if (isNewBook)
				{
					changed = true;
					bookService.AddEvent(new NotificationEvent { Title = name, Platform = Platform.ScribbleHub, Url = story.Link, Type = EventType.NewBook, Description = "New series added to reading list" });
				}

				foreach (CliChapter c in story.Chapters)
				{
					if (string.IsNullOrWhiteSpace(c.Link)) continue;
					string chapterTitle = Decode(c.Title);
					bool isNewChapter = await db.UpsertChapterAsync(new DbChapter(0, id, FormatDate(c.ReleasedAt, now), chapterTitle, c.Link));
					if (isNewChapter && !isNewBook)
					{
						changed = true;
						bookService.AddEvent(new NotificationEvent { Title = name, Platform = Platform.ScribbleHub, ChapterTitle = chapterTitle, Url = c.Link, Type = EventType.NewChapter, Description = chapterTitle });
					}
				}
				if (story.Chapters.Count > 0) { await UpdatePredictionAsync(id); changed = true; }
			}
			return changed;
		}

		private async Task<bool> IngestRoyalRoadAsync(string dataDir, CancellationToken ct)
		{
			List<CliRoyalRoadBook>? fictions = await ReadJsonAsync<List<CliRoyalRoadBook>>(Path.Combine(dataDir, "royalroad.json"), ct);
			if (fictions is null) return false;

			bool changed = false;
			string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
			foreach (CliRoyalRoadBook book in fictions)
			{
				if (string.IsNullOrWhiteSpace(book.Url)) continue;
				string title = Decode(book.Title);
				(long id, bool isNewBook) = await db.UpsertBookAsync(new DbBook(0, now, title, book.Url, book.CoverUrl, "", "royalroad", null, book.Status));
				if (isNewBook)
				{
					changed = true;
					bookService.AddEvent(new NotificationEvent { Title = title, Platform = Platform.RoyalRoad, Url = book.Url, Type = EventType.NewBook, Description = "New favourite added" });
				}

				foreach (CliBareChapter c in book.Chapters)
				{
					if (string.IsNullOrWhiteSpace(c.Url)) continue;
					string chapterTitle = Decode(c.Title);
					bool isNewChapter = await db.UpsertChapterAsync(new DbChapter(0, id, FormatDate(c.ReleasedAt, now), chapterTitle, c.Url));
					if (isNewChapter && !isNewBook)
					{
						changed = true;
						bookService.AddEvent(new NotificationEvent { Title = title, Platform = Platform.RoyalRoad, ChapterTitle = chapterTitle, Url = c.Url, Type = EventType.NewChapter, Description = chapterTitle });
					}
				}
				if (book.Chapters.Count > 0) { await UpdatePredictionAsync(id); changed = true; }
			}
			return changed;
		}

		private async Task<bool> IngestAo3Async(string dataDir, CancellationToken ct)
		{
			List<CliAo3Book>? works = await ReadJsonAsync<List<CliAo3Book>>(Path.Combine(dataDir, "ao3.json"), ct);
			if (works is null) return false;

			bool changed = false;
			string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
			foreach (CliAo3Book work in works)
			{
				if (string.IsNullOrWhiteSpace(work.Url)) continue;
				string title = Decode(work.Title);
				string author = Decode(work.Author);
				(long id, bool isNewBook) = await db.UpsertBookAsync(new DbBook(0, now, title, work.Url, null, author, "aothree", null, work.Status));
				if (isNewBook)
				{
					changed = true;
					bookService.AddEvent(new NotificationEvent { Title = title, Author = author, Platform = Platform.Ao3, Url = work.Url, Type = EventType.NewBook, Description = "New bookmark added" });
				}

				foreach (CliBareChapter c in work.Chapters)
				{
					if (string.IsNullOrWhiteSpace(c.Url)) continue;
					string chapterTitle = Decode(c.Title);
					bool isNewChapter = await db.UpsertChapterAsync(new DbChapter(0, id, FormatDate(c.ReleasedAt, now), chapterTitle, c.Url));
					if (isNewChapter && !isNewBook)
					{
						changed = true;
						bookService.AddEvent(new NotificationEvent { Title = title, Author = author, Platform = Platform.Ao3, ChapterTitle = chapterTitle, Url = c.Url, Type = EventType.NewChapter, Description = chapterTitle });
					}
				}
				if (work.Chapters.Count > 0) { await UpdatePredictionAsync(id); changed = true; }
			}
			return changed;
		}

		private async Task<bool> IngestLiteroticaAsync(string dataDir, CancellationToken ct)
		{
			CliLiteroticaData? data = await ReadJsonAsync<CliLiteroticaData>(Path.Combine(dataDir, "literotica.json"), ct);
			if (data is null) return false;

			bool changed = false;
			string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
			foreach (CliLiteroticaWork work in data.Works)
			{
				// Literotica "works" often have no landing page of their own (Url is "" or a bare slug) —
				// only chapters have real URLs. Key the book row on the first chapter's URL in that case,
				// since books.url is UNIQUE and every blank/slug Url would otherwise collide.
				string? bookUrl = work.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
					? work.Url
					: work.Chapters.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Url))?.Url;
				if (string.IsNullOrWhiteSpace(bookUrl)) continue;

				string title = Decode(work.Title);
				string author = Decode(work.Author);
				(long id, bool isNewBook) = await db.UpsertBookAsync(new DbBook(0, now, title, bookUrl, null, author, "literotica", null, work.Status));
				if (isNewBook)
				{
					changed = true;
					bookService.AddEvent(new NotificationEvent { Title = title, Author = author, Platform = Platform.Literotica, Url = bookUrl, Type = EventType.NewBook, Description = "New story found" });
				}

				foreach (CliLiteroticaChapter c in work.Chapters)
				{
					if (string.IsNullOrWhiteSpace(c.Url)) continue;
					string chapterTitle = Decode(c.Title);
					bool isNewChapter = await db.UpsertChapterAsync(new DbChapter(0, id, FormatDate(c.PublishedAt, now), chapterTitle, c.Url));
					if (isNewChapter && !isNewBook)
					{
						changed = true;
						bookService.AddEvent(new NotificationEvent { Title = title, Author = author, Platform = Platform.Literotica, ChapterTitle = chapterTitle, Url = c.Url, Type = EventType.NewChapter, Description = chapterTitle });
					}
				}
				if (work.Chapters.Count > 0) { await UpdatePredictionAsync(id); changed = true; }
			}
			return changed;
		}

		// Recomputes release_cycle_days from this book's chapter history (now that the CLI reports
		// real chapter dates) so the calendar/NextExpected prediction actually has something to work with.
		private async Task UpdatePredictionAsync(long bookId)
		{
			List<DbChapter> chapters = await db.GetChaptersAsync(bookId);
			List<DateTime> dates = chapters
				.Select(c => DateTime.TryParse(c.ReleasedAt, out DateTime d) ? d : (DateTime?)null)
				.Where(d => d.HasValue).Select(d => d!.Value)
				.OrderBy(d => d)
				.TakeLast(10)
				.ToList();

			DateTime? next = DatabaseService.EstimateFromHistory(dates);
			if (next is null) return;

			double cycleDays = dates.Count >= 2 ? (next.Value - dates[^1]).TotalDays : 7;
			await db.SetBookCycleAsync(bookId, cycleDays);

			// A flat cycle length can't reproduce an alternating schedule (Mon/Wed/Fri isn't "every 2
			// days") — persist the actual weekday set too, when one's confirmed, so the calendar grid
			// can project by matching weekdays directly instead of fixed-step arithmetic.
			DatabaseService.TryEstimateWeeklyPattern(dates, out _, out int? weekdayMask);
			await db.SetBookWeeklyPatternAsync(bookId, weekdayMask);
		}

		private static string FormatDate(DateTime? d, string fallback) => d?.ToString("yyyy-MM-dd HH:mm:ss") ?? fallback;

		// The CLI's scraped titles sometimes carry raw HTML entities (e.g. "Deceiver&#x27;s Path") —
		// that's just text, not markup, so the browser renders it literally instead of decoding it.
		// Decode once here, at the boundary where CLI data enters our system.
		private static string Decode(string s) => System.Net.WebUtility.HtmlDecode(s);

		private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct) where T : class
		{
			if (!File.Exists(path)) return null;
			try
			{
				await using FileStream fs = File.OpenRead(path);
				return await JsonSerializer.DeserializeAsync<T>(fs, JsonOpts, ct);
			}
			catch
			{
				return null;
			}
		}

		private static string? FindCliDir()
		{
			string? overridePath = Environment.GetEnvironmentVariable("BOOKNOTIFIER_CLI_PATH");
			if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(Path.Combine(overridePath, "BookNotifier.dll")))
				return overridePath;

			string dockerPath = Path.Combine(AppContext.BaseDirectory, "cli");
			if (File.Exists(Path.Combine(dockerPath, "BookNotifier.dll")))
				return dockerPath;

			DirectoryInfo? dir = new(AppContext.BaseDirectory);
			while (dir is not null)
			{
				foreach (string cfg in new[] { "Debug", "Release" })
				{
					string dev = Path.Combine(dir.FullName, "BookNotifier", "bin", cfg, "net10.0");
					if (File.Exists(Path.Combine(dev, "BookNotifier.dll"))) return dev;
				}
				dir = dir.Parent;
			}
			return null;
		}
	}
}
