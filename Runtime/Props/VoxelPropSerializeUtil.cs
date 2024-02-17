using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections.LowLevel.Unsafe;

public partial class VoxelProps {
    // Affected segment (those that contain modified props)
    internal NativeHashMap<int3, NativeBitArray> ignorePropsBitmasks;
    internal ComputeBuffer ignorePropBitmaskBuffer;

    // Lookup used for storing and referncing modified but not deleted props
    internal NativeHashMap<int4, int> globalBitmaskIndexToLookup;

    // Actual data that will be stored per prop type
    internal struct PropTypeSerializedData {
        public NativeList<byte> rawBytes;
        public int stride;
        public NativeBitArray set;
        public bool valid;
    }
    internal NativeArray<PropTypeSerializedData> propTypeSerializedData;

    internal void SerializePropsOnSegmentUnload(int4 removed) {
        if (terrain.VoxelSegments.propSegmentsDict.TryGetValue(removed, out Segment segment)) {
            foreach (var collection in segment.props) {
                var propData = propTypeSerializedData[collection.Key];
                int stride = propData.stride;
                NativeList<byte> rawBytes = propData.rawBytes;
                NativeBitArray free = propData.set;

                // Create a writer that we will reuse
                FastBufferWriter writer = new FastBufferWriter(stride, Allocator.Temp);

                for (int i = 0; i < collection.Value.Item1.Count; i++) {
                    GameObject prop = collection.Value.Item1[i];
                    ushort dispatchIndex = collection.Value.Item2[i];
                    int index = VoxelUtils.FetchPropBitmaskIndex(collection.Key, dispatchIndex);
                    int4 indexer = new int4(segment.regionPosition, index);

                    // Set the prop as "destroyed"
                    if (prop == null) {
                        // Initialize this segment as a "modified" segment that will read from the prop ignore bitmask
                        if (!ignorePropsBitmasks.ContainsKey(segment.regionPosition)) {
                            ignorePropsBitmasks.Add(segment.regionPosition, new NativeBitArray(ignorePropBitmaskBuffer.count * 32, Allocator.Persistent));
                        }

                        // Write the affected bit to the buffer to tell the compute shader
                        // to no longer spawn this prop
                        NativeBitArray bitmask = ignorePropsBitmasks[segment.regionPosition];
                        bitmask.Set(index, true);

                        // Set the bit back to "free" since we're deleting the prop
                        if (globalBitmaskIndexToLookup.TryGetValue(index, out int lookup)) {
                            free.Set(lookup, false);
                        }

                        globalBitmaskIndexToLookup.Remove(indexer);
                    } else {
                        // Check if the prop was "modified"
                        var serializableProp = prop.GetComponent<SerializableProp>();

                        if (serializableProp.wasModified) {
                            // If we don't have an index, find a free one using the bitmask
                            if (serializableProp.ElementIndex == -1) {
                                serializableProp.ElementIndex = free.Find(0, 1);
                                free.Set(serializableProp.ElementIndex, true);
                            }

                            // Write the prop data
                            writer.WriteNetworkSerializable(serializableProp);

                            // Either copy the memory (update) or add it
                            int currentElementCount = rawBytes.Length / stride;
                            int currentByteOffset = serializableProp.ElementIndex * stride;

                            // Unsafe needed for raw mem cpy
                            unsafe {
                                if (serializableProp.ElementIndex >= currentElementCount) {
                                    rawBytes.AddRange(writer.GetUnsafePtr(), stride);
                                } else {
                                    UnsafeUtility.MemCpy((byte*)writer.GetUnsafePtr(), (byte*)rawBytes.GetUnsafePtr() + currentByteOffset, stride);
                                }
                            }

                            writer.Seek(0);
                            globalBitmaskIndexToLookup.TryAdd(indexer, serializableProp.ElementIndex);
                        }

                        serializableProp.wasModified = false;
                    }
                }

                writer.Dispose();
            }
        }
    }
}
