using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotNext.Runtime.Caching;

using Numerics;
using Threading;

public partial class RandomAccessCache<TKey, TValue>
{
    // devirtualize Value getter manually (JIT will replace this method with one of the actual branches)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref readonly TValue GetValue(KeyValuePair pair)
    {
        Debug.Assert(pair is not null);
        Debug.Assert(pair is not FakeKeyValuePair);
        Debug.Assert(Atomic.IsAtomic<TValue>() ? pair is KeyValuePairAtomicAccess : pair is KeyValuePairNonAtomicAccess);

        return ref Atomic.IsAtomic<TValue>()
            ? ref Unsafe.As<KeyValuePairAtomicAccess>(pair).Value
            : ref Unsafe.As<KeyValuePairNonAtomicAccess>(pair).ValueRef;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetValue(KeyValuePair pair, TValue value)
    {
        Debug.Assert(pair is not FakeKeyValuePair);

        if (Atomic.IsAtomic<TValue>())
        {
            Unsafe.As<KeyValuePairAtomicAccess>(pair).Value = value;
        }
        else
        {
            Unsafe.As<KeyValuePairNonAtomicAccess>(pair).Value = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ClearValue(KeyValuePair pair)
    {
        Debug.Assert(pair is not FakeKeyValuePair);

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
        {
            // do nothing
        }
        else if (Atomic.IsAtomic<TValue>())
        {
            Unsafe.As<KeyValuePairAtomicAccess>(pair).Value = default!;
        }
        else
        {
            Unsafe.As<KeyValuePairNonAtomicAccess>(pair).ClearValue();
        }
    }

    private static KeyValuePair CreatePair(TKey key, TValue value, int hashCode)
    {
        return Atomic.IsAtomic<TValue>()
            ? new KeyValuePairAtomicAccess(key, hashCode) { Value = value }
            : new KeyValuePairNonAtomicAccess(key, hashCode) { Value = value };
    }

    private readonly Bucket[] buckets;
    private readonly ulong fastModMultiplier;

    private Bucket GetBucket(int hashCode)
    {
        var index = (int)(IntPtr.Size is sizeof(ulong)
            ? PrimeNumber.FastMod((uint)hashCode, (uint)buckets.Length, fastModMultiplier)
            : (uint)hashCode % (uint)buckets.Length);

        Debug.Assert((uint)index < (uint)buckets.Length);

        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buckets), index);
    }

    internal partial class KeyValuePair(TKey key, int hashCode)
    {
        internal readonly int KeyHashCode = hashCode;
        internal readonly TKey Key = key;
        internal volatile KeyValuePair? NextInBucket; // volatile, used by the dictionary subsystem only
        
        // Reference counting is used to establish lifetime of the stored value (not KeyValuePair instance).
        // Initial value 1 means that the pair is referenced by the eviction queue. There
        // are two competing threads that may decrement the counter to zero: removal thread (see TryRemove)
        // and eviction thread. To synchronize the decision, 'cacheState' is used. The thread that evicts the pair
        // successfully (transition from 0 => -1) is able to decrement the counter to zero.
        private volatile int lifetimeCounter = 1;

        internal bool TryAcquireCounter()
        {
            int currentValue, tmp = lifetimeCounter;
            do
            {
                currentValue = tmp;
                if (currentValue is 0)
                    break;
            } while ((tmp = Interlocked.CompareExchange(ref lifetimeCounter, currentValue + 1, currentValue)) != currentValue);

            return currentValue > 0U;
        }

        internal bool ReleaseCounter() => Interlocked.Decrement(ref lifetimeCounter) > 0;
        
        [ExcludeFromCodeCoverage]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal (int Alive, int Dead) BucketNodesCount
        {
            get
            {
                var alive = 0;
                var dead = 0;
                for (var current = this; current is not null; current = current.NextInBucket)
                {
                    ref var counterRef = ref current.IsDead ? ref dead : ref alive;
                    counterRef++;
                }

                return (alive, dead);
            }
        }
    }

    private sealed class KeyValuePairAtomicAccess(TKey key, int hashCode) : KeyValuePair(key, hashCode)
    {
        internal required TValue Value;

        public override string ToString() => ToString(Value);
    }

    // non-atomic access utilizes copy-on-write semantics
    private sealed class KeyValuePairNonAtomicAccess(TKey key, int hashCode) : KeyValuePair(key, hashCode)
    {
        private sealed class ValueHolder(TValue value)
        {
            internal readonly TValue Value = value;
        }

        private static readonly ValueHolder DefaultHolder = new(default!);
        
        private ValueHolder holder;

        internal required TValue Value
        {
            get => holder.Value;

            [MemberNotNull(nameof(holder))] set => holder = new(value);
        }

        internal ref readonly TValue ValueRef => ref holder.Value;

        internal void ClearValue() => holder = DefaultHolder;

        public override string ToString() => ToString(Value);
    }

    [DebuggerDisplay($"NumberOfItems = {{{nameof(Count)}}}")]
    internal sealed class Bucket : AsyncExclusiveLock
    {
        private bool newPairAdded;
        private volatile KeyValuePair? first; // volatile

        [ExcludeFromCodeCoverage]
        private (int Alive, int Dead) Count => first?.BucketNodesCount ?? default;

        internal KeyValuePair? TryAdd(TKey key, int hashCode, TValue value)
        {
            KeyValuePair? result;
            if (newPairAdded)
            {
                result = null;
            }
            else
            {
                result = CreatePair(key, value, hashCode);
                result.NextInBucket = first;
                first = result;
                newPairAdded = true;
            }

            return result;
        }

        internal void MarkAsReadyToAdd() => newPairAdded = false;

        private void Remove(KeyValuePair? previous, KeyValuePair current)
        {
            ref var location = ref previous is null ? ref first : ref previous.NextInBucket;
            Volatile.Write(ref location, current.NextInBucket);
        }

        internal KeyValuePair? TryRemove(IEqualityComparer<TKey>? keyComparer, TKey key, int hashCode)
        {
            var result = default(KeyValuePair?);

            // remove all dead nodes from the bucket
            if (keyComparer is null)
            {
                for (KeyValuePair? current = first, previous = null;
                     current is not null;
                     previous = current, current = current.NextInBucket)
                {
                    if (result is null && hashCode == current.KeyHashCode
                                       && EqualityComparer<TKey>.Default.Equals(key, current.Key)
                                       && current.MarkAsEvicted())
                    {
                        result = current;
                    }

                    if (current.IsDead)
                    {
                        Remove(previous, current);
                    }
                }
            }
            else
            {
                for (KeyValuePair? current = first, previous = null;
                     current is not null;
                     previous = current, current = current.NextInBucket)
                {
                    if (result is null && hashCode == current.KeyHashCode
                                       && keyComparer.Equals(key, current.Key)
                                       && current.MarkAsEvicted())
                    {
                        result = current;
                    }

                    if (current.IsDead)
                    {
                        Remove(previous, current);
                    }
                }
            }

            return result;
        }

        internal KeyValuePair? TryGet(IEqualityComparer<TKey>? keyComparer, TKey key, int hashCode)
        {
            var result = default(KeyValuePair?);

            // remove all dead nodes from the bucket
            if (keyComparer is null)
            {
                for (KeyValuePair? current = first, previous = null;
                     current is not null;
                     previous = current, current = current.NextInBucket)
                {
                    if (result is null && hashCode == current.KeyHashCode
                                       && EqualityComparer<TKey>.Default.Equals(key, current.Key)
                                       && current.Visit()
                                       && current.TryAcquireCounter())
                    {
                        result = current;
                    }

                    if (current.IsDead)
                    {
                        Remove(previous, current);
                    }
                }
            }
            else
            {
                for (KeyValuePair? current = first, previous = null;
                     current is not null;
                     previous = current, current = current.NextInBucket)
                {
                    if (result is null && hashCode == current.KeyHashCode
                                       && keyComparer.Equals(key, current.Key)
                                       && current.Visit()
                                       && current.TryAcquireCounter())
                    {
                        result = current;
                    }

                    if (current.IsDead)
                    {
                        Remove(previous, current);
                    }
                }
            }

            return result;
        }

        internal KeyValuePair? Modify(IEqualityComparer<TKey>? keyComparer, TKey key, int hashCode)
        {
            KeyValuePair? valueHolder = null;
            if (keyComparer is null)
            {
                for (KeyValuePair? current = first, previous = null; current is not null; previous = current, current = current.NextInBucket)
                {
                    if (valueHolder is null && hashCode == current.KeyHashCode
                                            && EqualityComparer<TKey>.Default.Equals(key, current.Key)
                                            && current.Visit()
                                            && current.TryAcquireCounter())
                    {
                        valueHolder = current;
                    }

                    if (current.IsDead)
                    {
                        Remove(previous, current);
                    }
                }
            }
            else
            {
                for (KeyValuePair? current = first, previous = null; current is not null; previous = current, current = current.NextInBucket)
                {
                    if (valueHolder is null && hashCode == current.KeyHashCode
                                            && keyComparer.Equals(key, current.Key)
                                            && current.Visit()
                                            && current.TryAcquireCounter())
                    {
                        valueHolder = current;
                    }

                    if (current.IsDead)
                    {
                        Remove(previous, current);
                    }
                }
            }

            return valueHolder;
        }

        internal void CleanUp(IEqualityComparer<TKey>? keyComparer)
        {
            // remove all dead nodes from the bucket
            if (keyComparer is null)
            {
                for (KeyValuePair? current = first, previous = null;
                     current is not null;
                     previous = current, current = current.NextInBucket)
                {
                    if (current.IsDead)
                    {
                        Remove(previous, current);
                    }
                }
            }
            else
            {
                for (KeyValuePair? current = first, previous = null;
                     current is not null;
                     previous = current, current = current.NextInBucket)
                {
                    if (current.IsDead)
                    {
                        Remove(previous, current);
                    }
                }
            }
        }
        
        internal void Invalidate(IEqualityComparer<TKey>? keyComparer, Action<KeyValuePair> cleanup)
        {
            // remove all dead nodes from the bucket
            if (keyComparer is null)
            {
                for (KeyValuePair? current = first, previous = null;
                     current is not null;
                     previous = current, current = current.NextInBucket)
                {
                    Remove(previous, current);
                    
                    if (current.MarkAsEvicted() && current.ReleaseCounter() is false)
                    {
                        cleanup.Invoke(current);
                    }
                }
            }
            else
            {
                for (KeyValuePair? current = first, previous = null;
                     current is not null;
                     previous = current, current = current.NextInBucket)
                {
                    Remove(previous, current);
                    
                    if (current.MarkAsEvicted() && current.ReleaseCounter() is false)
                    {
                        cleanup.Invoke(current);
                    }
                }
            }
        }
    }
}