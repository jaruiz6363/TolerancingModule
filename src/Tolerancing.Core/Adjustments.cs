using AberrationCalculator.Core.RayTrace;
using WavefrontErrorCalculator.Core;

namespace Tolerancing.Core;

/// <summary>
/// One adjustment a compensator may make after the lens is built, beside its focus: an air space,
/// an element's or a group's tilt or decenter in its cell, the image plane's tilt. Each is set from
/// zero, the nominal lens's setting, by an amount in its own units, and may be held within a travel.
/// </summary>
public abstract record Adjustment
{
    /// <summary>How the adjustment reads in a report: "space 5", "tilt x 6-7", "image tilt y".</summary>
    public abstract string Label { get; }

    public abstract string Units { get; }

    /// <summary>The most it may move either way from zero, in its units; null for no limit.</summary>
    public double? Travel { get; init; }

    /// <summary>A step the size of a small part of a typical move, for the solver's derivatives.</summary>
    public abstract double Step { get; }

    /// <summary>Makes the adjustment on a lens with its errors already applied.</summary>
    public abstract void Apply(PerturbedLens lens, double amount);
}

/// <summary>An air space (or any thickness) after <see cref="Surface"/>, longer by the amount, in lens units.</summary>
public sealed record SpaceAdjustment(int Surface) : Adjustment
{
    public override string Label => $"space {Surface}";
    public override string Units => "mm";
    public override double Step => 1e-3;

    public override void Apply(PerturbedLens lens, double amount) => lens.System.Surfaces[Surface].Thickness += amount;
}

/// <summary>An element or cemented group, surfaces first to last, tilted in its cell about its first vertex, in degrees: as <see cref="ElementTilt"/>.</summary>
public sealed record ElementTiltAdjustment(int First, int Last, Axis Axis) : Adjustment
{
    public override string Label => $"tilt {(Axis == Axis.X ? "x" : "y")} {First}-{Last}";
    public override string Units => "deg";
    public override double Step => 1e-3;

    public override void Apply(PerturbedLens lens, double amount) => new ElementTilt(First, Last, Axis).Apply(lens, amount);
}

/// <summary>An element or cemented group, surfaces first to last, shifted across the axis in its cell, in lens units: as <see cref="ElementDecentre"/>.</summary>
public sealed record ElementDecenterAdjustment(int First, int Last, Axis Axis) : Adjustment
{
    public override string Label => $"decenter {(Axis == Axis.X ? "x" : "y")} {First}-{Last}";
    public override string Units => "mm";
    public override double Step => 1e-3;

    public override void Apply(PerturbedLens lens, double amount) => new ElementDecentre(First, Last, Axis).Apply(lens, amount);
}

/// <summary>
/// The image plane - a detector - tilted about its center, in degrees: about x, its +y edge moves
/// away from the lens; about y, its +x edge. See <see cref="ImagePlane"/> for how it is evaluated.
/// </summary>
public sealed record ImageTiltAdjustment(Axis Axis) : Adjustment
{
    public override string Label => $"image tilt {(Axis == Axis.X ? "x" : "y")}";
    public override string Units => "deg";
    public override double Step => 1e-3;

    public override void Apply(PerturbedLens lens, double amount)
    {
        var image = lens.System.Surfaces[^1];
        double a = amount * Math.PI / 180.0;
        if (Axis == Axis.X) image.TiltX += a; else image.TiltY += a;
    }
}

/// <summary>
/// A tilted image plane, as the criteria evaluate it. AberrationCalculator traces to a flat image
/// plane across the axis and does not read the image surface's tilt. The module keeps an image
/// tilt there, θx and θy in radians, as the plane z = y tan θx + x tan θy, and evaluates each field
/// with its image plane moved along the axis to where the tilted plane meets that field's chief
/// ray, by WavefrontErrorCalculator's <see cref="WavefrontOptions.FocusShift"/>. The plane turns about
/// the image center, where the axial field's chief ray lands, as a detector turns about its middle.
///
/// <para>For an RMS wavefront that is exact to first order in the tilt: the wavefront is referred
/// to a sphere about the chief ray's image point, which depends on where the plane meets the chief
/// ray, not on the plane's slope there. For a spot, an MTF, it leaves out the plane's slope across
/// the spot, which is second order. The chief ray's height is taken at the untilted plane.</para>
/// </summary>
public static class ImagePlane
{
    /// <summary>The options for field <paramref name="field"/>, with the image plane moved to the tilted plane's height there; the options themselves when the plane is not tilted.</summary>
    public static WavefrontOptions For(LensModel lens, int field, WavefrontOptions options)
    {
        var image = lens.System.Surfaces[^1];
        double tx = image.TiltX, ty = image.TiltY;
        if (tx == 0.0 && ty == 0.0) return options;
        RealRayTrace.Landing Chief(double f) => RealRayTrace.Trace(lens.System, lens.Indices(lens.PrimaryWavelength), lens.Paraxial,
                                                                  f, 0.0, 0.0, atParaxialFocus: false);
        var hit = Chief(lens.System.Fields[field].Y);
        if (!hit.Ok) return options;
        // About the image center, where the axial field's chief ray lands: about the axis instead, an
        // image moved sideways by a decenter would turn a tilt into a focus shift of every field alike.
        var center = Chief(0.0);
        double cy = center.Ok ? center.Y : 0.0, cx = center.Ok ? center.Z : 0.0;
        double shift = (hit.Y - cy) * Math.Tan(tx) + (hit.Z - cx) * Math.Tan(ty);   // Z is the sagittal height, x
        return double.IsFinite(shift) ? options with { FocusShift = options.FocusShift + shift } : options;
    }
}
