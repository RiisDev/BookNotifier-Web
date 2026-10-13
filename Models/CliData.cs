namespace BookNotifier_Web.Models
{
	// Shapes of the JSON caches written by the BookNotifier CLI (RiisDev/BookNotifier) into its own
	// <exe-dir>/data/*.json. Duplicated here (not shared via project reference) because the CLI is a
	// separate process/repo we only talk to over its file-based contract.
	public record CliGoodReadsBook(string Title, string AuthorName, string Url, string? SeriesName, double? SeriesPosition,
		DateTime? PublishedAt = null, string? CoverUrl = null, string Status = "unknown");

	public record CliChapter(string Title, string Link, string Id, DateTime? ReleasedAt = null);
	public record CliScribbleBook(string Name, string Link, string Id, List<CliChapter> Chapters,
		string? CoverUrl = null, string Status = "unknown");

	public record CliBareChapter(string Title, string Url, DateTime? ReleasedAt = null);
	public record CliRoyalRoadBook(string Title, string Url, List<CliBareChapter> Chapters,
		string? CoverUrl = null, string Status = "unknown");

	public record CliAo3Book(string WorkId, string Title, string Url, string Author, List<CliBareChapter> Chapters,
		string Status = "unknown");

	public record CliLiteroticaChapter(string Title, string Url, DateTime? PublishedAt = null);
	public record CliLiteroticaWork(string Title, string Url, string Author, List<CliLiteroticaChapter> Chapters,
		string Status = "unknown");
	public record CliLiteroticaData(HashSet<string> Authors, List<CliLiteroticaWork> Works);
}
