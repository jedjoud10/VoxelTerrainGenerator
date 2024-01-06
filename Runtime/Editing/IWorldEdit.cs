using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

// Wrapper that we can use to update the values of dynamic edits at runtime
public class DynamicWorldEdit<T> where T: IWorldEdit {
    public T worldEdit;
}

// Interface for world edits that we can disable / toggle / move around
public interface IWorldEdit : INetworkSerializable {
    // Is the world edit even enabled
    public bool Enabled { get; }

    // Create the delta voxel modifications (without having to read the inner voxel data)
    // The given input Voxel is the last delta value (for continuous edits)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Voxel Modify(float3 position, Voxel voxel);

    // Get the AABB bounds of this voxel edit
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bounds GetBounds();

    // MUST CALL THE "ApplyGeneric" function because we can't hide away generics
    public JobHandle Apply(VoxelChunk chunk, ref NativeArray<Voxel> voxels, JobHandle dep);

    // Apply any generic world edit onto oncoming data
    internal static JobHandle ApplyGeneric<T>(VoxelChunk chunk, ref NativeArray<Voxel> voxels, JobHandle dep, T edit) where T : struct, IWorldEdit {
        WorldEditJob<T> job = new WorldEditJob<T> {
            chunkOffset = math.float3(chunk.node.Position),
            voxelScale = VoxelUtils.VoxelSizeFactor,
            size = VoxelUtils.Size,
            vertexScaling = VoxelUtils.VertexScaling,
            scalingFactor = chunk.node.ScalingFactor,
            dynamicEdit = edit,
            voxels = voxels,
        };
        return job.Schedule(VoxelUtils.Volume, 2048, dep);
    }
}

public class WorldEditTypeRegistry {
    internal byte count;
    internal Dictionary<byte, IWorldEditRegistry> dynamicEditRegistries;
    internal Dictionary<Type, byte> lookup;

    public void Register<T>() where T: struct, IWorldEdit {
        dynamicEditRegistries.Add(count, new TypedContainer<T> {
            list = new List<T>()
        });

        lookup.Add(typeof(T), count);
        count++;
    }

    public void Add<T>(T edit) where T: IWorldEdit {
        byte type = lookup[typeof(T)];
        List<T> list = (List<T>)dynamicEditRegistries[type].InternalList();
        list.Add(edit);
    }
}

// Dictionary<Type, Container>
// Container -> Interface
// Interface -> List<T>

// Dynamic edit container for a specific type of dynamic edit
// This is what is actually sent over the network

public interface IWorldEditRegistry {
    public void NetworkSerialize<T1>(BufferSerializer<T1> serializer) where T1 : IReaderWriter;
    public IList InternalList();
}

struct TypedContainer<T> : IWorldEditRegistry where T : struct, IWorldEdit {
    internal List<T> list;

    public IList InternalList() {
        return list;
    }

    public void NetworkSerialize<T1>(BufferSerializer<T1> serializer) where T1 : IReaderWriter {
        if (serializer.IsReader) {
            list.Clear();
            serializer.GetFastBufferReader().ReadValue<T>(out T[] values);
            serializer.GetFastBufferReader().ReadValue<Test>(out Test test);
            list.AddRange(values);
        } else {
            serializer.GetFastBufferWriter().WriteValue(list.ToArray());
        }
    }

   
}

class Test : INetworkSerializable {
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        throw new NotImplementedException();
    }
}