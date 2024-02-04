using Unity.Collections;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

public class DefaultSerializableProp : SerializableProp {
    public override int Stride => sizeof(int);
    public int data;

    public override void NetworkSerialize<T>(BufferSerializer<T> serializer) {
        serializer.SerializeValue(ref data);
    }
}