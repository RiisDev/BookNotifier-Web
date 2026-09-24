using BookNotifier_Web.Components.Pages;
using BookNotifier_Web.Data;
using BookNotifier_Web.Models;

namespace BookNotifier_Web.Services
{
	public class BookNotifierService(DatabaseService db)
	{
		public List<BookEntry> Books { get; } = [];
		public List<NotificationEvent> Events { get; } = [];
		public List<DbPendingRelease> UpcomingReleases { get; } = [];
		public List<PlatformStatus> Platforms { get; } = [];

		public Func<Platform, Task>? RecheckDelegate { get; set; }
		public bool IsMockMode { get; private set; }

		public void EnableMockMode() => IsMockMode = true;

		private readonly Lock _lock = new();

		public async Task InitializeAsync()
		{
			lock (_lock)
			{
				Platforms.Clear();
				foreach (Platform p in Enum.GetValues<Platform>())
				{
					Platforms.Add(new PlatformStatus
					{
						Platform = p,
						IsEnabled = false,
						IsRunning = false,
						CheckInterval = TimeSpan.FromMinutes(5),
					});
				}
			}

			List<DbBookWithStats> rows = await db.GetBooksWithStatsAsync();
			lock (_lock)
			{
				Books.Clear();
				foreach (DbBookWithStats row in rows)
					Books.Add(MapToEntry(row));
			}

			List<DbPendingRelease> pending = await db.GetPendingReleasesAsync(notifiedOnly: true);
			lock (_lock)
			{
				UpcomingReleases.Clear();
				UpcomingReleases.AddRange(pending);

				foreach (DbPendingRelease r in pending)
				{
					if (r.ReleaseDate is null) continue;
					if (!DateOnly.TryParse(r.ReleaseDate, out DateOnly d)) continue;
					DateTime dt = d.ToDateTime(TimeOnly.MinValue);
					if (dt < DateTime.Today) continue;
					BookEntry? entry = Books.FirstOrDefault(b => b.Id == r.BookId.ToString());
					if (entry is not null) entry.NextExpected = dt;
				}
			}

			List<NotificationEvent> events = await db.GetRecentEventsAsync();
			lock (_lock)
			{
				Events.Clear();
				Events.AddRange(events); // already ordered newest-first by the query
			}
		}

		public IEnumerable<BookEntry> GetBooks(Platform? platform = null) =>
			platform.HasValue
				? Books.Where(b => b.Platform == platform)
				: Books;

		public IEnumerable<NotificationEvent> GetRecentEvents(int count = 20) => Events.OrderByDescending(e => e.Timestamp).Take(count);

		public IEnumerable<NotificationEvent> GetTodayEvents() => Events.Where(e => e.Timestamp >= DateTime.Today);

		public int GetUnreadCount() => Events.Count(e => !e.IsRead);

		public IEnumerable<BookEntry> GetCalendarBooks(int year, int month) => Books.Where(b => b.NextExpected.HasValue
			                 && b.NextExpected.Value.Year  == year
			                 && b.NextExpected.Value.Month == month);

		public void UpsertBook(DbBook book, bool isNew, int chapterCount = 0, DateTime? lastChapterAt = null)
		{
			lock (_lock)
			{
				BookEntry? existing = Books.FirstOrDefault(b => b.Id == book.Id.ToString());
				if (existing is null)
				{
					Books.Add(MapToEntry(new DbBookWithStats(book, chapterCount, lastChapterAt?.ToString("yyyy-MM-dd HH:mm:ss"))));
				}
				else
				{
					if (chapterCount > 0) existing.TotalChapters = chapterCount;
					if (lastChapterAt.HasValue) existing.LastUpdated = lastChapterAt.Value;
				}
			}
		}

		public void OnNewChapter(long bookId, DbChapter chapter)
		{
			lock (_lock)
			{
				BookEntry? entry = Books.FirstOrDefault(b => b.Id == bookId.ToString());
				if (entry is null) return;
				entry.TotalChapters = (entry.TotalChapters ?? 0) + 1;
				entry.NewChapters   = (entry.NewChapters   ?? 0) + 1;
				entry.HasUnread     = true;
				if (DateTime.TryParse(chapter.ReleasedAt, out DateTime dt)) entry.LastUpdated = dt;
			}
		}

		public void SetNextExpected(long bookId, DateTime? next)
		{
			lock (_lock)
			{
				BookEntry? entry = Books.FirstOrDefault(b => b.Id == bookId.ToString());
				if (entry is not null) entry.NextExpected = next;
			}
		}

