# A procedural terrain generator that makes use of your GPU to generate fully volumetric terrain

## Features
* 3D Octree for worlds up to 32km with sub-meter voxel precision
* GPU Voxel Generation with Async CPU readback
* MUltithreaded and Jobified CPU meshing implementing the Surface Nets algorithm
  *  Supports vertex merging and custom materials
  *  Supports custom per vertex ambient occlusion and UV pass-through data
  *  Async Collision Baking using the job system and PhysX
  *  Custom skirts system for meshes with different resolution to avoid gaps between chunks
  *  Fully parallel, runs under 6ms for a 64x64x64 mesh on my machine for 8 threads 
* Terrain editing using duplicate octree
  * Supports "dynamic" edits which are applied on a global scale (non-destructive)
  * Supports "voxel" edits which are applied on a local voxel-to-voxel scale
* GPU Based Prop Generation
  * Avoids unecessary CPU callbacks
  * Uses density and surface data to generate props
  * Multiple prop "variants" supported
  * Uses the GPU for indirect instanced rendering directly without CPU callback
  * Uses billboard system for props that are further away
    * Billboard captures are done automatically at the start of the frame
    * Makes use of albedo, mask, and normal map data to create billboards procedurally
* Serialization / deserialization system that supports terrain edits, terrain seed, and modified/destoyed props
  * Uses RLE and delta compression for voxel data
  * Uses RLE for prop masks

Showcase:
![image](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/506140cb-6bd8-4c07-a3aa-9438115872b1)
![image](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/8b0d434b-0d18-4e3c-806d-a9ceb16e024c)
![image](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/5291314d-16da-420f-8a26-cda33c42060d)
![image](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/1fedfe2e-fc9e-4672-bbfa-dd413d86448d)
