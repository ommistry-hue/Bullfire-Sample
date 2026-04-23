using Bullfire;
using BullfireSample.Models;

namespace BullfireSample.Services;

/// <summary>
/// Bullfire handler that runs whenever a scheduled status change fires.
/// Resolves fresh from a DI scope per invocation — same pattern as an ASP.NET Core
/// controller action.
/// </summary>
public sealed class StatusUpdateHandler : IJobHandler<StatusUpdateJob>
{
    private readonly StatusStore _store;
    private readonly ILogger<StatusUpdateHandler> _logger;

    public StatusUpdateHandler(StatusStore store, ILogger<StatusUpdateHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task HandleAsync(JobContext<StatusUpdateJob> context, CancellationToken cancellationToken)
    {
        var updated = _store.UpdateStatus(context.Data.ItemId, context.Data.NewStatus);

        if (updated)
        {
            _logger.LogInformation(
                "Status change applied: item={ItemId} → {NewStatus} (jobId={JobId}, attempt={Attempt})",
                context.Data.ItemId, context.Data.NewStatus, context.JobId, context.AttemptsMade);
        }
        else
        {
            _logger.LogWarning(
                "Status change skipped — item {ItemId} not found (jobId={JobId})",
                context.Data.ItemId, context.JobId);
        }

        return Task.CompletedTask;
    }
}
