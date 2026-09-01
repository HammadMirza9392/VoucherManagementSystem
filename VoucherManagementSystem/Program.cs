using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Repositories;
using VoucherManagementSystem.Filters;
using VoucherManagementSystem.Services;
using VoucherManagementSystem.Services.Caching;

// Tell Npgsql to treat all DateTime as UTC globally — avoids errors across the whole app
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Disable file watching outside development. Each watched file consumes an inotify
// instance, and Linux hosts (Render) cap these at 128 per user — exhausting them
// crashes the app at CreateBuilder with an IOException before startup completes.
var isDevelopment = string.Equals(
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
    "Development",
    StringComparison.OrdinalIgnoreCase);

if (!isDevelopment)
{
    Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var mvcBuilder = builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<PageLockFilter>();
});

// Razor runtime compilation is a development-only convenience (edit .cshtml without
// restarting). In production it watches the whole Views tree, burning inotify handles.
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}

// ---------------------------------------------------------------------------
// Database connection
//
// appsettings.json ships with a DUMMY connection string on purpose, so the real
// database credentials never live in the repository. Each deployment supplies its
// own value, and the first source that has one wins:
//
//   1. Environment variable  ConnectionStrings__DefaultConnection   (Render, Docker, IIS)
//   2. Environment variable  DATABASE_URL                           (postgres://user:pass@host:port/db)
//   3. appsettings.Development.json                                 (local machine only, git-ignored)
//   4. appsettings.json                                             (dummy placeholder)
//
// On Render: Settings > Environment > add ConnectionStrings__DefaultConnection.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Render's own Postgres injects DATABASE_URL in URL form, which Npgsql cannot read
// directly, so translate it into a normal key/value connection string.
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrWhiteSpace(databaseUrl) && databaseUrl.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
{
    var uri = new Uri(databaseUrl);
    var credentials = uri.UserInfo.Split(':', 2);
    connectionString =
        $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};" +
        $"Database={uri.AbsolutePath.TrimStart('/')};" +
        $"Username={Uri.UnescapeDataString(credentials[0])};" +
        $"Password={Uri.UnescapeDataString(credentials.Length > 1 ? credentials[1] : string.Empty)};" +
        "SSL Mode=Require;Trust Server Certificate=true";
}

if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("CHANGE_ME"))
{
    throw new InvalidOperationException(
        "No database connection string configured. appsettings.json only holds a placeholder. " +
        "Set the environment variable ConnectionStrings__DefaultConnection (or DATABASE_URL) for this " +
        "deployment, or put a real value in appsettings.Development.json when running locally.");
}

// Configure Entity Framework with performance optimizations
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(connectionString,
        npgsqlOptions =>
        {
            npgsqlOptions.CommandTimeout(30);
            npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
        });
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking); // Faster read operations
    options.EnableSensitiveDataLogging(false); // Reduce overhead
});

// Master-data memory cache (customers, items, banks, rates, etc.) — invalidated on writes
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IMasterDataCache, MasterDataCache>();

// Register Repository Services
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IVoucherRepository, VoucherRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IItemRepository, ItemRepository>();
builder.Services.AddScoped<IBankRepository, BankRepository>();
builder.Services.AddScoped<IExpenseHeadRepository, ExpenseHeadRepository>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();

// Register PageLockFilter with DbContext dependency
builder.Services.AddScoped<PageLockFilter>();

// Site branding (site name shown across the frontend) — shares the memory cache above
builder.Services.AddScoped<ISiteSettingsService, SiteSettingsService>();

// Add Session support for authentication
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Add response compression for faster data transfer
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Cache static files for 7 days
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=604800");
    }
});

app.UseRouting();

app.UseSession();

// Custom Authentication Middleware
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower();
    var isLoggedIn = context.Session.GetString("IsLoggedIn") == "true";

    // Allow access to login page, PageLock pages, and static files
    if (path == "/home/login" ||
        path == "/home/dologin" ||
        path == "/pagelock/verifymasterpassword" ||
        path == "/pagelock/masterlockauth" ||
        path == "/pagelock/verifypassword" ||
        path == "/pagelock/updatelockmode" ||
        path == "/pagelock/updatepassword" ||
        path == "/pagelock/togglelock" ||
        path == "/pagelock/updatemasterpassword" ||
        path?.StartsWith("/lib") == true ||
        path?.StartsWith("/css") == true ||
        path?.StartsWith("/js") == true)
    {
        await next();
        return;
    }

    // Redirect to login if not authenticated
    if (!isLoggedIn)
    {
        context.Response.Redirect("/Home/Login");
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Seed Database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
        SeedData.Initialize(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();