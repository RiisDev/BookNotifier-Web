using System.Text.RegularExpressions;
using BookNotifier.Utilities;

namespace BookNotifier_Web.Services
{
	// TODO: Amazon scraping logic below is unverified — the search-result link matching and the
	// "Publication date" extraction were written blind against typical Amazon markup, not a real
	// fetched page. Check both against a live product page and fix up before relying on this for
	// real release-date reminders.
	public class AmazonService
	{
		private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

		static AmazonService()
		{
			Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
				"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
		}

		public async Task<(string? AmazonUrl, DateOnly? ReleaseDate)> LookupAsync(string title, string author, CancellationToken ct = default)
		{
			string query = Uri.EscapeDataString($"{title} {author}".Trim());
			string searchUrl = $"https://www.amazon.com/s?k={query}&i=stripbooks";

			string searchHtml;
			try { searchHtml = await Http.GetStringAsync(searchUrl, ct); }
			catch { return (null, null); }

			HtmlDocument searchDoc = HtmlDocument.Parse(searchHtml);
			string? productPath = searchDoc.QuerySelectorAll("a[href]")
				.Select(a => a.GetAttribute("href"))
				.FirstOrDefault(href => href is not null && href.Contains("/dp/"));
			if (productPath is null) return (null, null);

			string productUrl = productPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
				? productPath
				: $"https://www.amazon.com{productPath}";

			string productHtml;
			try { productHtml = await Http.GetStringAsync(productUrl, ct); }
			catch { return (productUrl, null); }

			HtmlDocument productDoc = HtmlDocument.Parse(productHtml);
			HtmlElement? dateLabel = productDoc.All.FirstOrDefault(e =>
				e.TextContent.Trim().StartsWith("Publication date", StringComparison.OrdinalIgnoreCase));
			string? rowText = dateLabel?.Parent is HtmlElement row ? row.TextContent : dateLabel?.TextContent;

			DateOnly? releaseDate = null;
			if (rowText is not null)
			{
				Match m = Regex.Match(rowText, @"[A-Za-z]+ \d{1,2},? \d{4}");
				if (m.Success && DateOnly.TryParse(m.Value, out DateOnly parsed)) releaseDate = parsed;
			}

			return (productUrl, releaseDate);
		}
	}
}
