namespace BookNotifier_Web.Models
{
	public enum Platform
	{
		GoodReads = 0,
		ScribbleHub = 1,
		RoyalRoad = 2,
		Literotica = 3,
		Ao3 = 4,
	}

	public enum BookStatus
	{
		Ongoing,
		Completed,
		Hiatus,
		New
	}

	public class BookEntry
	{
		public string Id { get; set; } = Guid.NewGuid().ToString();
		public string Title { get; set; } = string.Empty;
		public string Author { get; set; } = string.Empty;
		public Platform Platform { get; set; }
		public int? TotalChapters { get; set; }
		public int? NewChapters { get; set; }
		public DateTime LastUpdated { get; set; }
		public DateTime? NextExpected { get; set; }
		public double? ReleaseCycleDays { get; set; }
		public int? ReleaseWeekdayMask { get; set; }
		public BookStatus Status { get; set; }
		public string? Url { get; set; }
		public string? Genre { get; set; }
		public double? Rating { get; set; }
		public string? Description { get; set; }
		public bool HasUnread { get; set; }
		public string? CoverUrl { get; set; }
		public string? AmazonUrl { get; set; }
		public DateOnly? ReleaseDate { get; set; }
	}
}
