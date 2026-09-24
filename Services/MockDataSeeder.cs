using BookNotifier_Web.Data;
using BookNotifier_Web.Models;

namespace BookNotifier_Web.Services
{
    /// <summary>
    /// Populates in-memory services with realistic fake data for UI development.
    /// Activated via --mock arg or automatically in DEBUG builds.
    /// No DB writes except for Settings (which uses LoadMock, also in-memory only).
    /// </summary>
    internal static class MockDataSeeder
    {
        public static void Seed(BookNotifierService bookService, SettingsService settingsService)
        {
            bookService.EnableMockMode();
            settingsService.LoadMock(MockSettings());
            SeedPlatforms(bookService);
            SeedBooks(bookService);
            SeedEvents(bookService);
            SeedReleases(bookService);
        }

        // ── Settings ──────────────────────────────────────────────────────────────

        private static AppSettings MockSettings() => new()
        {
            SetupComplete      = true,
            EnabledNotifiers   = ["discord"],
            WebhookUrl         = "https://discord.com/api/webhooks/mock/token",
            FlareSolverUrl     = "http://localhost:8191/",
            GoodReadsUserId    = "123456789",
            GoodReadsShelfTag  = "to-read",
            ScribbleHubUserId   = "123456",
            LiteroticaUsername  = "mockuser",
            RoyalRoadUserId     = "987654",
            Ao3Username         = "mockuser",
            Ao3Pseudoname       = "",
            AmazonCheckEnabled  = true,
            CalendarEnabled     = true,
        };

        // ── Platform statuses ─────────────────────────────────────────────────────

        private static void SeedPlatforms(BookNotifierService svc)
        {
            SetPlat(svc, Platform.GoodReads,  running: false, trackedItems: 3, notifsToday: 1,
                lastCheck: DateTime.Now.AddMinutes(-7),  interval: TimeSpan.FromMinutes(5),  error: null);
            SetPlat(svc, Platform.ScribbleHub, running: true,  trackedItems: 4, notifsToday: 3,
                lastCheck: DateTime.Now.AddMinutes(-2),  interval: TimeSpan.FromMinutes(1),  error: null);
            SetPlat(svc, Platform.RoyalRoad,   running: false, trackedItems: 4, notifsToday: 2,
                lastCheck: DateTime.Now.AddMinutes(-18), interval: TimeSpan.FromMinutes(5),  error: null);
            SetPlat(svc, Platform.Literotica,  running: false, trackedItems: 3, notifsToday: 0,
                lastCheck: DateTime.Now.AddMinutes(-62), interval: TimeSpan.FromMinutes(10), error: "Rate limited — retrying in 8 min");
            SetPlat(svc, Platform.Ao3,         running: false, trackedItems: 4, notifsToday: 1,
                lastCheck: DateTime.Now.AddMinutes(-31), interval: TimeSpan.FromHours(1),    error: null);
        }

        private static void SetPlat(BookNotifierService svc, Platform p, bool running, int trackedItems,
            int notifsToday, DateTime lastCheck, TimeSpan interval, string? error)
        {
            svc.SetPlatformStatus(p, enabled: true, running: running, error: error, recheckMs: (int)interval.TotalMilliseconds);
            PlatformStatus? status = svc.Platforms.FirstOrDefault(x => x.Platform == p);
            if (status is null) return;
            status.LastCheck          = lastCheck;
            status.NextCheck          = running ? null : lastCheck + interval;
            status.TrackedItems       = trackedItems;
            status.NotificationsToday = notifsToday;
        }

        // ── Books ─────────────────────────────────────────────────────────────────

