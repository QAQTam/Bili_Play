namespace BiliPlay.Core;

/// <summary>
/// Category filter for the account notification feed. Filtered rows are
/// materialized once per data or category change so list rebinding reads a
/// stable snapshot instead of re-enumerating the source on every access;
/// callers invalidate after repopulating the source or switching category.
/// </summary>
public sealed class NotificationFeed
{
    private IReadOnlyList<AccountNotification> _visible = [];
    private bool _dirty = true;
    private int _category = -1;
    private IEnumerable<AccountNotification>? _source;

    /// <summary>Forces re-filtering on the next read, e.g. after the source was repopulated.</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>Returns the category-filtered rows; repeated reads between invalidations return the same instance.</summary>
    public IReadOnlyList<AccountNotification> Visible(IEnumerable<AccountNotification> items, int category)
    {
        if (_dirty || category != _category || !ReferenceEquals(items, _source))
        {
            _visible = Filter(items, category);
            _category = category; _source = items;
            _dirty = false;
        }
        return _visible;
    }

    public static IReadOnlyList<AccountNotification> Filter(IEnumerable<AccountNotification> items, int category) => category switch
    {
        1 => items.Where(item => item.IsLike).ToArray(),
        2 => items.Where(item => item.Category == "回复与@" && !item.IsLike).ToArray(),
        3 => items.Where(item => item.Category == "系统通知").ToArray(),
        _ => items.Where(item => item.Category == "私信").ToArray(),
    };
}
