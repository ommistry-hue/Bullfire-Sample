using System.Collections.Concurrent;
using BullfireSample.Models;

namespace BullfireSample.Services;

/// <summary>
/// In-memory singleton store for <see cref="StatusItem"/>s. In a real app this would be a
/// database (EF Core, Dapper, etc.) — kept in-memory here so the sample has zero external
/// dependencies besides Redis.
/// </summary>
public sealed class StatusStore
{
    private readonly ConcurrentDictionary<Guid, StatusItem> _items = new();

    public StatusItem Add(StatusItem item)
    {
        _items[item.Id] = item;
        return item;
    }

    public IReadOnlyList<StatusItem> GetAll() =>
        _items.Values.OrderByDescending(i => i.CreatedAt).ToList();

    public StatusItem? Get(Guid id) => _items.TryGetValue(id, out var item) ? item : null;

    public bool UpdateStatus(Guid id, ItemStatus newStatus)
    {
        if (!_items.TryGetValue(id, out var item))
        {
            return false;
        }
        item.CurrentStatus = newStatus;
        item.CompletedAt = DateTime.UtcNow;
        return true;
    }
}
