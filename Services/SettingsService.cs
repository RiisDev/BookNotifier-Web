using BookNotifier_Web.Data;
using BookNotifier_Web.Models;

namespace BookNotifier_Web.Services
{
	public class SettingsService(DatabaseService db)
	{
		private AppSettings _settings = new();

		public async Task LoadAsync() => _settings = await db.LoadSettingsAsync();

		public bool IsSetupComplete => _settings.SetupComplete;

		public AppSettings Clone() => _settings.Clone();

		public async Task SaveAsync(AppSettings settings)
		{
			await db.SaveSettingsAsync(settings);
			_settings = settings.Clone();
		}

		internal void LoadMock(AppSettings settings) => _settings = settings;
	}
}