		public void UpsertUpcomingRelease(DbPendingRelease release)
		{
			lock (_lock)
			{
				int idx = UpcomingReleases.FindIndex(r => r.BookId == release.BookId);
				if (idx >= 0) UpcomingReleases[idx] = release;
				else          UpcomingReleases.Add(release);
			}
		}

		public void AddEvent(NotificationEvent evt)
		{
			lock (_lock)
			{
				Events.Insert(0, evt);
				if (Events.Count > 500) Events.RemoveRange(500, Events.Count - 500);
			}

			// Fire-and-forget: callers here are all sync and don't await AddEvent, and losing a rare
			// persist on a crash isn't worth making every one of them async. The in-memory list above
			// is what the UI actually reads from immediately either way. Skip in mock mode so demo
			// data never touches the real database.
			if (!IsMockMode) _ = PersistEventAsync(evt);
		}

		private async Task PersistEventAsync(NotificationEvent evt)
		{
			try { await db.AddEventAsync(evt); }
			catch { /* best-effort; in-memory copy already added */ }
		}

		public void SetPlatformStatus(Platform platform, bool enabled, bool running, string? error, int recheckMs = 0)
		{
			lock (_lock)
			{
				PlatformStatus? status = Platforms.FirstOrDefault(p => p.Platform == platform);
				if (status is null) return;
				status.IsEnabled    = enabled;
				status.IsRunning    = running;
				status.ErrorMessage = error;
				if (running)  status.LastCheck = DateTime.Now;
				if (recheckMs > 0) status.CheckInterval = TimeSpan.FromMilliseconds(recheckMs);
			}
		}

		public void SetPlatformRunning(Platform platform, bool running)
		{
			lock (_lock)
			{
				PlatformStatus? status = Platforms.FirstOrDefault(p => p.Platform == platform);
				if (status is null) return;
				status.IsRunning = running;
				if (!running) status.LastCheck = DateTime.Now;
			}
		}

		public void RefreshPlatformBooks(List<DbBookWithStats> freshRows, Platform platform)
		{
			lock (_lock)
			{
				Dictionary<string, DbBookWithStats> lookup = freshRows.ToDictionary(r => r.Book.Url);
				foreach (BookEntry entry in Books.Where(b => b.Platform == platform))
				{
					if (entry.Url is null || !lookup.TryGetValue(entry.Url, out DbBookWithStats? row)) continue;
					entry.TotalChapters = row.ChapterCount > 0 ? row.ChapterCount : entry.TotalChapters;
					if (row.LastChapterAt is not null && DateTime.TryParse(row.LastChapterAt, out DateTime dt))
						entry.LastUpdated = dt;
					lookup.Remove(entry.Url);
				}

				foreach ((string _, DbBookWithStats row) in lookup)
					Books.Add(MapToEntry(row));
			}
		}

		public async Task TriggerRecheckAsync(Platform platform)
		{
			SetPlatformRunning(platform, true);
			if (RecheckDelegate is not null) await RecheckDelegate(platform);
			else await Task.Delay(1500);
			SetPlatformRunning(platform, false);
		}

		private static BookEntry MapToEntry(DbBookWithStats row)
		{
			(DbBook book, int chapCount, string? lastAt) = row;
			DateTime lastUpdated = lastAt is not null && DateTime.TryParse(lastAt, out DateTime dt)
				? dt
				: DateTime.TryParse(book.FirstFoundAt, out DateTime ft) ? ft : DateTime.UtcNow;

			return new BookEntry
			{
				Id = book.Id.ToString(),
				Title = book.Title,
				Author = book.Author,
				Platform = PageLogic.GetPlatformFromString(book.Website),
				Url = book.Url,
				CoverUrl = book.CoverUrl,
				TotalChapters = chapCount > 0 ? chapCount : null,
				LastUpdated = lastUpdated,
				Status = ParseStatus(book.Status),
				NextExpected = book.ReleaseCycleDays is { } days && lastUpdated != default
					? lastUpdated.AddDays(days)
					: null,
				ReleaseCycleDays = book.ReleaseCycleDays,
				ReleaseWeekdayMask = book.WeeklyPatternMask,
			};
		}


		private static BookStatus ParseStatus(string status) => status.ToLowerInvariant() switch
		{
			"hiatus" => BookStatus.Hiatus,
			"completed" => BookStatus.Completed,
			"new" => BookStatus.New,
			_ => BookStatus.Ongoing
		};
	}
}
