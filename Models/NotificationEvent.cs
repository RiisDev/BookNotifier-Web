namespace BookNotifier_Web.Models
{
	public enum EventType
	{
		NewChapter,
		NewBook,
		NewSeries,
		AuthorRelease,
		StatusChange,
		BookReleased,
	}

	public class NotificationEvent
	{
		public string Id { get; set; } = Guid.NewGuid().ToString();
		public DateTime Timestamp { get; set; } = DateTime.UtcNow;
		public Platform Platform { get; set; }
		public EventType Type { get; set; }
		public string Title { get; set; } = string.Empty;
		public string? Author { get; set; }
		public string? ChapterTitle { get; set; }
		public string Description { get; set; } = string.Empty;
		public string? Url { get; set; }
		public bool IsRead { get; set; }
	}
}
