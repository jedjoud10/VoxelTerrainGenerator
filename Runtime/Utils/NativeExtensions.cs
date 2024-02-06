using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;

public static class NativeExtensions {
    // FOR FUCKS SAKE UNITY WHY ARE YOU SO FUCKING ANNOYING
    // Why on GOD's GREEN FUCKING EARTH DO I NEED TO TO DO THIS SHIT I FUCKING HATE YOU GO KILL YOURSELF
    // (all love no hate)
    // TODO: Submit bug report
    public static NativeArray<T> AsNativeArrayExt<T>(this NativeBitArray self) where T : unmanaged {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle handle = NativeBitArrayUnsafeUtility.GetAtomicSafetyHandle(self);
#endif
        var arr = self.AsNativeArray<T>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        NativeBitArrayUnsafeUtility.SetAtomicSafetyHandle(ref self, handle);
#endif
        return arr;
    }
}