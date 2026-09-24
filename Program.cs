using BookNotifier_Web.Components;
using BookNotifier_Web.Data;
using BookNotifier_Web.Services;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "data", "keys")));

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<BookNotifierService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<AmazonService>();
// Registered as itself too (not just IHostedService) so the Setup page can inject the same
// instance to trigger a one-off run and read its live progress.
builder.Services.AddSingleton<CliRunnerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CliRunnerService>());
builder.Services.AddHostedService<ReleaseTrackerService>();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

DatabaseService db = app.Services.GetRequiredService<DatabaseService>();
SettingsService settings = app.Services.GetRequiredService<SettingsService>();
await db.InitializeAsync();
await settings.LoadAsync();
BookNotifierService bookService = app.Services.GetRequiredService<BookNotifierService>();
await bookService.InitializeAsync();

bool useMock = args.Contains("--mock");
#if DEBUG
useMock = true;
#endif
if (useMock) MockDataSeeder.Seed(bookService, settings);

app.Run();
