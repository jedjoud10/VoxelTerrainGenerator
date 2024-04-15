# A procedural terrain generator that makes use of your GPU to generate fully volumetric terrain

## Features
* 3D Octree for worlds up to 32km with sub-meter voxel precision
* GPU Voxel Generation with Async CPU readback
* Morton encoding to improve CPU cache locality (didn't actually test but I just implemented it for the funny)
* Multithreaded and Jobified CPU meshing implementing the Surface Nets algorithm
  *  Supports vertex merging and custom materials
  *  Supports custom per vertex ambient occlusion and UV pass-through data
  *  Async Collision Baking using the job system and PhysX
  *  Custom skirts system for meshes with different resolution to avoid gaps between chunks (kinda works)
* Terrain editing using duplicate octree
  * Supports dynamic edits which are applied on a global scale (non-destructive)
  * Supports voxel edits which are applied on a local voxel-to-voxel scale
  * Generic dynamic edits/ voxel edits allowing you to write your own editing shapes and brushes (using Job system as well) 
  * Callback for voxel edits to detect how much volume was added/removed for each material type
  * Custom frame limit to limit number of in-flight meshing jobs to reduce latency
* GPU Based Prop Generation
  * Avoids unecessary CPU callbacks
  * Uses density and surface data to generate props
  * Multiple prop "variants" supported
  * Uses the GPU for indirect instanced rendering directly
  * Uses billboard system for props that are further away
    * Billboard captures are done automatically at the start of the frame
    * Makes use of albedo, mask, and normal map data to create billboards procedurally
* Serialization / deserialization system that supports terrain edits, terrain seed, and modified/destoyed props
  * Uses RLE and delta compression for voxel data
  * Uses RLE for prop masks
* In editor SDF/Volume/Slice preview using unity Handles API 
 
 ## WIP Features to be added
  * Structure generation
  * Mess around with multiple camera angles for props
  * Custom prop spawning / modifiers (using CSG)
  * Optimize rendering & voxel editing
  * Better compresion ratio for saved worlds
  * Better compression algorithms for props and voxel data
  * Better lighting effects (AO/GI)
  * Voxel Occlusion culling for props and terrain chunks
  * Fully GPU-driven voxel chunks using indirect draw
    * Maybe mess around with nvidia mesh/task shaders?
    * Compute based fallback for chunks further away, to reduce readback
  * Voxel graph / interpreter to create voxel terrains in C# or visually
    * Full world biome generation (big low-res 3d texture)
    * Per-biome localized volumetric fog
  * Multiplayer support (theoretically should be easy)
    * Just need to share seed to all clients
    * And whenever we do a new edit, send an edit "request" to all clients who need it
    * Apply delta compression for edits and possible send the whole dupe octree sometimes
    * For props since they implement INetworkSerializable you just need to share their values 

## Main issues
  * Still riddled with bugs
    * Editing terrain sometimes leaves gaps
    * Prop generation sometimes breaks out of nowhere
  * Terrain chunk scheduling is non-conservative. Always over-estimates the amount of chunks _actually_ containing terrain
  * Bad performance when editing large voxel/dynamic edits (due to the dupe-octree nature of voxel edits)
  * Slow async GPU readback which causes frame time spikes when there is more than 1 request per frame
  * Billboarded prop normals don't seem to match up with their gameobject counterpart
  * Floating terrain segments (could fix by running a flood fill and seeing the parts that aren't "connected")
  * Floating props (due to low-resolution segment voxel grid)

## Showcase:
![image](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/506140cb-6bd8-4c07-a3aa-9438115872b1)
![image](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/8b0d434b-0d18-4e3c-806d-a9ceb16e024c)
![image](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/5291314d-16da-420f-8a26-cda33c42060d)
![image](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/1fedfe2e-fc9e-4672-bbfa-dd413d86448d)
![image](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/51a05c97-5f5b-4822-901b-3aac0f442a42)


## In Editor Previews
![Screenshot 2024-04-08 135853](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/c719561f-05d4-4b1a-9e6c-fae8e4e29cb8)
![Screenshot 2024-04-08 141419](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/3228c033-1ef9-4d56-bf6d-5efa8a58177f)
![2222image](https://github.com/jedjoud10/VoxelTerrainGenerator/assets/34755598/a736877a-9a96-4212-9bd7-634db644438f)
