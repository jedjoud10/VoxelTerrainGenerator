using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class WorldEditTypeRegistry {
    internal Dictionary<Type, byte> lookup = new Dictionary<Type, byte>();
    internal List<IWorldEditRegistry> types = new List<IWorldEditRegistry>();

    // Register a new type of world edit that we can use going forward
    // The whole reason we have this registry system is so we would only need
    // To send a single byte to reference to a whole TYPE of world edits
    public void Register<T>() where T : struct, IWorldEdit {
        lookup.Add(typeof(T), (byte)types.Count);
        types.Add(new TypedThingy<T> {
            list = new List<T>()
        });
    }

    // Add a new world edit to the registry
    public int Add<T>(T worldEdit) where T : struct, IWorldEdit {
        byte type = lookup[typeof(T)];
        List<T> list = (List<T>)types[type].List;
        list.Add(worldEdit);
        return list.Count - 1;
    }

    // Retrieve the edit at the specific index and specific type
    public T Get<T>(int index) where T : struct, IWorldEdit {
        byte type = lookup[typeof(T)];
        List<T> list = (List<T>)types[type].List;
        return list[index];
    }

    public void Serialize(FastBufferWriter writer) {
        Debug.Log($"Serializing {types.Count} unique world edit types");
        writer.WriteByteSafe((byte)types.Count);

        foreach (var item in types) {
            item.Serialize(writer);
        }
    }

    public void Deserialize(FastBufferReader reader) {
        reader.ReadByteSafe(out byte count);
        Debug.Log($"Deserializing {count} unique world edit types");

        for (int i = 0; i < count; i++) {
            types[i].Deserialize(reader);
        }
    }

    internal List<Bounds> AllBounds() {
        List<Bounds> list = new List<Bounds>();
        foreach (var item in types) {
            item.AddBounds(ref list);
        }
        return list;
    }
}

// Dictionary<Type, Container>
// Container -> Interface
// Interface -> List<T>

// Dynamic edit container for a specific type of dynamic edit
// This is what is actually sent over the network

internal interface IWorldEditRegistry {
    public IList List { get; }
    public void AddBounds(ref List<Bounds> bounds);
    public void Serialize(FastBufferWriter writer);
    public void Deserialize(FastBufferReader reader);
}

// I love not giving a FUCK about naming conventions...
struct TypedThingy<T> : IWorldEditRegistry where T : struct, IWorldEdit {
    internal List<T> list;
    internal byte index;
    public IList List => list;



    public void Serialize(FastBufferWriter writer) {
        Debug.Log($"Serializing world edit type {typeof(T).Name}");
        writer.WriteByteSafe(index);
        writer.WriteValueSafe(list.ToArray());
    }

    public void Deserialize(FastBufferReader reader) {
        Debug.Log($"Deserializing world edit type {typeof(T).Name}");
        reader.ReadByteSafe(out byte tempIndex);

        if (tempIndex != index) {
            Debug.LogError("Mismatch in dynamic edit type!");
        }

        reader.ReadValueSafe(out T[] temp);
        list.Clear();
        list.AddRange(temp);
    }

    public void AddBounds(ref List<Bounds> bounds) {
        foreach (var item in list) {
            bounds.Add(item.GetBounds());
        }
    }
}