        private static void SeedBooks(BookNotifierService svc)
        {
            DateTime D(int daysAgo) => DateTime.UtcNow.AddDays(-daysAgo);

            DateTime T(int daysFromNow) => DateTime.Today.AddDays(daysFromNow);

            // GoodReads — physical books, release dates drive NextExpected via releaseDate→nextExpected passthrough
            Add(svc, 1001, "The Midnight Archive",       "Iris Calloway",    Platform.GoodReads,  BookStatus.Ongoing,    null, 0, D(1),  "https://www.goodreads.com/book/show/1001",  null,
                amazonUrl: "https://amazon.com/dp/MOCK001", releaseDate: DateOnly.FromDateTime(T(14)));
            Add(svc, 1002, "Shattered Kingdoms",         "Marcus Vex",       Platform.GoodReads,  BookStatus.Completed,  null, 0, D(5),  "https://www.goodreads.com/book/show/1002",  null);
            Add(svc, 1003, "Echo of the Forgotten",      "L. Chen",          Platform.GoodReads,  BookStatus.Hiatus,     null, 0, D(12), "https://www.goodreads.com/book/show/1003",  null,
                releaseDate: DateOnly.FromDateTime(T(62)));

            // ScribbleHub — web novels, cadence-based NextExpected
            Add(svc, 2001, "Dungeon Core: Reborn",       "MidnightInk",      Platform.ScribbleHub, BookStatus.Ongoing,   342, 5, D(0),  "https://www.scribblehub.com/series/2001/", null,  hasUnread: true,  nextExpected: T(2));
            Add(svc, 2002, "The Villainess Lives Twice", "PearlQuill",       Platform.ScribbleHub, BookStatus.Ongoing,   189, 2, D(1),  "https://www.scribblehub.com/series/2002/", null,  hasUnread: true,  nextExpected: T(5));
            Add(svc, 2003, "System Error: Null Pointer", "CodexScribe",      Platform.ScribbleHub, BookStatus.Completed, 520, 0, D(30), "https://www.scribblehub.com/series/2003/", null);
            Add(svc, 2004, "Threads of Fate",            "Wisteria",         Platform.ScribbleHub, BookStatus.New,        12, 12, D(2), "https://www.scribblehub.com/series/2004/", null,  hasUnread: true,  nextExpected: T(3));

            // RoyalRoad — progression fantasy
            Add(svc, 3001, "Pale Defender",              "J. Kross",         Platform.RoyalRoad,   BookStatus.Ongoing,   234, 1, D(1),  "https://www.royalroad.com/fiction/3001",   null,  hasUnread: true,  nextExpected: T(4));
            Add(svc, 3002, "The Last Artificer",         "Nora Vane",        Platform.RoyalRoad,   BookStatus.Ongoing,    87, 3, D(0),  "https://www.royalroad.com/fiction/3002",   null,  hasUnread: true,  nextExpected: T(7));
            Add(svc, 3003, "Ascendant Protocol",         "Zero.Byte",        Platform.RoyalRoad,   BookStatus.Hiatus,    156, 0, D(45), "https://www.royalroad.com/fiction/3003",   null);
            Add(svc, 3004, "Glass Oracle",               "Amara Sol",        Platform.RoyalRoad,   BookStatus.Ongoing,    67, 2, D(2),  "https://www.royalroad.com/fiction/3004",   null,  hasUnread: true,  nextExpected: T(10));

            // Literotica — adult fiction
            Add(svc, 4001, "Starfall Station",           "TwilightAuthor",   Platform.Literotica,  BookStatus.Ongoing,    28, 1, D(3),  "https://www.literotica.com/series/4001",   null,  hasUnread: true,  nextExpected: T(18));
            Add(svc, 4002, "The Professor's Assistant",  "IndigoNight",      Platform.Literotica,  BookStatus.Completed,  15, 0, D(20), "https://www.literotica.com/series/4002",   null);
            Add(svc, 4003, "Crimson Ties",               "Emberveil",        Platform.Literotica,  BookStatus.Ongoing,    42, 0, D(6),  "https://www.literotica.com/series/4003",   null,  nextExpected: T(25));

            // AO3 — fanfiction
            Add(svc, 5001, "Through the Looking Glass",  "orphan_account",   Platform.Ao3,         BookStatus.Ongoing,    23, 2, D(1),  "https://archiveofourown.org/works/5001",   null,  hasUnread: true,  nextExpected: T(6));
            Add(svc, 5002, "Static",                     "Penrose",          Platform.Ao3,         BookStatus.Completed,   8, 0, D(14), "https://archiveofourown.org/works/5002",   null);
            Add(svc, 5003, "Between Worlds",             "celestial_drift",  Platform.Ao3,         BookStatus.Ongoing,    45, 1, D(2),  "https://archiveofourown.org/works/5003",   null,  hasUnread: true,  nextExpected: T(12));
            Add(svc, 5004, "The Long Way Home",          "starmap",          Platform.Ao3,         BookStatus.Hiatus,     31, 0, D(60), "https://archiveofourown.org/works/5004",   null);
        }

        private static void Add(BookNotifierService svc, long id, string title, string author,
            Platform platform, BookStatus status, int? totalChapters, int? newChapters,
            DateTime lastUpdated, string url, string? coverUrl, bool hasUnread = false,
            string? amazonUrl = null, DateOnly? releaseDate = null, DateTime? nextExpected = null)
        {
            svc.Books.Add(new BookEntry
            {
                Id            = id.ToString(),
                Title         = title,
                Author        = author,
                Platform      = platform,
                Status        = status,
                TotalChapters = totalChapters,
                NewChapters   = newChapters > 0 ? newChapters : null,
                LastUpdated   = lastUpdated,
                Url           = url,
                CoverUrl      = coverUrl,
                HasUnread     = hasUnread,
                AmazonUrl     = amazonUrl,
                ReleaseDate   = releaseDate,
                NextExpected  = nextExpected
                    ?? (releaseDate.HasValue ? releaseDate.Value.ToDateTime(TimeOnly.MinValue) : null),
            });
        }

