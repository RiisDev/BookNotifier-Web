namespace BookNotifier_Web.Data
{
	public record DbBook(
		long    Id,
		string  FirstFoundAt,
		string  Title,
		string  Url,
		string? CoverUrl,
		string  Author,
		string  Website,
		double? ReleaseCycleDays,
		string  Status = "ongoing",
		// Bitmask of DayOfWeek values (bit N = (int)DayOfWeek N) when a confirmed weekly release
		// pattern exists (e.g. Mon/Wed/Fri) — set separately via SetBookWeeklyPatternAsync, never
		// through UpsertBookAsync, so this always defaults to null there.
		int?    WeeklyPatternMask = null
	);

	public record DbChapter(
		long   Id,
		long   BookId,
		string ReleasedAt,
		string Title,
		string Url
	);

	public record DbLogEntry(
		long   Id,
		string Timestamp,
		string Level,
		string Caller,
		string Message
	);

	public record DbBookWithStats(
		DbBook  Book,
		int     ChapterCount,
		string? LastChapterAt
	);

	public record DbPendingRelease(
		long    Id,
		long    BookId,
		string  Title,
		string  BookUrl,
		string? AmazonUrl,
		string? ReleaseDate,
		bool    CalendarAdded,
		bool    Notified,
		string  CreatedAt
	);
}
