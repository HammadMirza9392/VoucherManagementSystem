using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Repositories;
using VoucherManagementSystem.Filters;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<PageLockFilter>();
})
    .AddRazorRuntimeCompilation();

// Configure Entity Framework with performance optimizations
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.CommandTimeout(30); // Fast timeout
            sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
        });
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking); // Faster read operations
    options.EnableSensitiveDataLogging(false); // Reduce overhead
});

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