        // ── Events ────────────────────────────────────────────────────────────────

        private static void SeedEvents(BookNotifierService svc)
        {
            DateTime T(int minsAgo) => DateTime.UtcNow.AddMinutes(-minsAgo);

            Ev(svc, Platform.ScribbleHub, EventType.NewChapter, "Dungeon Core: Reborn",       "MidnightInk",     "Chapter 342 — The Iron Depths",    T(8),   "https://www.scribblehub.com/read/2001-dungeon-core/chapter/342/");
            Ev(svc, Platform.RoyalRoad,   EventType.NewChapter, "The Last Artificer",          "Nora Vane",       "Chapter 87 — Calibration Day",     T(15),  "https://www.royalroad.com/fiction/3002/chapter/87");
            Ev(svc, Platform.ScribbleHub, EventType.NewBook,    "Threads of Fate",             "Wisteria",        "New series added to reading list", T(90),  "https://www.scribblehub.com/series/2004/");
            Ev(svc, Platform.Ao3,         EventType.NewChapter, "Through the Looking Glass",   "orphan_account",  "Chapter 23 — Mirror, Mirror",      T(120), "https://archiveofourown.org/works/5001/chapters/23");
            Ev(svc, Platform.ScribbleHub, EventType.NewChapter, "The Villainess Lives Twice",  "PearlQuill",      "Chapter 189 — The Final Act",      T(140), "https://www.scribblehub.com/read/2002/chapter/189/");
            Ev(svc, Platform.GoodReads,   EventType.NewBook,    "The Midnight Archive",        "Iris Calloway",   "Added to your to-read shelf",      T(480), "https://www.goodreads.com/book/show/1001",          isRead: true);
            Ev(svc, Platform.RoyalRoad,   EventType.NewChapter, "Glass Oracle",                "Amara Sol",       "Chapter 67 — Fractured Light",     T(600), "https://www.royalroad.com/fiction/3004/chapter/67", isRead: true);
            Ev(svc, Platform.RoyalRoad,   EventType.NewChapter, "Pale Defender",               "J. Kross",        "Chapter 234 — The Pale Tide",      T(720), "https://www.royalroad.com/fiction/3001/chapter/234", isRead: true);

            // literotica author profile updates (recent)
            Ev(svc, Platform.Literotica,  EventType.AuthorRelease, "Emberveil",               "Emberveil",       "Updated biography",                 T(200),   "https://www.literotica.com/au/Emberveil");
            Ev(svc, Platform.Literotica,  EventType.AuthorRelease, "IndigoNight",             "IndigoNight",     "Updated profile picture & bio",     T(560),   "https://www.literotica.com/au/IndigoNight", isRead: true);

            // yesterday
            Ev(svc, Platform.Literotica,  EventType.NewChapter, "Starfall Station",            "TwilightAuthor",  "Part 28 — Docking Bay 9",          T(1440),  "https://www.literotica.com/series/4001/28", isRead: true);
            Ev(svc, Platform.Literotica,  EventType.AuthorRelease, "TwilightAuthor",              "TwilightAuthor", "Updated bio and linked socials", T(1490),  "https://www.literotica.com/au/TwilightAuthor", isRead: true);
            Ev(svc, Platform.ScribbleHub, EventType.NewChapter, "Dungeon Core: Reborn",        "MidnightInk",     "Chapter 341 — Salt Caverns",        T(1500),  "https://www.scribblehub.com/read/2001/chapter/341/", isRead: true);
            Ev(svc, Platform.Ao3,         EventType.NewChapter, "Between Worlds",              "celestial_drift", "Chapter 45 — Crossing Over",        T(1600),  "https://archiveofourown.org/works/5003/chapters/45", isRead: true);
            Ev(svc, Platform.RoyalRoad,   EventType.NewChapter, "The Last Artificer",          "Nora Vane",       "Chapter 86 — Stress Tests",         T(1800),  "https://www.royalroad.com/fiction/3002/chapter/86", isRead: true);
            Ev(svc, Platform.GoodReads,   EventType.BookReleased, "Echo of the Forgotten",     "L. Chen",         "Amazon release date confirmed",     T(2100),  "https://amazon.com/dp/MOCK003",               isRead: true);
            Ev(svc, Platform.ScribbleHub, EventType.StatusChange, "System Error: Null Pointer","CodexScribe",     "Status changed to Completed",       T(2880),  "https://www.scribblehub.com/series/2003/",   isRead: true);

            // 2 days ago
            Ev(svc, Platform.RoyalRoad,   EventType.NewChapter, "Glass Oracle",                "Amara Sol",       "Chapter 66 — The Oracle Speaks",    T(2880+60),  "https://www.royalroad.com/fiction/3004/chapter/66", isRead: true);
            Ev(svc, Platform.Ao3,         EventType.NewChapter, "Through the Looking Glass",   "orphan_account",  "Chapter 22 — Red Queen",            T(2880+120), "https://archiveofourown.org/works/5001/chapters/22", isRead: true);
            Ev(svc, Platform.Literotica,  EventType.NewChapter, "Crimson Ties",                "Emberveil",       "Chapter 42 — Scarlet Threads",      T(2880+300), "https://www.literotica.com/series/4003/42",   isRead: true);
            Ev(svc, Platform.ScribbleHub, EventType.NewChapter, "The Villainess Lives Twice",  "PearlQuill",      "Chapter 188 — Betrayal",            T(2880+360), "https://www.scribblehub.com/read/2002/chapter/188/", isRead: true);
            Ev(svc, Platform.RoyalRoad,   EventType.NewChapter, "Pale Defender",               "J. Kross",        "Chapter 233 — The Last Light",      T(2880+480), "https://www.royalroad.com/fiction/3001/chapter/233", isRead: true);

            // 3 days ago
            Ev(svc, Platform.ScribbleHub, EventType.NewChapter, "Dungeon Core: Reborn",        "MidnightInk",     "Chapter 340 — The Deep Forge",      T(4320+60),  "https://www.scribblehub.com/read/2001/chapter/340/", isRead: true);
            Ev(svc, Platform.Ao3,         EventType.NewBook,    "The Long Way Home",           "starmap",         "New work bookmarked",               T(4320+180), "https://archiveofourown.org/works/5004/",     isRead: true);
            Ev(svc, Platform.GoodReads,   EventType.NewBook,    "Echo of the Forgotten",       "L. Chen",         "Added to your to-read shelf",       T(4320+300), "https://www.goodreads.com/book/show/1003",    isRead: true);
            Ev(svc, Platform.Literotica,  EventType.NewBook,    "Starfall Station",            "TwilightAuthor",  "New series discovered",             T(4320+420), "https://www.literotica.com/series/4001/",     isRead: true);
        }

