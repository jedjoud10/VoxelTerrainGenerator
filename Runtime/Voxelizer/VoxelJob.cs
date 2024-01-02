using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
public struct VoxelJob : IJobParallelFor {
    [WriteOnly]
    public NativeArray<Voxel> voxels;
    public float3 chunkOffset;
    public float chunkScale;
    
    public void Execute(int index) {
        uint3 pos = VoxelUtils.IndexToPos(index, 66);
        float3 position = pos;
        position += -math.float3(0.5f);
        position *= chunkScale;
        position += chunkOffset;
        //int3 editPosShit = (int3)math.floor(position);
        int3 editPosShit = (int3)math.floor(((float3)pos - math.float3(0.8f)) * chunkScale + chunkOffset);
        Voxel voxel = Voxel.Empty;
        //voxel.density = (half)(noise.snoise(position * 0.02f) * 10 + position.y);
        //voxel.density = (half)(math.min(position.y, math.distance(position, float3.zero) - 10));
        voxel.density = (half)position.y;
        voxel.density = (half)(math.min(voxel.density, SimulateEdit(editPosShit)));
        voxel.material = 0;
        voxels[index] = voxel;
    }

    private float SimulateEdit(int3 pos) {
        return math.distance(pos + math.float3(0, 0, 0), float3.zero) - 10;
    }
}