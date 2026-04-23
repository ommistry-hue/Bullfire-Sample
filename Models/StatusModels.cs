using System.ComponentModel.DataAnnotations;

namespace BullfireSample.Models;

public enum ItemStatus
{
    Pending,
    InProgress,
    Done,
    Cancelled,
}

/// <summary>A row in the in-memory status store.</summary>
public sealed class StatusItem
{
    public required Guid Id { get; init; }
    public required string Title { get; set; }
    public required ItemStatus CurrentStatus { get; set; }
    public required ItemStatus TargetStatus { get; init; }
    public required DateTime ScheduledFor { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public string? JobId { get; set; }
}

/// <summary>Form model bound by MVC from the Create view.</summary>
public sealed class CreateStatusItemRequest
{
    [Required, StringLength(120)]
    public string Title { get; set; } = "";

    [Required]
    public ItemStatus TargetStatus { get; set; } = ItemStatus.Done;

    /// <summary>
    /// When the background job should fire and update the status. Form uses an
    /// HTML &lt;input type="datetime-local"&gt;, which posts a <see cref="DateTime"/>
    /// in the user's local time zone.
    /// </summary>
    [Required]
    public DateTime ScheduledFor { get; set; } = DateTime.Now.AddSeconds(30);
}

/// <summary>
/// Payload serialized by Bullfire and sent to the background worker. It carries the
/// item id and the new status to apply when the delay elapses.
/// </summary>
public sealed record StatusUpdateJob(Guid ItemId, ItemStatus NewStatus);