        private static void Ev(BookNotifierService svc, Platform platform, EventType type,
            string title, string? author, string description, DateTime timestamp,
            string? url = null, bool isRead = false)
        {
            svc.Events.Add(new NotificationEvent
            {
                Platform    = platform,
                Type        = type,
                Title       = title,
                Author      = author,
                Description = description,
                Timestamp   = timestamp,
                Url         = url,
                IsRead      = isRead,
            });
        }

        // ── Pending releases ──────────────────────────────────────────────────────

        private static void SeedReleases(BookNotifierService svc)
        {
            string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            svc.UpcomingReleases.Add(new DbPendingRelease(
                Id: 1, BookId: 1001,
                Title: "The Midnight Archive",
                BookUrl: "https://www.goodreads.com/book/show/1001",
                AmazonUrl: "https://amazon.com/dp/MOCK001",
                ReleaseDate: DateOnly.FromDateTime(DateTime.Today.AddDays(14)).ToString("yyyy-MM-dd"),
                CalendarAdded: true, Notified: false, CreatedAt: Now()));

            svc.UpcomingReleases.Add(new DbPendingRelease(
                Id: 2, BookId: 1003,
                Title: "Echo of the Forgotten",
                BookUrl: "https://www.goodreads.com/book/show/1003",
                AmazonUrl: "https://amazon.com/dp/MOCK003",
                ReleaseDate: DateOnly.FromDateTime(DateTime.Today.AddMonths(2)).ToString("yyyy-MM-dd"),
                CalendarAdded: false, Notified: false, CreatedAt: Now()));

            svc.UpcomingReleases.Add(new DbPendingRelease(
                Id: 3, BookId: 1004,
                Title: "Shattered Kingdoms: Ascendant",
                BookUrl: "https://www.goodreads.com/book/show/1004",
                AmazonUrl: "https://amazon.com/dp/MOCK004",
                ReleaseDate: DateOnly.FromDateTime(DateTime.Today.AddDays(62)).ToString("yyyy-MM-dd"),
                CalendarAdded: true, Notified: false, CreatedAt: Now()));
        }
    }
}
