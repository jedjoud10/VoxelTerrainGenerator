// Generated voxel data from the GPU
// Allows us to check if the readback has finished and if we can use the NativeArray
// Also allows us to Free the native array to give it back to the Voxel Generator for generation

public class VoxelReadbackRequest : VoxelTempContainer {
    public int Index { get; internal set; }
    public VoxelGenerator generator;

    // Dispose of the request's memory, giving it back to the VoxelGenerator
    public override void TempDispose() {
        generator.freeVoxelNativeArrays[Index] = true;
        generator.voxelNativeArrays[Index] = voxels;
        chunk = null;
    }
}