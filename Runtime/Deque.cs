using System.Collections;

namespace SharpTS.Runtime;

/// <summary>
/// A double-ended queue (deque) implemented as a circular buffer.
/// Provides O(1) operations for adding/removing elements at both ends,
/// and O(1) random access by index.
/// </summary>
/// <remarks>
/// This implementation is optimized for SharpTSArray's shift/unshift operations,
/// which are O(n) with a standard List but O(1) with this deque.
/// Implements IList&lt;T&gt; for compatibility with existing code expecting List semantics.
/// </remarks>
/// <typeparam name="T">The type of elements in the deque.</typeparam>
public sealed class Deque<T> : IList<T>, IReadOnlyList<T>
{
    private T[] _buffer;
    private int _head;  // Index of the first element
    private int _count;

    private const int DefaultCapacity = 4;

    /// <summary>
    /// Creates an empty deque with default capacity.
    /// </summary>
    public Deque() : this(DefaultCapacity) { }

    /// <summary>
    /// Creates an empty deque with specified initial capacity.
    /// </summary>
    public Deque(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = capacity > 0 ? new T[capacity] : [];
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Creates a deque initialized with elements from a collection.
    /// </summary>
    public Deque(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        if (collection is ICollection<T> col)
        {
            _buffer = new T[Math.Max(col.Count, DefaultCapacity)];
            col.CopyTo(_buffer, 0);
            _count = col.Count;
        }
        else
        {
            _buffer = new T[DefaultCapacity];
            foreach (var item in collection)
                AddLast(item);
        }
        _head = 0;
    }

    /// <summary>
    /// Gets the number of elements in the deque.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Gets the current capacity of the internal buffer.
    /// </summary>
    public int Capacity => _buffer.Length;

    public bool IsReadOnly => false;

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _buffer[ToBufferIndex(index)];
        }
        set
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _buffer[ToBufferIndex(index)] = value;
        }
    }

    /// <summary>
    /// Converts a logical index to a buffer index using modular arithmetic.
    /// </summary>
    private int ToBufferIndex(int logicalIndex)
    {
        int bufferIndex = _head + logicalIndex;
        if (bufferIndex >= _buffer.Length)
            bufferIndex -= _buffer.Length;
        return bufferIndex;
    }

    /// <summary>
    /// Adds an element to the end of the deque. O(1) amortized.
    /// </summary>
    public void AddLast(T item)
    {
        EnsureCapacity(_count + 1);
        _buffer[ToBufferIndex(_count)] = item;
        _count++;
    }

    /// <summary>
    /// Adds an element to the beginning of the deque. O(1) amortized.
    /// </summary>
    public void AddFirst(T item)
    {
        EnsureCapacity(_count + 1);
        _head = _head == 0 ? _buffer.Length - 1 : _head - 1;
        _buffer[_head] = item;
        _count++;
    }

    /// <summary>
    /// Removes and returns the element at the end of the deque. O(1).
    /// </summary>
    public T RemoveLast()
    {
        if (_count == 0)
            throw new InvalidOperationException("Deque is empty.");

        int lastIndex = ToBufferIndex(_count - 1);
        T item = _buffer[lastIndex];
        _buffer[lastIndex] = default!;
        _count--;
        return item;
    }

    /// <summary>
    /// Removes and returns the element at the beginning of the deque. O(1).
    /// </summary>
    public T RemoveFirst()
    {
        if (_count == 0)
            throw new InvalidOperationException("Deque is empty.");

        T item = _buffer[_head];
        _buffer[_head] = default!;
        _head = _head + 1 == _buffer.Length ? 0 : _head + 1;
        _count--;
        return item;
    }

    /// <summary>
    /// Peeks at the element at the beginning without removing it. O(1).
    /// </summary>
    public T PeekFirst()
    {
        if (_count == 0)
            throw new InvalidOperationException("Deque is empty.");
        return _buffer[_head];
    }

    /// <summary>
    /// Peeks at the element at the end without removing it. O(1).
    /// </summary>
    public T PeekLast()
    {
        if (_count == 0)
            throw new InvalidOperationException("Deque is empty.");
        return _buffer[ToBufferIndex(_count - 1)];
    }

    /// <summary>
    /// Adds an element to the end (IList compatibility).
    /// </summary>
    public void Add(T item) => AddLast(item);

    /// <summary>
    /// Inserts an element at the specified index. O(n) in general,
    /// but O(1) for index 0 (prepend) and index Count (append).
    /// </summary>
    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index == 0)
        {
            AddFirst(item);
            return;
        }
        if (index == _count)
        {
            AddLast(item);
            return;
        }

        // General case: shift elements
        EnsureCapacity(_count + 1);

        // Decide whether to shift left or right based on position
        if (index < _count / 2)
        {
            // Shift elements [0, index) to the left
            _head = _head == 0 ? _buffer.Length - 1 : _head - 1;
            for (int i = 0; i < index; i++)
            {
                _buffer[ToBufferIndex(i)] = _buffer[ToBufferIndex(i + 1)];
            }
        }
        else
        {
            // Shift elements [index, count) to the right
            for (int i = _count; i > index; i--)
            {
                _buffer[ToBufferIndex(i)] = _buffer[ToBufferIndex(i - 1)];
            }
        }

        _buffer[ToBufferIndex(index)] = item;
        _count++;
    }

    /// <summary>
    /// Removes the element at the specified index. O(n) in general,
    /// but O(1) for index 0 and index Count-1.
    /// </summary>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index == 0)
        {
            RemoveFirst();
            return;
        }
        if (index == _count - 1)
        {
            RemoveLast();
            return;
        }

        // Decide whether to shift left or right
        if (index < _count / 2)
        {
            // Shift elements [0, index) to the right
            for (int i = index; i > 0; i--)
            {
                _buffer[ToBufferIndex(i)] = _buffer[ToBufferIndex(i - 1)];
            }
            _buffer[_head] = default!;
            _head = _head + 1 == _buffer.Length ? 0 : _head + 1;
        }
        else
        {
            // Shift elements [index+1, count) to the left
            for (int i = index; i < _count - 1; i++)
            {
                _buffer[ToBufferIndex(i)] = _buffer[ToBufferIndex(i + 1)];
            }
            _buffer[ToBufferIndex(_count - 1)] = default!;
        }

        _count--;
    }

    /// <summary>
    /// Removes a range of elements starting at the specified index.
    /// </summary>
    public void RemoveRange(int index, int count)
    {
        if (index < 0 || count < 0 || index + count > _count)
            throw new ArgumentOutOfRangeException();

        if (count == 0) return;

        // Shift elements to fill the gap
        int remaining = _count - index - count;
        for (int i = 0; i < remaining; i++)
        {
            _buffer[ToBufferIndex(index + i)] = _buffer[ToBufferIndex(index + count + i)];
        }

        // Clear the freed slots
        for (int i = 0; i < count; i++)
        {
            _buffer[ToBufferIndex(_count - 1 - i)] = default!;
        }

        _count -= count;
    }

    /// <summary>
    /// Inserts a collection of elements at the specified index.
    /// </summary>
    public void InsertRange(int index, IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        if ((uint)index > (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var items = collection is ICollection<T> col ? col : collection.ToList();
        if (items.Count == 0) return;

        EnsureCapacity(_count + items.Count);

        // Shift existing elements to make room
        for (int i = _count - 1; i >= index; i--)
        {
            _buffer[ToBufferIndex(i + items.Count)] = _buffer[ToBufferIndex(i)];
        }

        // Insert new elements
        int insertIdx = 0;
        foreach (var item in items)
        {
            _buffer[ToBufferIndex(index + insertIdx)] = item;
            insertIdx++;
        }

        _count += items.Count;
    }

    /// <summary>
    /// Returns a range of elements as a new list.
    /// </summary>
    public List<T> GetRange(int index, int count)
    {
        if (index < 0 || count < 0 || index + count > _count)
            throw new ArgumentOutOfRangeException();

        var result = new List<T>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add(_buffer[ToBufferIndex(index + i)]);
        }
        return result;
    }

    /// <summary>
    /// Reverses the elements in place.
    /// </summary>
    public void Reverse()
    {
        for (int i = 0; i < _count / 2; i++)
        {
            int j = _count - 1 - i;
            int idxI = ToBufferIndex(i);
            int idxJ = ToBufferIndex(j);
            (_buffer[idxI], _buffer[idxJ]) = (_buffer[idxJ], _buffer[idxI]);
        }
    }

    /// <summary>
    /// Removes all elements from the deque.
    /// </summary>
    public void Clear()
    {
        if (_count > 0)
        {
            // Clear references to allow GC
            if (_head + _count <= _buffer.Length)
            {
                Array.Clear(_buffer, _head, _count);
            }
            else
            {
                Array.Clear(_buffer, _head, _buffer.Length - _head);
                Array.Clear(_buffer, 0, (_head + _count) % _buffer.Length);
            }
        }
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Adds all elements from a collection to the end of the deque.
    /// </summary>
    public void AddRange(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        foreach (var item in collection)
            AddLast(item);
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    public int IndexOf(T item)
    {
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < _count; i++)
        {
            if (comparer.Equals(_buffer[ToBufferIndex(i)], item))
                return i;
        }
        return -1;
    }

    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || arrayIndex + _count > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));

        for (int i = 0; i < _count; i++)
        {
            array[arrayIndex + i] = _buffer[ToBufferIndex(i)];
        }
    }

    /// <summary>
    /// Copies all elements to a new array.
    /// </summary>
    public T[] ToArray()
    {
        var array = new T[_count];
        CopyTo(array, 0);
        return array;
    }

    /// <summary>
    /// Copies all elements to a new list.
    /// </summary>
    public List<T> ToList()
    {
        var list = new List<T>(_count);
        for (int i = 0; i < _count; i++)
        {
            list.Add(_buffer[ToBufferIndex(i)]);
        }
        return list;
    }

    private void EnsureCapacity(int min)
    {
        if (_buffer.Length >= min) return;

        int newCapacity = _buffer.Length == 0 ? DefaultCapacity : _buffer.Length * 2;
        if (newCapacity < min) newCapacity = min;

        // Allocate new buffer and copy elements in logical order
        var newBuffer = new T[newCapacity];
        for (int i = 0; i < _count; i++)
        {
            newBuffer[i] = _buffer[ToBufferIndex(i)];
        }
        _buffer = newBuffer;
        _head = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return _buffer[ToBufferIndex(i)];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
