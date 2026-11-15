using System.Collections;

namespace Claudio.Core.Models;

/// <summary>
/// A read-only list that compares by its items, as a Swift array does. Records holding one stay
/// equal when they hold the same things, which is how a view model tells a new reading from the
/// same one.
/// </summary>
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] _items;

    public ValueList(IEnumerable<T> items) => _items = [.. items];

#pragma warning disable CA1000 // ValueList<Agent>.Empty reads as Swift's [].
    public static ValueList<T> Empty { get; } = new([]);
#pragma warning restore CA1000

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public bool Equals(ValueList<T>? other) =>
        other is not null && (ReferenceEquals(this, other) || _items.AsSpan().SequenceEqual(other._items, EqualityComparer<T>.Default));

    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }

#pragma warning disable CA2225 // A list built from an array or a List reads as either.
    public static implicit operator ValueList<T>(T[] items) => new(items);

    public static implicit operator ValueList<T>(List<T> items) => new(items);
#pragma warning restore CA2225
}
