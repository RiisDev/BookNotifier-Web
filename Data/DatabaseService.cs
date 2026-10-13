using BookNotifier_Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace BookNotifier_Web.Data
{
	public class DatabaseService(ILogger<DatabaseService> logger)
	{
		private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");
		public static string DbPath => Path.Combine(DataDir, "booknotifier.db");
		private static string ConnStr => $"Data Source={DbPath}";

		public async Task InitializeAsync()
		{
			Directory.CreateDirectory(DataDir);
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			foreach (string sql in InitStatements)
			{
				await using SqliteCommand cmd = conn.CreateCommand();
				cmd.CommandText = sql;
				await cmd.ExecuteNonQueryAsync();
			}
			await EnsureColumnAsync(conn, "books", "release_cycle_days", "REAL");
			await EnsureColumnAsync(conn, "books", "status", "TEXT NOT NULL DEFAULT 'ongoing'");
			await EnsureColumnAsync(conn, "books", "amazon_url", "TEXT");
			await EnsureColumnAsync(conn, "books", "weekly_pattern_mask", "INTEGER");
		}

		private static readonly string[] InitStatements =
		[
			"PRAGMA journal_mode=WAL;",
			"PRAGMA foreign_keys=ON;",
			"""
			CREATE TABLE IF NOT EXISTS settings (
			    key   TEXT NOT NULL PRIMARY KEY,
			    value TEXT NOT NULL DEFAULT ''
			);
			""",
			"""
			CREATE TABLE IF NOT EXISTS books (
			    id             INTEGER PRIMARY KEY AUTOINCREMENT,
			    first_found_at TEXT    NOT NULL,
			    title          TEXT    NOT NULL,
			    url            TEXT    NOT NULL UNIQUE,
			    cover_url      TEXT,
			    author         TEXT    NOT NULL DEFAULT '',
			    website        TEXT    NOT NULL
			);
			""",
			"CREATE INDEX IF NOT EXISTS idx_books_website ON books(website);",
			"""
			CREATE TABLE IF NOT EXISTS chapters (
			    id          INTEGER PRIMARY KEY AUTOINCREMENT,
			    book_id     INTEGER NOT NULL REFERENCES books(id) ON DELETE CASCADE,
			    released_at TEXT    NOT NULL,
			    title       TEXT    NOT NULL,
			    url         TEXT    NOT NULL UNIQUE
			);
			""",
			"CREATE INDEX IF NOT EXISTS idx_chapters_book ON chapters(book_id);",
			"""
			CREATE TABLE IF NOT EXISTS logs_goodreads (
			    id        INTEGER PRIMARY KEY AUTOINCREMENT,
			    timestamp TEXT    NOT NULL,
			    level     TEXT    NOT NULL DEFAULT 'INFO',
			    caller    TEXT    NOT NULL DEFAULT '',
			    message   TEXT    NOT NULL
			);
			""",
			"""
			CREATE TABLE IF NOT EXISTS logs_scribblehub (
			    id        INTEGER PRIMARY KEY AUTOINCREMENT,
			    timestamp TEXT    NOT NULL,
			    level     TEXT    NOT NULL DEFAULT 'INFO',
			    caller    TEXT    NOT NULL DEFAULT '',
			    message   TEXT    NOT NULL
			);
			""",
			"""
			CREATE TABLE IF NOT EXISTS logs_royalroad (
			    id        INTEGER PRIMARY KEY AUTOINCREMENT,
			    timestamp TEXT    NOT NULL,
			    level     TEXT    NOT NULL DEFAULT 'INFO',
			    caller    TEXT    NOT NULL DEFAULT '',
			    message   TEXT    NOT NULL
			);
			""",
			"""
			CREATE TABLE IF NOT EXISTS logs_literotica (
			    id        INTEGER PRIMARY KEY AUTOINCREMENT,
			    timestamp TEXT    NOT NULL,
			    level     TEXT    NOT NULL DEFAULT 'INFO',
			    caller    TEXT    NOT NULL DEFAULT '',
			    message   TEXT    NOT NULL
			);
			""",
			"""
			CREATE TABLE IF NOT EXISTS logs_ao3 (
			    id        INTEGER PRIMARY KEY AUTOINCREMENT,
			    timestamp TEXT    NOT NULL,
			    level     TEXT    NOT NULL DEFAULT 'INFO',
			    caller    TEXT    NOT NULL DEFAULT '',
			    message   TEXT    NOT NULL
			);
			""",
			"""
			CREATE TABLE IF NOT EXISTS logs_cli (
			    id        INTEGER PRIMARY KEY AUTOINCREMENT,
			    timestamp TEXT    NOT NULL,
			    level     TEXT    NOT NULL DEFAULT 'INFO',
			    caller    TEXT    NOT NULL DEFAULT '',
			    message   TEXT    NOT NULL
			);
			""",
			"""
			CREATE TABLE IF NOT EXISTS pending_releases (
			    id             INTEGER PRIMARY KEY AUTOINCREMENT,
			    book_id        INTEGER NOT NULL REFERENCES books(id) ON DELETE CASCADE,
			    title          TEXT    NOT NULL,
			    book_url       TEXT    NOT NULL,
			    amazon_url     TEXT,
			    release_date   TEXT,
			    calendar_added INTEGER NOT NULL DEFAULT 0,
			    notified       INTEGER NOT NULL DEFAULT 0,
			    created_at     TEXT    NOT NULL
			);
			""",
			"CREATE UNIQUE INDEX IF NOT EXISTS idx_pending_book ON pending_releases(book_id);",
			"""
			CREATE TABLE IF NOT EXISTS events (
			    id            TEXT    PRIMARY KEY,
			    timestamp     TEXT    NOT NULL,
			    platform      INTEGER NOT NULL,
			    type          INTEGER NOT NULL,
			    title         TEXT    NOT NULL,
			    author        TEXT,
			    chapter_title TEXT,
			    description   TEXT    NOT NULL DEFAULT '',
			    url           TEXT,
			    is_read       INTEGER NOT NULL DEFAULT 0
			);
			""",
			"CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events(timestamp DESC);",
		];

		public async Task<AppSettings> LoadSettingsAsync()
		{
			Dictionary<string, string> d = new();
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT key, value FROM settings;";
			await using SqliteDataReader rdr = await cmd.ExecuteReaderAsync();
			while (await rdr.ReadAsync())
				d[rdr.GetString(0)] = rdr.GetString(1);
			return MapToSettings(d);
		}

		public async Task SaveSettingsAsync(AppSettings s)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteTransaction tx = await conn.BeginTransactionAsync() as SqliteTransaction
			                                   ?? throw new InvalidOperationException("Could not begin transaction.");
			foreach ((string key, string value) in MapFromSettings(s))
			{
				await using SqliteCommand cmd = conn.CreateCommand();
				cmd.Transaction = tx;
				cmd.CommandText =
					"INSERT INTO settings(key,value) VALUES($k,$v) " +
					"ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
				cmd.Parameters.AddWithValue("$k", key);
				cmd.Parameters.AddWithValue("$v", value);
				await cmd.ExecuteNonQueryAsync();
			}
			await tx.CommitAsync();
		}

		public async Task<(long Id, bool IsNew)> UpsertBookAsync(DbBook book)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();

			await using SqliteCommand sel = conn.CreateCommand();
			sel.CommandText = "SELECT id FROM books WHERE url=$url;";
			sel.Parameters.AddWithValue("$url", book.Url);
			object? existingId = await sel.ExecuteScalarAsync();
			bool isNew = existingId is null;

			await using SqliteCommand upsert = conn.CreateCommand();
			upsert.CommandText = """
			                      INSERT INTO books(first_found_at, title, url, cover_url, author, website, status)
			                      VALUES($t,$title,$url,$cover,$author,$site,$status)
			                      ON CONFLICT(url) DO UPDATE SET
			                          title     = excluded.title,
			                          author    = excluded.author,
			                          cover_url = COALESCE(excluded.cover_url, books.cover_url),
			                          status    = excluded.status;
			                      """;
			upsert.Parameters.AddWithValue("$t",      book.FirstFoundAt);
			upsert.Parameters.AddWithValue("$title",  book.Title);
			upsert.Parameters.AddWithValue("$url",    book.Url);
			upsert.Parameters.AddWithValue("$cover",  book.CoverUrl ?? (object)DBNull.Value);
			upsert.Parameters.AddWithValue("$author", book.Author);
			upsert.Parameters.AddWithValue("$site",   book.Website);
			upsert.Parameters.AddWithValue("$status", book.Status);
			await upsert.ExecuteNonQueryAsync();

			if (isNew)
			{
				await using SqliteCommand selNew = conn.CreateCommand();
				selNew.CommandText = "SELECT id FROM books WHERE url=$url;";
				selNew.Parameters.AddWithValue("$url", book.Url);
				existingId = await selNew.ExecuteScalarAsync();
			}

			return (Convert.ToInt64(existingId), isNew);
		}

		public async Task UpdateBookStatusAsync(long id, string status)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = "UPDATE books SET status=$s WHERE id=$id;";
			cmd.Parameters.AddWithValue("$s",  status);
			cmd.Parameters.AddWithValue("$id", id);
			await cmd.ExecuteNonQueryAsync();
		}

		public async Task<List<DbBookWithStats>> GetBooksWithStatsAsync(string? website = null)
		{
			List<DbBookWithStats> list = [];
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			string where = website is null ? "" : "WHERE b.website=$w ";
			cmd.CommandText = $"""
			                   SELECT b.id, b.first_found_at, b.title, b.url, b.cover_url, b.author, b.website,
			                          b.release_cycle_days, b.status, b.weekly_pattern_mask,
			                          COUNT(c.id) AS chapter_count,
			                          MAX(c.released_at) AS last_chapter_at
			                   FROM books b
			                   LEFT JOIN chapters c ON c.book_id = b.id
			                   {where}
			                   GROUP BY b.id
			                   ORDER BY COALESCE(MAX(c.released_at), b.first_found_at) DESC;
			                   """;
			if (website is not null) cmd.Parameters.AddWithValue("$w", website);
			await using SqliteDataReader rdr = await cmd.ExecuteReaderAsync();
			while (await rdr.ReadAsync())
			{
				DbBook book = ReadBook(rdr);
				int chapCount  = rdr.GetInt32(10);
				string? lastAt = rdr.IsDBNull(11) ? null : rdr.GetString(11);
				list.Add(new DbBookWithStats(book, chapCount, lastAt));
			}
			return list;
		}

		public async Task<List<DbBook>> GetBooksAsync(string? website = null)
		{
			List<DbBook> list = [];
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = website is null
				? "SELECT id, first_found_at, title, url, cover_url, author, website, release_cycle_days, status, weekly_pattern_mask FROM books ORDER BY first_found_at DESC;"
				: "SELECT id, first_found_at, title, url, cover_url, author, website, release_cycle_days, status, weekly_pattern_mask FROM books WHERE website=$w ORDER BY first_found_at DESC;";
			if (website is not null) cmd.Parameters.AddWithValue("$w", website);
			await using SqliteDataReader rdr = await cmd.ExecuteReaderAsync();
			while (await rdr.ReadAsync())
				list.Add(ReadBook(rdr));
			return list;
		}

		public async Task<DbBook?> GetBookByIdAsync(long id)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText =
				"SELECT id, first_found_at, title, url, cover_url, author, website, release_cycle_days, status, weekly_pattern_mask FROM books WHERE id=$id LIMIT 1;";
			cmd.Parameters.AddWithValue("$id", id);
			await using SqliteDataReader rdr = await cmd.ExecuteReaderAsync();
			return await rdr.ReadAsync() ? ReadBook(rdr) : null;
		}

		public async Task<DbBook?> GetBookByUrlAsync(string url)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText =
				"SELECT id, first_found_at, title, url, cover_url, author, website, release_cycle_days, status, weekly_pattern_mask FROM books WHERE url=$url LIMIT 1;";
			cmd.Parameters.AddWithValue("$url", url);
			await using SqliteDataReader rdr = await cmd.ExecuteReaderAsync();
			return await rdr.ReadAsync() ? ReadBook(rdr) : null;
		}

		public async Task SetBookCycleAsync(long bookId, double? cycleDays)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = "UPDATE books SET release_cycle_days=$days WHERE id=$id;";
			cmd.Parameters.AddWithValue("$days", cycleDays.HasValue ? cycleDays.Value : DBNull.Value);
			cmd.Parameters.AddWithValue("$id",   bookId);
			await cmd.ExecuteNonQueryAsync();
		}

		public async Task SetBookWeeklyPatternAsync(long bookId, int? weekdayMask)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = "UPDATE books SET weekly_pattern_mask=$mask WHERE id=$id;";
			cmd.Parameters.AddWithValue("$mask", weekdayMask.HasValue ? weekdayMask.Value : DBNull.Value);
			cmd.Parameters.AddWithValue("$id",   bookId);
			await cmd.ExecuteNonQueryAsync();
		}

		public async Task<DateTime?> GetEstimatedNextAsync(long bookId)
		{
			DbBook? book     = await GetBookByIdAsync(bookId);
			List<DbChapter> chapters = await GetChaptersAsync(bookId);
			List<DateTime> dates = chapters
				.Take(4)
				.Select(c => DateTime.TryParse(c.ReleasedAt, out DateTime d) ? d : (DateTime?)null)
				.Where(d => d.HasValue)
				.Select(d => d!.Value)
				.OrderBy(d => d)
				.ToList();
			return EstimateFromHistory(dates, book?.ReleaseCycleDays);
		}

		public static DateTime? EstimateFromHistory(
			IReadOnlyList<DateTime> ascendingDates, double? manualCycleDays = null)
		{
			if (ascendingDates.Count == 0) return null;
			DateTime last = ascendingDates[^1];
			if (manualCycleDays is { } days) return last.AddDays(days);
			if (ascendingDates.Count < 2) return null;

			// A fixed weekday schedule (e.g. Mon/Wed/Fri) has alternating gaps (2, 2, 3 days) — a flat
			// gap-based estimate after a Friday release lands on Sunday (2 days later) and falsely
			// flags Sat/Sun as overdue for the real Monday release. Check for a weekday pattern first.
			if (TryEstimateWeeklyPattern(ascendingDates, out DateTime weeklyNext, out _)) return weeklyNext;

			// Median, not mean — a book's early history often has a burst (author posts several
			// chapters at once when launching, or a backfill scrape catches up several at nearly the
			// same timestamp). A mean over those gaps drags the estimate well below the book's actual
			// current cadence; a median shrugs off a minority of outlier gaps.
			List<long> gapTicks = Enumerable
				.Range(1, ascendingDates.Count - 1)
				.Select(i => (ascendingDates[i] - ascendingDates[i - 1]).Ticks)
				.OrderBy(t => t)
				.ToList();
			long medianTicks = gapTicks.Count % 2 == 1
				? gapTicks[gapTicks.Count / 2]
				: (gapTicks[gapTicks.Count / 2 - 1] + gapTicks[gapTicks.Count / 2]) / 2;

			return last + TimeSpan.FromTicks(medianTicks);
		}

		// Detects "releases fall on a fixed set of weekdays" (Mon/Wed/Fri, every Sunday, etc.) and,
		// if confirmed, predicts the next date on one of those weekdays instead of a flat day-gap.
		// Also outputs the weekday set as a bitmask (bit N = (int)DayOfWeek N) so callers can persist
		// it for calendar-grid projection, which needs the actual pattern, not just a single next date.
		public static bool TryEstimateWeeklyPattern(IReadOnlyList<DateTime> ascendingDates, out DateTime next, out int? weekdayMask)
		{
			next = default;
			weekdayMask = null;

			// One entry per calendar day: a same-day burst (backfill/launch dump) must not look like
			// "always releases on this one weekday", and day-bucketing is what makes the recurrence
			// count below meaningful.
			List<DateTime> distinctDays = ascendingDates.Select(d => d.Date).Distinct().OrderBy(d => d).ToList();
			if (distinctDays.Count < 6) return false; // need a few weeks of signal before trusting a pattern

			List<DateTime> recent = distinctDays.TakeLast(21).ToList(); // ~3 weeks
			Dictionary<DayOfWeek, int> counts = recent.GroupBy(d => d.DayOfWeek).ToDictionary(g => g.Key, g => g.Count());

			// A weekday only counts as part of the schedule if it recurs — a single bonus/one-off
			// chapter on an off day (common: authors post an extra chapter for a milestone, a poll win,
			// a patron goal) must not poison detection of the otherwise-consistent schedule. Filter
			// those out rather than rejecting the whole pattern because of them.
			Dictionary<DayOfWeek, int> reliable = counts.Where(kv => kv.Value >= 2).ToDictionary(kv => kv.Key, kv => kv.Value);

			// 6-7 distinct weekdays is just "releases often" (daily), not a fixed schedule. And the
			// reliable weekdays must explain most of what we're seeing — if bonus/irregular days make
			// up the bulk of the window, this isn't "a schedule with occasional extras", it's noise.
			if (reliable.Count is 0 or > 5) return false;
			if (reliable.Values.Sum() < recent.Count * 0.6) return false;

			DateTime lastDay = distinctDays[^1];
			TimeSpan timeOfDay = ascendingDates[^1].TimeOfDay;
			for (int i = 1; i <= 14; i++)
			{
				DateTime candidate = lastDay.AddDays(i);
				if (!reliable.ContainsKey(candidate.DayOfWeek)) continue;
				next = candidate + timeOfDay;
				weekdayMask = reliable.Keys.Aggregate(0, (mask, dow) => mask | (1 << (int)dow));
				return true;
			}
			return false;
		}

		public async Task<bool> UpsertChapterAsync(DbChapter chapter)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = """
			                  INSERT OR IGNORE INTO chapters(book_id, released_at, title, url)
			                  VALUES($bid, $t, $title, $url);
			                  """;
			cmd.Parameters.AddWithValue("$bid",   chapter.BookId);
			cmd.Parameters.AddWithValue("$t",     chapter.ReleasedAt);
			cmd.Parameters.AddWithValue("$title", chapter.Title);
			cmd.Parameters.AddWithValue("$url",   chapter.Url);
			return await cmd.ExecuteNonQueryAsync() > 0;
		}

		public async Task InsertChapterAsync(DbChapter chapter) => await UpsertChapterAsync(chapter);

		public async Task<List<DbChapter>> GetChaptersAsync(long bookId)
		{
			List<DbChapter> list = [];
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText =
				"SELECT id, book_id, released_at, title, url FROM chapters WHERE book_id=$bid ORDER BY released_at DESC;";
			cmd.Parameters.AddWithValue("$bid", bookId);
			await using SqliteDataReader rdr = await cmd.ExecuteReaderAsync();
			while (await rdr.ReadAsync())
				list.Add(new DbChapter(
					rdr.GetInt64(0), rdr.GetInt64(1),
					rdr.GetString(2), rdr.GetString(3), rdr.GetString(4)
				));
			return list;
		}

		public async Task SetBookAmazonUrlAsync(long bookId, string amazonUrl)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = "UPDATE books SET amazon_url=$u WHERE id=$id;";
			cmd.Parameters.AddWithValue("$u",  amazonUrl);
			cmd.Parameters.AddWithValue("$id", bookId);
			await cmd.ExecuteNonQueryAsync();
		}

		public async Task<bool> UpsertPendingReleaseAsync(DbPendingRelease r)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = """
			                  INSERT INTO pending_releases
			                      (book_id, title, book_url, amazon_url, release_date, calendar_added, notified, created_at)
			                  VALUES ($bid,$title,$burl,$amazon,$date,0,0,$created)
			                  ON CONFLICT(book_id) DO UPDATE SET
			                      amazon_url   = excluded.amazon_url,
			                      release_date = excluded.release_date;
			                  """;
			cmd.Parameters.AddWithValue("$bid",     r.BookId);
			cmd.Parameters.AddWithValue("$title",   r.Title);
			cmd.Parameters.AddWithValue("$burl",    r.BookUrl);
			cmd.Parameters.AddWithValue("$amazon",  r.AmazonUrl ?? (object)DBNull.Value);
			cmd.Parameters.AddWithValue("$date",    r.ReleaseDate ?? (object)DBNull.Value);
			cmd.Parameters.AddWithValue("$created", r.CreatedAt);
			return await cmd.ExecuteNonQueryAsync() > 0;
		}

		public async Task<List<DbPendingRelease>> GetPendingReleasesAsync(bool notifiedOnly = false)
		{
			List<DbPendingRelease> list = [];
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			string where = notifiedOnly ? "WHERE notified=0" : "";
			cmd.CommandText =
				$"SELECT id,book_id,title,book_url,amazon_url,release_date,calendar_added,notified,created_at " +
				$"FROM pending_releases {where} ORDER BY release_date ASC;";
			await using SqliteDataReader rdr = await cmd.ExecuteReaderAsync();
			while (await rdr.ReadAsync())
				list.Add(ReadPendingRelease(rdr));
			return list;
		}

		public async Task MarkReleaseNotifiedAsync(long id)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = "UPDATE pending_releases SET notified=1 WHERE id=$id;";
			cmd.Parameters.AddWithValue("$id", id);
			await cmd.ExecuteNonQueryAsync();
		}

		public async Task MarkCalendarAddedAsync(long id)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = "UPDATE pending_releases SET calendar_added=1 WHERE id=$id;";
			cmd.Parameters.AddWithValue("$id", id);
			await cmd.ExecuteNonQueryAsync();
		}

		// Activity feed persistence — this used to be in-memory only, so a container restart
		// silently wiped it. Trims to the newest 500 rows on every insert to match the in-memory cap.
		public async Task AddEventAsync(NotificationEvent evt)
		{
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText = """
			                  INSERT OR IGNORE INTO events(id, timestamp, platform, type, title, author, chapter_title, description, url, is_read)
			                  VALUES ($id,$ts,$plat,$type,$title,$author,$chap,$desc,$url,$read);
			                  """;
			cmd.Parameters.AddWithValue("$id",     evt.Id);
			cmd.Parameters.AddWithValue("$ts",     evt.Timestamp.ToString("o"));
			cmd.Parameters.AddWithValue("$plat",   (int)evt.Platform);
			cmd.Parameters.AddWithValue("$type",   (int)evt.Type);
			cmd.Parameters.AddWithValue("$title",  evt.Title);
			cmd.Parameters.AddWithValue("$author", evt.Author ?? (object)DBNull.Value);
			cmd.Parameters.AddWithValue("$chap",   evt.ChapterTitle ?? (object)DBNull.Value);
			cmd.Parameters.AddWithValue("$desc",   evt.Description);
			cmd.Parameters.AddWithValue("$url",    evt.Url ?? (object)DBNull.Value);
			cmd.Parameters.AddWithValue("$read",   evt.IsRead ? 1 : 0);
			await cmd.ExecuteNonQueryAsync();

			await using SqliteCommand trim = conn.CreateCommand();
			trim.CommandText = "DELETE FROM events WHERE id NOT IN (SELECT id FROM events ORDER BY timestamp DESC LIMIT 500);";
			await trim.ExecuteNonQueryAsync();
		}

		public async Task<List<NotificationEvent>> GetRecentEventsAsync(int limit = 500)
		{
			List<NotificationEvent> list = [];
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText =
				"SELECT id, timestamp, platform, type, title, author, chapter_title, description, url, is_read " +
				"FROM events ORDER BY timestamp DESC LIMIT $lim;";
			cmd.Parameters.AddWithValue("$lim", limit);
			await using SqliteDataReader rdr = await cmd.ExecuteReaderAsync();
			while (await rdr.ReadAsync())
			{
				list.Add(new NotificationEvent
				{
					Id           = rdr.GetString(0),
					Timestamp    = DateTime.Parse(rdr.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
					Platform     = (Platform)rdr.GetInt32(2),
					Type         = (EventType)rdr.GetInt32(3),
					Title        = rdr.GetString(4),
					Author       = rdr.IsDBNull(5) ? null : rdr.GetString(5),
					ChapterTitle = rdr.IsDBNull(6) ? null : rdr.GetString(6),
					Description  = rdr.GetString(7),
					Url          = rdr.IsDBNull(8) ? null : rdr.GetString(8),
					IsRead       = rdr.GetInt32(9) != 0,
				});
			}
			return list;
		}

		private static DbPendingRelease ReadPendingRelease(SqliteDataReader r) => new(
			r.GetInt64(0),
			r.GetInt64(1),
			r.GetString(2),
			r.GetString(3),
			r.IsDBNull(4) ? null : r.GetString(4),
			r.IsDBNull(5) ? null : r.GetString(5),
			r.GetInt32(6) != 0,
			r.GetInt32(7) != 0,
			r.GetString(8)
		);

		public async Task AddLogAsync(string platform, string level, string caller, string message)
		{
			LogLevel logLevel = level.ToUpperInvariant() switch
			{
				"ERROR" => LogLevel.Error,
				"WARN"  => LogLevel.Warning,
				_       => LogLevel.Information,
			};
			logger.Log(logLevel, "[{Platform}] {Caller}: {Message}", platform, caller, message);

			string table = LogTable(platform);
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText =
				$"INSERT INTO {table}(timestamp, level, caller, message) VALUES($ts,$lvl,$caller,$msg);";
			cmd.Parameters.AddWithValue("$ts",     DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
			cmd.Parameters.AddWithValue("$lvl",    level.ToUpper());
			cmd.Parameters.AddWithValue("$caller", caller);
			cmd.Parameters.AddWithValue("$msg",    message);
			await cmd.ExecuteNonQueryAsync();
		}

		public async Task<List<DbLogEntry>> GetLogsAsync(string platform, int limit = 200)
		{
			string table = LogTable(platform);
			List<DbLogEntry> list  = [];
			await using SqliteConnection conn = new(ConnStr);
			await conn.OpenAsync();
			await using SqliteCommand cmd = conn.CreateCommand();
			cmd.CommandText =
				$"SELECT id, timestamp, level, caller, message FROM {table} ORDER BY id DESC LIMIT $lim;";
			cmd.Parameters.AddWithValue("$lim", limit);
			await using SqliteDataReader rdr = await cmd.ExecuteReaderAsync();
			while (await rdr.ReadAsync())
				list.Add(new DbLogEntry(
					rdr.GetInt64(0), rdr.GetString(1),
					rdr.GetString(2), rdr.GetString(3), rdr.GetString(4)
				));
			return list;
		}

		private static DbBook ReadBook(SqliteDataReader rdr) => new(
			rdr.GetInt64(0),
			rdr.GetString(1),
			rdr.GetString(2),
			rdr.GetString(3),
			rdr.IsDBNull(4) ? null : rdr.GetString(4),
			rdr.GetString(5),
			rdr.GetString(6),
			rdr.IsDBNull(7) ? null : rdr.GetDouble(7),
			rdr.IsDBNull(8) ? "ongoing" : rdr.GetString(8),
			rdr.IsDBNull(9) ? null : rdr.GetInt32(9)
		);

		private static async Task EnsureColumnAsync(
			SqliteConnection conn, string table, string column, string type)
		{
			await using SqliteCommand check = conn.CreateCommand();
			check.CommandText =
				$"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
			bool exists = Convert.ToInt64(await check.ExecuteScalarAsync()) > 0;
			if (!exists)
			{
				await using SqliteCommand alter = conn.CreateCommand();
				alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
				await alter.ExecuteNonQueryAsync();
			}
		}

		// Wipes every table (settings included) so the app comes back up as a fresh install.
		// The BookNotifier CLI's own JSON caches are untouched — that's fine, since ingestion keys
		// "is this new" off our books/chapters tables, not the CLI's cache, so everything it knows
		// about will look new to us again and get re-ingested on the next pass.
		public async Task ResetAllAsync()
		{
			string[] tables =
			[
				"settings", "books", "chapters", "pending_releases", "events",
				"logs_goodreads", "logs_scribblehub", "logs_royalroad", "logs_literotica", "logs_ao3", "logs_cli",
			];

			await using (SqliteConnection conn = new(ConnStr))
			{
				await conn.OpenAsync();
				await using SqliteTransaction tx = await conn.BeginTransactionAsync() as SqliteTransaction
				                                   ?? throw new InvalidOperationException("Could not begin transaction.");
				foreach (string table in tables)
				{
					await using SqliteCommand cmd = conn.CreateCommand();
					cmd.Transaction = tx;
					cmd.CommandText = $"DELETE FROM {table};";
					await cmd.ExecuteNonQueryAsync();
				}
				await tx.CommitAsync();
			}

			await using SqliteConnection vacuumConn = new(ConnStr);
			await vacuumConn.OpenAsync();
			await using SqliteCommand vacuum = vacuumConn.CreateCommand();
			vacuum.CommandText = "VACUUM;";
			await vacuum.ExecuteNonQueryAsync();
		}

		private static string LogTable(string platform) => platform.ToLowerInvariant() switch
		{
			"goodreads"   => "logs_goodreads",
			"scribblehub" => "logs_scribblehub",
			"royalroad"   => "logs_royalroad",
			"literotica"  => "logs_literotica",
			"aothree"     => "logs_ao3",
			"cli"         => "logs_cli",
			_ => throw new ArgumentException($"Unknown platform: {platform}")
		};

		private static AppSettings MapToSettings(Dictionary<string, string> d) => new()
		{
			SetupComplete           = d.GetValueOrDefault("setup_complete", "") == "true"
			                       || !string.IsNullOrWhiteSpace(d.GetValueOrDefault("webhook", "")),
			EnabledNotifiers        = d.GetValueOrDefault("notifier", "")
				.Split(',', StringSplitOptions.RemoveEmptyEntries)
				.ToList(),
			WebhookUrl              = d.GetValueOrDefault("webhook",                   ""),
			FlareSolverUrl          = d.GetValueOrDefault("flaresolver_url",           ""),
			GoodReadsRecheckMs      = int.TryParse(d.GetValueOrDefault("goodreads_recheck_ms"),   out int gr)  ? gr  : 300000,
			GoodReadsUserId         = d.GetValueOrDefault("goodreads_user_id",         ""),
			GoodReadsShelfTag       = d.GetValueOrDefault("goodreads_shelf_tag",       "to-read"),
			ScribbleHubRecheckMs    = int.TryParse(d.GetValueOrDefault("scribblehub_recheck_ms"), out int sh)  ? sh  : 60000,
			ScribbleHubUserId       = d.GetValueOrDefault("scribblehub_userid",         ""),
			LiteroticaRecheckMs     = int.TryParse(d.GetValueOrDefault("literotica_recheck_ms"),  out int lit) ? lit : 600000,
			LiteroticaUsername      = d.GetValueOrDefault("literotica_username",       ""),
			LiteroticaPassword      = d.GetValueOrDefault("literotica_password",       ""),
			RoyalRoadRecheckMs      = int.TryParse(d.GetValueOrDefault("royalroad_recheck_ms"),   out int rr)  ? rr  : 300000,
			RoyalRoadUserId         = d.GetValueOrDefault("royalroad_userid",          ""),
			Ao3RecheckMs            = int.TryParse(d.GetValueOrDefault("aothree_recheck_ms"), out int ao3) ? ao3 : 3_600_000,
			Ao3Username             = d.GetValueOrDefault("aothree_username",   ""),
			Ao3Pseudoname           = d.GetValueOrDefault("aothree_pseudoname", ""),
			AmazonCheckEnabled      = d.GetValueOrDefault("amazon_check_enabled",      "") == "true",
			CalendarEnabled         = d.GetValueOrDefault("calendar_enabled",          "") == "true",
		};

		private static Dictionary<string, string> MapFromSettings(AppSettings s) => new()
		{
			["setup_complete"]            = s.SetupComplete ? "true" : "false",
			["notifier"]                  = string.Join(",", s.EnabledNotifiers),
			["webhook"]                   = s.WebhookUrl,
			["flaresolver_url"]           = s.FlareSolverUrl,
			["goodreads_recheck_ms"]      = s.GoodReadsRecheckMs.ToString(),
			["goodreads_user_id"]         = s.GoodReadsUserId,
			["goodreads_shelf_tag"]       = s.GoodReadsShelfTag,
			["scribblehub_recheck_ms"]    = s.ScribbleHubRecheckMs.ToString(),
			["scribblehub_userid"]        = s.ScribbleHubUserId,
			["literotica_recheck_ms"]     = s.LiteroticaRecheckMs.ToString(),
			["literotica_username"]       = s.LiteroticaUsername,
			["literotica_password"]       = s.LiteroticaPassword,
			["royalroad_recheck_ms"]      = s.RoyalRoadRecheckMs.ToString(),
			["royalroad_userid"]          = s.RoyalRoadUserId,
			["aothree_recheck_ms"]        = s.Ao3RecheckMs.ToString(),
			["aothree_username"]          = s.Ao3Username,
			["aothree_pseudoname"]        = s.Ao3Pseudoname,
			["amazon_check_enabled"]      = s.AmazonCheckEnabled ? "true" : "false",
			["calendar_enabled"]          = s.CalendarEnabled    ? "true" : "false",
		};
	}
}
