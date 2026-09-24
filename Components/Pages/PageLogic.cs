using BookNotifier_Web.Models;
using Microsoft.AspNetCore.Components;

namespace BookNotifier_Web.Components.Pages
{
	public sealed record PlatConfig(Platform Platform, string? ChapterFormat = null, bool DarkLetter = false, bool ShowHiatusBadge = true);

	public static class PageLogic
	{
		public static readonly (string, Func<IEnumerable<BookEntry>, string>)[] Stats =
		[
			("Tracked", books => books.Count().ToString()),
			("Total Chapters", books => books.Sum(b => b.TotalChapters ?? 0).ToString()),
			("With Updates", books => books.Count(b => b.HasUnread || b.NewChapters > 0).ToString()),
			("Avg Rating", Avg),
		];

		public static readonly (string, BookStatus?)[] Filters =
		[
			("All", null),
			("Ongoing", BookStatus.Ongoing),
			("New", BookStatus.New),
			("Completed", BookStatus.Completed),
		];

		public static readonly (string, string)[] Sorts =
		[
			("Last Updated", "updated"),
			("Most Chapters", "chapters"),
			("Title A–Z", "title"),
			("Rating", "rating"),
		];

		public static string Avg(IEnumerable<BookEntry> books)
		{
			List<BookEntry> rated = books.Where(b => b.Rating.HasValue).ToList();
			return rated.Count > 0 ? rated.Average(b => b.Rating!.Value).ToString("F1") : "—";
		}

		public static string RelTime(DateTime dt)
		{
			TimeSpan d = DateTime.Now - dt;
			if (d.TotalMinutes < 1) return "just now";
			if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
			if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
			if (d.TotalDays < 7) return $"{(int)d.TotalDays}d ago";
			return dt.ToString("MMM d");
		}

		public static string FmtInterval(TimeSpan ts) => ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h" : $"{(int)ts.TotalMinutes}m";

		// NextExpected is a single computed date, but a fast-cycling book (e.g. daily chapters) should
		// occupy every matching day in a calendar/week view, not just the next one. Walks the cycle
		// both backward (to catch occurrences already due before rangeStart) and forward through rangeEnd.
		public static List<DateTime> ProjectedReleaseDays(DateTime? nextExpected, double? cycleDays, DateTime rangeStart, DateTime rangeEnd, int? weekdayMask = null)
		{
			List<DateTime> days = [];
			if (!nextExpected.HasValue) return days;

			rangeStart = rangeStart.Date;
			rangeEnd = rangeEnd.Date;

			// A confirmed weekday pattern (Mon/Wed/Fri, etc.) doesn't have a single "gap" — alternating
			// 2/2/3-day gaps aren't reproducible by fixed-step arithmetic without overshooting onto days
			// the book never actually releases on. With the real weekday set, just match days directly.
			if (weekdayMask is > 0)
			{
				for (DateTime d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
					if ((weekdayMask.Value & (1 << (int)d.DayOfWeek)) != 0)
						days.Add(d);
				return days;
			}

			DateTime cur = nextExpected.Value.Date;
			if (cycleDays is not (> 0))
			{
				if (cur >= rangeStart && cur <= rangeEnd) days.Add(cur);
				return days;
			}

			double step = cycleDays.Value;
			int guard = 0;
			while (cur > rangeStart && guard++ < 400) cur = cur.AddDays(-step);
			while (cur < rangeStart && guard++ < 400) cur = cur.AddDays(step);
			while (cur <= rangeEnd && guard++ < 400)
			{
				days.Add(cur);
				cur = cur.AddDays(step);
			}
			return days;
		}

		public static string RelTimeFuture(DateTime dt)
		{
			TimeSpan d = dt - DateTime.Now;
			if (d.TotalMinutes < 1) return "any moment";
			if (d.TotalMinutes < 60) return $"in {(int)d.TotalMinutes}m";
			if (d.TotalHours < 24) return $"in {(int)d.TotalHours}h";
			if (d.TotalDays < 7) return $"in {(int)d.TotalDays}d";
			return $"on {dt:MMM d}";
		}

		public static string PlatKey(Platform p) => p switch
		{
			Platform.GoodReads => "goodreads",
			Platform.ScribbleHub => "scribblehub",
			Platform.RoyalRoad => "royalroad",
			Platform.Literotica => "literotica",
			Platform.Ao3 => "aothree",
			_ => ""
		};

		public static Func<BookEntry, object> SortFunc(string key) => key switch
		{
			"chapters" => b => -(b.TotalChapters ?? 0),
			"title" => b => b.Title,
			"author" => b => b.Author,
			"rating" => b => -(b.Rating ?? 0),
			_ => b => b.LastUpdated == default ? DateTime.MinValue : DateTime.MaxValue - b.LastUpdated
		};

		public static string LogLevelClass(string level) => level switch
		{
			"ERROR" => "log-error",
			"WARN" => "log-warn",
			_ => "log-info"
		};
		
		public static readonly Platform[] Platforms =
		[
			Platform.GoodReads,
			Platform.ScribbleHub,
			Platform.RoyalRoad,
			Platform.Literotica,
			Platform.Ao3,
		];

		public static Platform GetPlatformFromString(string website) => website.ToLowerInvariant() switch
		{
			"scribblehub" => Platform.ScribbleHub,
			"royalroad" => Platform.RoyalRoad,
			"literotica" => Platform.Literotica,
			"aothree" => Platform.Ao3,
			"goodreads" => Platform.GoodReads,
			_ => throw new InvalidOperationException($"Invalid website: {website}")
		};

		public static string PlatColor(Platform p) => p switch
		{
			Platform.GoodReads => "#e9722e",
			Platform.ScribbleHub => "#2196f3",
			Platform.RoyalRoad => "#f8c147",
			Platform.Literotica => "#e91e63",
			Platform.Ao3 => "#00cec9",
			_ => "#aaaaaa"
		};
		
		public static string TypeLabel(EventType t) => t switch
		{
			EventType.NewChapter => "Chapter",
			EventType.NewBook => "New Book",
			EventType.NewSeries => "Series",
			EventType.AuthorRelease => "Author",
			EventType.StatusChange => "Status",
			_ => t.ToString()
		};

		public static string TypeColor(EventType t) => t switch
		{
			EventType.NewChapter => "#00e676",
			EventType.NewBook => "#40c4ff",
			EventType.NewSeries => "#6c5ce7",
			EventType.AuthorRelease => "#ffab40",
			EventType.StatusChange => "#cf6679",
			_ => "#ffffff"
		};

		public static MarkupString TypeIcon(EventType t) => t switch
		{
			EventType.NewChapter => new MarkupString("&#x1F4D6;"),
			EventType.NewBook => new MarkupString("&#x1F4DA;"),
			EventType.NewSeries => new MarkupString("&#x2728;"),
			EventType.AuthorRelease => new MarkupString("&#x270D;"),
			EventType.StatusChange => new MarkupString("&#x1F504;"),
			_ => new MarkupString("&#x1F514;")
		};
		
		public static string PlatGradient(Platform p) => p switch
		{
			Platform.GoodReads => "linear-gradient(145deg,#e9722e,#b34e18)",
			Platform.ScribbleHub => "linear-gradient(145deg,#2196f3,#1565c0)",
			Platform.RoyalRoad => "linear-gradient(145deg,#f8c147,#c99a0a)",
			Platform.Literotica => "linear-gradient(145deg,#e91e63,#ad1457)",
			Platform.Ao3 => "linear-gradient(145deg,#00cec9,#007a76)",
			_ => "linear-gradient(145deg,#aaaaaa,#666666)"
		};

	}
}
