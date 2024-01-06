using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Custom serialization code that I honestly think should be supported by default but wtv
public static class SerializationExtensions {
    /*
    public static void ReadValueSafe<TKey, TValue>(this FastBufferReader reader, ref NativeHashMap<TKey, TValue> dict)
    where TKey : unmanaged, IEquatable<TKey>
    where TValue : unmanaged {
    }

    public static void WriteValueSafe<TKey, TValue>(this FastBufferWriter writer, in NativeHashMap<TKey, TValue> dict)
        where TKey : unmanaged, IEquatable<TKey>, INetworkSerializeByMemcpy
        where TValue : unmanaged, INetworkSerializeByMemcpy {
        int size;
        unsafe {
            size = sizeof(TKey) + sizeof(TValue);
        };
        writer.TryBeginWrite(dict.Count * size + sizeof(int));
        NativeKeyValueArrays<TKey, TValue> keyValueArray = dict.GetKeyValueArrays(Allocator.Temp);

        writer.WriteValue(dict.Count);
        for (int i = 0; i < dict.Count; i++) {
            TKey key = keyValueArray.Keys[i];
            TValue val = keyValueArray.Values[i];
            writer.WriteValue(key);
            writer.WriteValue(val);
        }
    }

    public static void ReadValueSafe<T>(this FastBufferReader reader, ref NativeList<T> list) where T : unmanaged {
    }

    public static void WriteValueSafe<T>(this FastBufferWriter writer, in NativeList<T> list) where T : unmanaged {
    }
    */
}