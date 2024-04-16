// Simple noise class that allows us to write and combine noise types to evaluate them
public class Noise {
    public enum Type {
        Simplex,
        Perlin,
        VoronoiF1,
        VoronoiF2,
        Erosion
    }

    public Noise(Var<float> intensity, Var<float> scale) {
        this.intensity = intensity;
        this.scale = scale;
    }

    public Type type;
    public Var<float> intensity;
    public Var<float> scale;

    // Evaluate the noise at the specific point
    public virtual Var<float> Evaluate<T>(Var<T> position) { return 0.0f; }
}

// Fractal noise is a type of noise that implement fBm (either Ridged, Billow, or Sum mode)
public class FractalNoise : Noise {
    public enum FractalMode {
        Ridged,
        Billow,
        Sum,
    }


    public FractalMode mode;
    public float lacunarity;
    public float persistence;
    public int octaves;

    public FractalNoise(Var<float> intensity, Var<float> scale) : base(intensity, scale) {
    }

    public override Var<float> Evaluate<T>(Var<T> position) {
        return base.Evaluate(position);
    }
}
