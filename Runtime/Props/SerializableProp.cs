using Unity.Collections;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

// Custom prop interface that we can extend to implement custom prop saving/loading logic
// All data that must be persistently saved should be saved with the prop system
// The prop system will *not* make use of the serializable registry system
// because it would force us to create another layer of indirection (to uphold the struct contraint)
// So I'm just keeping the prop data saved as raw bytes and decompressing it only when needed
public abstract class SerializableProp : MonoBehaviour, INetworkSerializable {
    // Set the prop as modified, forcing us to serialize it
    public bool wasModified = false;

    // Dispatch group ID bitmask index used for loading and saving 
    public int ElementIndex { internal set; get; } = -1;

    // Stride of the byte data we will be writing
    public abstract int Stride { get; }

    // Variant of the prop type
    public int Variant { get; internal set; }

    // Called when the fake gameobject for capturing gets spawned
    public virtual void OnSpawnCaptureFake(Camera camnera, Texture2DArray[] renderedTextures, int variant) { }

    // Called when the fake gameobject for capturing gets destroyed
    public virtual void OnDestroyCaptureFake() { }

    // When a new prop spawns :3
    public virtual void OnPropSpawn(BlittableProp prop) { }

    // Serialization / deserialization on a per prop basis
    public abstract void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter;
}