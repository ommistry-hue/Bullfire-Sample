using Bullfire;
using Microsoft.AspNetCore.Mvc;
using BullfireSample.Models;
using BullfireSample.Services;

namespace BullfireSample.Controllers;

public sealed class HomeController : Controller
{
    public const string QueueName = "status-updates";

    private readonly StatusStore _store;
    private readonly BullfireRedisConnection _redis;
    private readonly ILogger<HomeController> _logger;

    public HomeController(StatusStore store, BullfireRedisConnection redis, ILogger<HomeController> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public IActionResult Index() => View(_store.GetAll());

    [HttpGet]
    public IActionResult Create() => View(new CreateStatusItemRequest());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateStatusItemRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(request);
        }

        var scheduledUtc = request.ScheduledFor.Kind == DateTimeKind.Utc
            ? request.ScheduledFor
            : request.ScheduledFor.ToUniversalTime();

        var delayMs = (long)Math.Max(0, (scheduledUtc - DateTime.UtcNow).TotalMilliseconds);

        var item = _store.Add(new StatusItem
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            CurrentStatus = ItemStatus.Pending,
            TargetStatus = request.TargetStatus,
            ScheduledFor = scheduledUtc,
            CreatedAt = DateTime.UtcNow,
        });

        var queue = new Queue(QueueName, _redis);
        var jobId = await queue.AddAsync(
            "change-status",
            new StatusUpdateJob(item.Id, item.TargetStatus),
            new JobOptions { DelayMilliseconds = delayMs },
            cancellationToken);

        item.JobId = jobId;

        _logger.LogInformation(
            "Scheduled status change for item {ItemId} ({Title}) → {NewStatus} in {DelayMs} ms (jobId={JobId})",
            item.Id, item.Title, item.TargetStatus, delayMs, jobId);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Error() => View();
}
