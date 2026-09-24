namespace BookNotifier_Web.Models
{
	public class PlatformStatus
	{
		public Platform Platform { get; set; }
		public bool IsEnabled { get; set; }
		public bool IsRunning { get; set; }
		public DateTime? LastCheck { get; set; }
		public DateTime? NextCheck { get; set; }
		public int TrackedItems { get; set; }
		public int NotificationsToday { get; set; }
		public string? ErrorMessage { get; set; }
		public TimeSpan CheckInterval { get; set; }
	}
}
