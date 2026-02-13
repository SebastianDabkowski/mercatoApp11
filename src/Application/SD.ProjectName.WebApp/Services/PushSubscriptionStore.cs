using System.Collections.Concurrent;

namespace SD.ProjectName.WebApp.Services;

public record PushSubscriptionEntry(string Endpoint, string P256dh, string Auth, DateTimeOffset CreatedOn);

public class PushSubscriptionStore
{
    private readonly ConcurrentDictionary<string, List<PushSubscriptionEntry>> _store = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<PushSubscriptionEntry> Get(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<PushSubscriptionEntry>();
        }

        var list = _store.GetValueOrDefault(userId);
        if (list == null)
        {
            return Array.Empty<PushSubscriptionEntry>();
        }

        lock (list)
        {
            return list.ToList();
        }
    }

    public bool HasAny(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var list = _store.GetValueOrDefault(userId);
        if (list == null)
        {
            return false;
        }

        lock (list)
        {
            return list.Count > 0;
        }
    }

    public void Save(string userId, PushSubscriptionEntry entry)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var list = _store.GetOrAdd(userId, _ => []);
        lock (list)
        {
            var existingIndex = list.FindIndex(s => string.Equals(s.Endpoint, entry.Endpoint, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                list[existingIndex] = entry;
            }
            else
            {
                list.Add(entry);
            }
        }
    }

    public void Remove(string userId, string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        var list = _store.GetValueOrDefault(userId);
        if (list == null)
        {
            return;
        }

        lock (list)
        {
            var index = list.FindIndex(s => string.Equals(s.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                list.RemoveAt(index);
            }
        }
    }
}
