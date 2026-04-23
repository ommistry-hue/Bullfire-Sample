using Bullfire;
using Bullfire.Dashboard;
using BullfireSample.Controllers;
using BullfireSample.Models;
using BullfireSample.Services;

var builder = WebApplication.CreateBuilder(args);

// --- MVC
builder.Services.AddControllersWithViews();

// --- In-memory store (replace with EF Core / Dapper / whatever in a real app)
builder.Services.AddSingleton<StatusStore>();

// --- Bullfire: producer + worker + dashboard
var redisConfig = builder.Configuration.GetConnectionString("Redis")
    ?? "127.0.0.1:6379,abortConnect=false";

builder.Services.AddBullfire(redisConfig);

builder.Services.AddBullfireWorker<StatusUpdateHandler, StatusUpdateJob>(
    HomeController.QueueName,
    opts =>
    {
        opts.WakeInterval = TimeSpan.FromMilliseconds(500);
    });

builder.Services.AddBullfireDashboard(opts =>
{
    opts.Queues.Add(HomeController.QueueName);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapBullfireDashboard("/bullfire");

app.Run();
