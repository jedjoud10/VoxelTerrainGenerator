using Codice.Client.BaseCommands.BranchExplorer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class SerializableRegistry {
    internal Dictionary<Type, byte> lookup;
    internal List<IRegistryType> types;

    public SerializableRegistry() {
        lookup = new Dictionary<Type, byte>();
        types = new List<IRegistryType>();
    }

    // Register a new type that we can use going forward
    public void Register<T>() where T : struct, INetworkSerializable {
        lookup.Add(typeof(T), (byte)types.Count);
        types.Add(new TypedThingy2<T> {
            list = new List<T>()
        });
    }

    // Add a new object to the registry
    public int Add<T>(T val) where T : struct, INetworkSerializable {
        byte type = lookup[typeof(T)];
        List<T> list = (List<T>)types[type].List;
        list.Add(val);
        return list.Count - 1;
    }

    // Retrieve the edit at the specific index and specific type
    public T Get<T>(int index) where T : struct, INetworkSerializable {
        byte type = lookup[typeof(T)];
        List<T> list = (List<T>)types[type].List;
        return list[index];
    }

    public void Serialize(FastBufferWriter writer) {
        Debug.Log($"Serializing {types.Count} registry types");
        writer.WriteByteSafe((byte)types.Count);

        foreach (var item in types) {
            item.Serialize(writer);
        }
    }

    public void Deserialize(FastBufferReader reader) {
        reader.ReadByteSafe(out byte count);
        Debug.Log($"Deserializing {count} registry types");

        for (int i = 0; i < count; i++) {
            types[i].Deserialize(reader);
        }
    }

    // Tries to get the inner registry values of values of a specific type
    // Casts each IList object to T internally
    // TODO: Optimize
    public List<T> TryGetAll<T>() {
        List<T> total = new List<T>();
        foreach (var reg in types) {
            if (typeof(T).IsAssignableFrom(reg.Inner)) {
                total.AddRange(reg.Enumerable.Select(x => (T)x));
            }
        }
        return total;
    }
}


public class SerializableManualRegistry {
    internal Dictionary<byte, IRegistryType> types;

    public SerializableManualRegistry() {
        types = new Dictionary<byte, IRegistryType>();
    }

    // Register a new type that we can use going forward
    public void Register<T>(byte index) where T : struct, INetworkSerializable {
        types.Add(index, new TypedThingy2<T> {
            list = new List<T>()
        });
    }

    // Add a new object to the registry
    public int Add<T>(byte index, T val) where T : struct, INetworkSerializable {
        List<T> list = (List<T>)types[index].List;
        list.Add(val);
        return list.Count - 1;
    }

    // Retrieve the edit at the specific index and specific type
    public T Get<T>(byte typeIndex, int elementIndex) where T : struct, INetworkSerializable {
        List<T> list = (List<T>)types[typeIndex].List;
        return list[elementIndex];
    }

    public void Serialize(FastBufferWriter writer) {
        Debug.Log($"Serializing {types.Count} manual registry types");
        writer.WriteByteSafe((byte)types.Count);

        foreach (var item in types) {
            item.Value.Serialize(writer);
        }
    }

    public void Deserialize(FastBufferReader reader) {
        reader.ReadByteSafe(out byte count);
        Debug.Log($"Deserializing {count} manual registry types");

        for (int i = 0; i < count; i++) {
            types[(byte)i].Deserialize(reader);
        }
    }
}

internal interface IRegistryType {
    public IList List { get; }
    public byte Index { get; }
    public Type Inner { get; }
    public IEnumerable<object> Enumerable { get; }
    public void Serialize(FastBufferWriter writer);
    public void Deserialize(FastBufferReader reader);
}

struct TypedThingy2<T> : IRegistryType where T : struct, INetworkSerializable {
    internal List<T> list;
    internal byte index;
    public Type Inner => typeof(T);
    public byte Index => index;
    public IList List => list;
    public IEnumerable<object> Enumerable => ((IEnumerable<T>)list).Select(x => (object)x);

    public void Serialize(FastBufferWriter writer) {
        Debug.Log($"Serializing list of type {typeof(T).Name}");
        writer.WriteByteSafe(index);
        writer.WriteValueSafe(list.ToArray());
    }

    public void Deserialize(FastBufferReader reader) {
        Debug.Log($"Deserializing list of type {typeof(T).Name}");
        reader.ReadByteSafe(out byte tempIndex);

        if (tempIndex != index) {
            Debug.LogError("Mismatch in type! Backwards compat isn't implemented yet. Bruhtonium?");
        }

        reader.ReadValueSafe(out T[] temp);
        list.Clear();
        list.AddRange(temp);
    }
}