// Taken from 
// https://github.com/keijiro/Firefly/blob/master/Assets/Firefly/Utility/NativeCounter.cs

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

[StructLayout(LayoutKind.Sequential)]
[NativeContainer]
unsafe public struct NativeMultiCounter {
    // The actual pointers to the allocated count needs to have restrictions relaxed so jobs can be schedled with this container
    [NativeDisableUnsafePtrRestriction]
    int* m_Counters;
    int capacity;
    int sizeOfInt;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    AtomicSafetyHandle m_Safety;
    // The dispose sentinel tracks memory leaks. It is a managed type so it is cleared to null when scheduling a job
    // The job cannot dispose the container, and no one else can dispose it until the job has run so it is ok to not pass it along
    // This attribute is required, without it this native container cannot be passed to a job since that would give the job access to a managed object
    [NativeSetClassTypeToNullOnSchedule]
    DisposeSentinel m_DisposeSentinel;
#endif

    // Keep track of where the memory for this was allocated
    Allocator m_AllocatorLabel;

    public NativeMultiCounter(int capacity, Allocator label) {
        // This check is redundant since we always use an int which is blittable.
        // It is here as an example of how to check for type correctness for generic types.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (!UnsafeUtility.IsBlittable<int>())
            throw new ArgumentException(string.Format("{0} used in NativeQueue<{0}> must be blittable", typeof(int)));
#endif
        m_AllocatorLabel = label;

        // Allocate native memory for multiple integers
        sizeOfInt = UnsafeUtility.SizeOf<int>();
        m_Counters = (int*)UnsafeUtility.Malloc(sizeOfInt * capacity, 4, label);
        UnsafeUtility.MemClear(m_Counters, sizeOfInt * capacity);

        // Create a dispose sentinel to track memory leaks. This also creates the AtomicSafetyHandle
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, label);
#endif

        this.capacity = capacity;
    }

    public int this[int index] {
        get {
            // Verify that the caller has read permission on this data.
            // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            return *(m_Counters + index * sizeOfInt);
        }
        set {
            // Verify that the caller has write permission on this data. This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            *(m_Counters + index * sizeOfInt) = value;
        }
    }

    public bool IsCreated {
        get { return m_Counters != null; }
    }

    public void Reset() {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        UnsafeUtility.MemClear(m_Counters, sizeOfInt * capacity);
    }

    public void Dispose() {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        // Let the dispose sentinel know that the data has been freed so it does not report any memory leaks
        DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
        UnsafeUtility.Free(m_Counters, m_AllocatorLabel);
        m_Counters = null;
    }

    [NativeContainer]
    // This attribute is what makes it possible to use NativeCounter.Concurrent in a ParallelFor job
    [NativeContainerIsAtomicWriteOnly]
    unsafe public struct Concurrent {
        // Copy of the pointer from the full NativeCounter
        [NativeDisableUnsafePtrRestriction]
        int* m_Counters;
        int sizeOfInt;
        int capacity;

        // Copy of the AtomicSafetyHandle from the full NativeCounter. The dispose sentinel is not copied since this inner struct does not own the memory and is not responsible for freeing it
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle m_Safety;
#endif

        // This is what makes it possible to assign to NativeCounter.Concurrent from NativeCounter
        public static implicit operator Concurrent(NativeMultiCounter cnt) {
            Concurrent concurrent;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(cnt.m_Safety);
            concurrent.m_Safety = cnt.m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref concurrent.m_Safety);
#endif

            concurrent.m_Counters = cnt.m_Counters;
            concurrent.sizeOfInt = cnt.sizeOfInt;
            concurrent.capacity = cnt.capacity;
            return concurrent;
        }

        public int Increment(int index) {
            // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            // The actual increment is implemented with an atomic since it can be incremented by multiple threads at the same time
            return Interlocked.Increment(ref *(m_Counters + index * sizeOfInt)) - 1;
        }

        public int Decrement(int index) {
            // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            // The actual decrement is implemented with an atomic since it can be decremented by multiple threads at the same time
            return Interlocked.Decrement(ref *(m_Counters + index * sizeOfInt)) - 1;
        }
    }
}