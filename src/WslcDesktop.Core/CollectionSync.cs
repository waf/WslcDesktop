using System.Collections.ObjectModel;

namespace WslcDesktop.Core;

internal static class CollectionSync
{
    /// <summary>
    /// Makes <paramref name="target"/> contain <paramref name="source"/> (same order) with the fewest changes:
    /// items whose key is unchanged and value is equal are left alone, so views keep their containers and selection.
    /// </summary>
    public static void Sync<T, TKey>(ObservableCollection<T> target, IReadOnlyList<T> source, Func<T, TKey> key)
        where TKey : notnull
    {
        var wanted = new HashSet<TKey>(source.Select(key));
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(key(target[i])))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];
            var itemKey = key(item);

            if (i < target.Count && EqualityComparer<TKey>.Default.Equals(key(target[i]), itemKey))
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], item))
                {
                    target[i] = item;
                }

                continue;
            }

            var existing = IndexOf(target, itemKey, key, start: i + 1);
            if (existing >= 0)
            {
                target.Move(existing, i);
                if (!EqualityComparer<T>.Default.Equals(target[i], item))
                {
                    target[i] = item;
                }
            }
            else
            {
                target.Insert(i, item);
            }
        }
    }

    private static int IndexOf<T, TKey>(ObservableCollection<T> items, TKey wanted, Func<T, TKey> key, int start)
    {
        for (var i = start; i < items.Count; i++)
        {
            if (EqualityComparer<TKey>.Default.Equals(key(items[i]), wanted))
            {
                return i;
            }
        }

        return -1;
    }
}
