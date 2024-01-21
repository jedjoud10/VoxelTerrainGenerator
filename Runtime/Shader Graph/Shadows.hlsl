void shadowcaster_float(out bool _out) {
#ifdef UNITY_PASS_SHADOWCASTER
    _out = true;
#else
    _out = false;
#endif
}