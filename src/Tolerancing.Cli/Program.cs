using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tolerancing.Core;
using WavefrontErrorCalculator.Core;

namespace Tolerancing.Cli;

public static class Program
{
    private const string Usage =
        "usage: tol nominal <lens>                 first-order data and RMS wavefront error per field\n" +
        "       tol sensitivity <lens> [options]   each default tolerance at both ends, refocused\n" +
        "         --grade lowcost|commercial|precision|extraprecise   (commercial)\n" +
        "         --test-wavelength <µm>          wavelength fringes are counted at (0.6328)\n" +
        "         --preset <name>                 WavefrontErrorCalculator convention preset (reference)\n" +
        "         --primary                       primary wavelength only, not polychromatic\n" +
        "         --no-compensator                no refocusing\n" +
        "         --compensator <surface>         the thickness refocused, if not the last before the image\n" +
        "         --top <n>                       list only the n worst tolerances\n" +
        "       tol evaluate <lens> [options]      one perturbed lens, field by field and wavelength by wavelength\n" +
        "         --apply CODE:surface:amount     a tolerance, repeatable: TFRN TRAD TTHI TIND TABB TCON TSDX TSDY\n" +
        "                                         TSTX TSTY (deg); TEDX TEDY TETX TETY (deg) on first-last\n" +
        "         --export <file.zmx>             write the perturbed lens for OpticStudio\n" +
        "         --test-wavelength, --preset, --primary as above; --compensate to refocus\n" +
        "       tol statistics <lens> [options]    Koch's prediction for a tolerance set, checked by Monte Carlo\n" +
        "         --grade, --test-wavelength, --preset, --primary, --no-compensator, --compensator as for sensitivity\n" +
        "         --tolerances <file>             the set to use instead of the grade's: lines of CODE surfaces ±t,\n" +
        "                                         or CODE surfaces min max [uniform|endpoint|normal[:k]]\n" +
        "         --travel <mm>                   the compensator's travel either way; allocate also keeps to it\n" +
        "         --tree kind|element|none        the error budget printed (kind)\n" +
        "         --report <file.json>            everything, for drawings and other programs\n" +
        "         --write-tolerances <file>       the set (allocated, for allocate) as a tolerance file\n" +
        "         --export-trials <dir>           the first Monte Carlo lenses (up to 10) as .zmx, to check in OpticStudio\n" +
        "         --distribution uniform|endpoint|normal[:k]   each error, ±t = kσ for normal (uniform)\n" +
        "         --polar disc|ring|rayleigh[:k]  draw each x-y decenter or tilt as one error all around\n" +
        "         --cross wavefront|auto|all|pairs|none   cross terms evaluated (wavefront: an RMS wavefront's from\n" +
        "                                         its pupil slopes, untraced; auto: traced, all up to 80, else pairs)\n" +
        "         --criterion rms|efl|bfl|boresight|boresight-mm|mtf:field:cy/mm:s|t   what the lens is judged by (rms)\n" +
        "         --limit <value>                 the yield at this criterion: waves for rms, mm for efl and bfl,\n" +
        "                                         degrees for boresight (the line of sight), mm for boresight-mm (at\n" +
        "                                         the image), the least MTF for mtf (the geometric MTF, Kidger's\n" +
        "                                         summation, with no diffraction factor)\n" +
        "         --also <criterion>:<limit>      a further criterion and its limit, repeatable: reported, allocate\n" +
        "                                         meets it too, Monte Carlo counts lenses within every limit\n" +
        "         --joint                         allocate: the yield is of lenses within every limit at once\n" +
        "         --shape sampled|gamma|pearson|normal   the spread of W²: drawn on the model, or from its moments (sampled)\n" +
        "         --montecarlo <n> [--seed <s>]   n lenses traced in full to check the prediction\n" +
        "         --no-check                      skip tracing 12 lenses one by one against the quadratic model\n" +
        "       tol allocate <lens> --limit <value> [options]   size the default set to meet a criterion\n" +
        "         --method equal|cost             equal effect (Hilbert, Smith) or least cost (Adams) (equal)\n" +
        "         --yield <fraction>              the goal: this fraction of lenses within the limit (0.977)\n" +
        "         --refits <n>                    sensitivity reruns at the allocated values (1)\n" +
        "         --no-round                      keep values exact rather than preferred numbers\n" +
        "         and the statistics options\n" +
        "       tol drawings <lens> --tolerances <file> --out <dir> [options]   element drawings for vendors\n" +
        "         --standard iso|asme             ISO 10110, or ASME Y14.18 with MIL-PRF-13830B (iso)\n" +
        "         --settings <file.json>          cosmetics, irregularity, chamfers, coatings, title block\n" +
        "         --test-wavelength <µm>          as for the tolerances (0.6328)\n" +
        "         --adjust <kind>[:travel]        as for statistics: an element decentered or tilted to align the lens\n" +
        "                                         is drawn as set at assembly, not held to a mount tolerance\n" +
        "       tol settings <file.json>           write the drawing settings, every default, to edit";

    /// <summary>
    /// Reports carry µ, σ, √ and ², which a Windows console's code page (1252, 437) has not all of:
    /// write UTF-8 to the console or a redirected file alike, and leave the console as it was found.
    /// </summary>
    public static int Main(string[] args)
    {
        var encoding = Console.OutputEncoding;
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch (IOException) { }
        try { return Run(args); }
        finally { try { Console.OutputEncoding = encoding; } catch (IOException) { } }
    }

    private static int Run(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            if (args.Length == 2 && args[0] == "nominal") return Nominal(args[1]);
            if (args.Length >= 2 && args[0] == "sensitivity") return SensitivityCommand(args[1], args.Skip(2).ToArray());
            if (args.Length >= 2 && args[0] == "evaluate") return EvaluateCommand(args[1], args.Skip(2).ToArray());
            if (args.Length >= 2 && args[0] == "statistics") return StatisticsCommand(args[1], args.Skip(2).ToArray(), allocate: false);
            if (args.Length >= 2 && args[0] == "allocate") return StatisticsCommand(args[1], args.Skip(2).ToArray(), allocate: true);
            if (args.Length >= 2 && args[0] == "drawings") return DrawingsCommand(args[1], args.Skip(2).ToArray());
            if (args.Length == 2 && args[0] == "settings") { File.WriteAllText(args[1], new Tolerancing.Drawings.DrawingSettings().ToJson()); return 0; }
        }
        catch (Exception e) when (UserError(e) is { } error)
        {
            Console.Error.WriteLine($"tol: {error.Message}");
            return 1;
        }
        Console.Error.WriteLine(Usage);
        return 2;
    }

    /// <summary>
    /// `--adjust`: a compensator beside the focus - "space:S", "tilt:F-L", "decenter:F-L" or
    /// "image-tilt", each followed by ":travel" for a limit either way. A tilt, a decenter and the
    /// image tilt adjust in x and in y, as two adjustments.
    /// </summary>
    private static IEnumerable<Adjustment> ParseAdjust(string spec)
    {
        var p = spec.Split(':');
        string kind = p[0].Trim().ToLowerInvariant();
        double? TravelAt(int k) => p.Length > k && p[k].Length > 0 ? double.Parse(p[k], CultureInfo.InvariantCulture) : null;
        (int First, int Last) Range(string s)
        {
            var r = s.Split('-');
            int first = int.Parse(r[0], CultureInfo.InvariantCulture);
            return (first, r.Length > 1 ? int.Parse(r[1], CultureInfo.InvariantCulture) : first);
        }
        string Need(int k) => p.Length > k ? p[k] : throw new ArgumentException($"--adjust {spec}: {kind} needs its surfaces, as {kind}:6-7");
        switch (kind)
        {
            case "space":
                return new[] { new SpaceAdjustment(int.Parse(Need(1), CultureInfo.InvariantCulture)) { Travel = TravelAt(2) } };
            case "tilt":
            {
                var (f, l) = Range(Need(1));
                return new[] { new ElementTiltAdjustment(f, l, Axis.X) { Travel = TravelAt(2) }, new ElementTiltAdjustment(f, l, Axis.Y) { Travel = TravelAt(2) } };
            }
            case "decenter":
            {
                var (f, l) = Range(Need(1));
                return new Adjustment[] { new ElementDecenterAdjustment(f, l, Axis.X) { Travel = TravelAt(2) }, new ElementDecenterAdjustment(f, l, Axis.Y) { Travel = TravelAt(2) } };
            }
            case "image-tilt":
                return new[] { new ImageTiltAdjustment(Axis.X) { Travel = TravelAt(1) }, new ImageTiltAdjustment(Axis.Y) { Travel = TravelAt(1) } };
            default:
                throw new ArgumentException($"no adjustment {kind}: space, tilt, decenter or image-tilt");
        }
    }

    /// <summary>`--fields`: "symmetric" (or "y"), the file's fields and their mirror images at −y (the default); "xy", their images on the x axis as well; or "file", the file's alone.</summary>
    private static FieldSymmetry Fields(string value) => value.ToLowerInvariant() switch
    {
        "symmetric" or "y" => FieldSymmetry.Y,
        "xy" => FieldSymmetry.XY,
        "file" => FieldSymmetry.File,
        _ => throw new ArgumentException($"no fields {value}: symmetric, xy or file"),
    };

    /// <summary>
    /// The error to report to the user, or null for a bug that should show its stack: an error raised
    /// inside a parallel evaluation arrives wrapped, and is unwrapped here.
    /// </summary>
    private static Exception? UserError(Exception e)
    {
        if (e is AggregateException a) e = a.Flatten().InnerExceptions.FirstOrDefault() ?? e;
        return e is IOException or NotSupportedException or FormatException or InvalidDataException
                    or ArgumentException or InvalidOperationException or ArithmeticException ? e : null;
    }

    private static int Nominal(string path)
    {
        var lens = LensModel.Read(path);
        var n = NominalPerformance.Evaluate(lens);
        Console.WriteLine($"{Path.GetFileName(path)}: {lens.System.Surfaces.Count} surfaces");
        Console.WriteLine($"EFL {n.Efl:F4}   BFL {n.Bfl:F4}   F/# {n.FNumber:F3}   λ {n.WavelengthUm:F4} µm");
        Console.WriteLine("field        RMS (waves)   P-V (waves)   Strehl   vignetted");
        foreach (var f in n.Fields)
            Console.WriteLine(f.Failure != null
                ? $"{f.FieldValue,8:G6}   fails: {f.Failure}"
                : $"{f.FieldValue,8:G6}   {f.Rms,11:F5}   {f.PeakToValley,11:F5}   {(f.Strehl is double st ? st.ToString("F4") : "—"),6}   {f.Vignetted,9}");
        return 0;
    }

    private static int SensitivityCommand(string path, string[] options)
    {
        var grade = ShopGrade.Commercial;
        double testWavelength = PowerFringes.HeNe;
        var wavefront = WavefrontOptions.Reference;
        var symmetricFields = FieldSymmetry.Y;
        var adjustSpecs = new List<string>();
        bool polychromatic = true, compensate = true;
        int? focusSurface = null;
        int top = int.MaxValue;
        string criterionName = "rms";
        for (int i = 0; i < options.Length; i++)
        {
            string Next() => i + 1 < options.Length ? options[++i] : throw new ArgumentException($"{options[i]} needs a value");
            switch (options[i])
            {
                case "--grade": grade = Enum.Parse<ShopGrade>(Next(), ignoreCase: true); break;
                case "--test-wavelength": testWavelength = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--preset": wavefront = WavefrontOptions.Preset(Next()); break;
                case "--fields": symmetricFields = Fields(Next()); break;
                case "--adjust": adjustSpecs.Add(Next()); break;
                case "--primary": polychromatic = false; break;
                case "--no-compensator": compensate = false; break;
                case "--compensator": focusSurface = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--top": top = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--criterion": criterionName = Next(); break;
                default: throw new ArgumentException($"unknown option {options[i]}");
            }
        }

        var lens = NominalLens.Read(path, symmetricFields);
        var rms = Traceable(new RmsWavefrontCriterion { Options = wavefront, Polychromatic = polychromatic }, lens);
        var compensator = compensate ? (focusSurface is int fs ? new FocusCompensator(fs) : FocusCompensator.BackFocus(lens)) : null;
        if (adjustSpecs.Count > 0)
            compensator = compensator == null ? throw new ArgumentException("--adjust needs the focus compensator") : compensator with { Adjustments = adjustSpecs.SelectMany(ParseAdjust).ToList() };
        var criterion = Criteria.Create(criterionName, rms, lens, compensator);
        var items = DefaultTolerances.Create(lens, grade, testWavelength, compensator);

        var clock = Stopwatch.StartNew();
        var table = Sensitivity.Run(lens, items, criterion, compensator);
        clock.Stop();

        Console.WriteLine($"{Path.GetFileName(path)}: {items.Count} tolerances, {grade} grade, fringes at {testWavelength} µm");
        Console.WriteLine($"criterion   {table.Criterion}" + ReferenceText(criterion));
        BflNote(criterion, compensator, lens);
        Console.WriteLine(CompensatorsLine(compensator, lens, criterion,
                                           compensator == null ? "" : $", nominal moved {table.NominalCompensator:+0.000000;-0.000000} mm"));
        // An MTF is shown as an MTF, and its changes as changes of the MTF; the rows are in the same order, worst first.
        double Q(double w) => criterion.Quoted(w);
        Console.WriteLine($"nominal     {Q(table.Nominal):F6}   refocused {Q(table.NominalCompensated):F6}");
        Console.WriteLine();
        Console.WriteLine("type  surf        min        max   criterion@min     change   criterion@max     change    comp@min    comp@max");
        foreach (var r in table.WorstOffenders.Take(top))
            Console.WriteLine($"{r.Item.Tolerance.Code} {ToleranceSpec.Surfaces(r.Item.Tolerance),5} {r.Item.Min,10:G4} {r.Item.Max,10:G4}" +
                              $"   {Q(r.MinCriterion),13:F6} {Q(r.MinCriterion) - Q(table.NominalCompensated),10:+0.000000;-0.000000}" +
                              $"   {Q(r.MaxCriterion),13:F6} {Q(r.MaxCriterion) - Q(table.NominalCompensated),10:+0.000000;-0.000000}" +
                              $"  {r.MinCompensator,10:+0.0000;-0.0000}  {r.MaxCompensator,10:+0.0000;-0.0000}");
        Console.WriteLine();
        if (!criterion.IsFloor)
            Console.WriteLine($"RSS of the worse increases {table.RssIncrease:F6}; estimated criterion {table.Estimated:F6} (linear, independent: a ranking, not a yield)");
        Console.WriteLine($"{clock.Elapsed.TotalSeconds:F1} s");
        return 0;
    }

    private static int EvaluateCommand(string path, string[] options)
    {
        double testWavelength = PowerFringes.HeNe;
        var wavefront = WavefrontOptions.Reference;
        var symmetricFields = FieldSymmetry.Y;
        var adjustSpecs = new List<string>();
        bool polychromatic = true, compensate = false;
        string? export = null;
        var specs = new List<string>();
        for (int i = 0; i < options.Length; i++)
        {
            string Next() => i + 1 < options.Length ? options[++i] : throw new ArgumentException($"{options[i]} needs a value");
            switch (options[i])
            {
                case "--apply": specs.Add(Next()); break;
                case "--test-wavelength": testWavelength = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--preset": wavefront = WavefrontOptions.Preset(Next()); break;
                case "--fields": symmetricFields = Fields(Next()); break;
                case "--adjust": adjustSpecs.Add(Next()); break;
                case "--primary": polychromatic = false; break;
                case "--compensate": compensate = true; break;
                case "--export": export = Next(); break;
                default: throw new ArgumentException($"unknown option {options[i]}");
            }
        }

        var lens = NominalLens.Read(path, symmetricFields);
        var perturbed = lens.Perturb();
        var applied = specs.Select(s => ToleranceSpec.Parse(s, testWavelength)).ToList();
        foreach (var (tolerance, amount) in applied) tolerance.Apply(perturbed, amount);

        if (adjustSpecs.Count > 0 && !compensate) throw new ArgumentException("--adjust needs --compensate");
        var criterion = Traceable(new RmsWavefrontCriterion { Options = wavefront, Polychromatic = polychromatic }, lens);
        string compensation = "";
        if (compensate)
        {
            var adjusting = FocusCompensator.BackFocus(lens) with { Adjustments = adjustSpecs.SelectMany(ParseAdjust).ToList() };
            var c = adjusting.Solve(perturbed, criterion);
            compensation = $"   back focus moved {c.Change:+0.000000;-0.000000} mm" +
                           string.Concat(adjusting.Adjustments.Select((a, k) => $", {a.Label} {c.Adjustments![k]:+0.000000;-0.000000} {a.Units}"));
        }
        var model = perturbed.Build();
        var system = model.System;
        if (export != null) ZemaxExport.Write(perturbed, export);

        Console.WriteLine($"{Path.GetFileName(path)} {string.Join(" ", specs)}");
        Console.WriteLine($"EFL {model.Paraxial.Efl:F6}   BFL {model.Paraxial.Bfl:F6}   criterion {criterion.Evaluate(model):F6}{compensation}");
        Console.WriteLine("surf          radius     thickness   index@primary");
        for (int s = 1; s < system.Surfaces.Count - 1; s++)
        {
            var surf = system.Surfaces[s];
            Console.WriteLine($"{s,4} {surf.Radius,15:F9} {surf.Thickness,13:F9} {model.Indices(model.PrimaryWavelength)[s],15:F9}" +
                              (surf.IsPerturbed ? $"   decenter ({surf.DecenterX:G6}, {surf.DecenterY:G6}, {surf.DecenterZ:G6})  tilt ({surf.TiltX * 180 / Math.PI:G6}°, {surf.TiltY * 180 / Math.PI:G6}°)" : ""));
        }
        int primary = model.PrimaryWavelength;
        var sites = new FieldSites(model);
        // A field on the x axis is traced on the lens turned a quarter turn, and turned back here.
        string FieldName(int f) => Math.Abs(system.Fields[f].X) > 1e-12 ? $"x {system.Fields[f].X:G4}" : $"{system.Fields[f].Y:G4}";
        Console.WriteLine("field   chief ray at the image, primary λ, launched at the paraxial pupil center (x, y)");
        for (int f = 0; f < system.Fields.Count; f++)
        {
            var (on, at) = sites[f];
            var hit = AberrationCalculator.Core.RayTrace.RealRayTrace.Trace(on.System, on.Indices(primary), on.Paraxial,
                                                                            on.System.Fields[at].Y, 0.0, 0.0, atParaxialFocus: false);
            var (hx, hy) = ReferenceEquals(on, model) ? (hit.Z, hit.Y) : (hit.Y, -hit.Z);
            Console.WriteLine($"{FieldName(f),7}   {(hit.Ok ? $"{hx,14:F9} {hy,14:F9}" : "fails")}");
        }
        Console.WriteLine("field    wave(µm)      RMS (waves)");
        for (int f = 0; f < system.Fields.Count; f++)
            for (int w = 0; w < system.Wavelengths.Count; w++)
            {
                string rms;
                var (on, at) = sites[f];
                try { rms = WavefrontCalculator.Compute(on, at, w, ImagePlane.For(on, at, criterion.EffectiveOptions), criterion.Sampling).Statistics.RmsStandardDeviation.ToString("F6", CultureInfo.InvariantCulture); }
                catch (InvalidOperationException) { rms = "fails"; }
                Console.WriteLine($"{FieldName(f),7}   {system.Wavelengths[w].Value,9:F7}   {rms,14}");
            }
        return 0;
    }

    private static int StatisticsCommand(string path, string[] options, bool allocate)
    {
        var grade = ShopGrade.Commercial;
        double testWavelength = PowerFringes.HeNe;
        var wavefront = WavefrontOptions.Reference;
        var symmetricFields = FieldSymmetry.Y;
        var adjustSpecs = new List<string>();
        bool polychromatic = true, compensate = true, round = true;
        int? focusSurface = null;
        Distribution axis = Distribution.Uniform;
        RadialDistribution? polar = null;
        var cross = CrossTerms.Wavefront;
        double? limit = null, travel = null;
        double yield = Prediction.Yield97;
        var shape = SpreadShape.Sampled;
        int trials = 0, seed = 1, refits = 1;
        var method = AllocationMethod.EqualEffect;
        string? tolerances = null, report = null, writeTolerances = null, tree = "kind", exportTrials = null, plots = null;
        bool check = true, joint = false;
        string criterionName = "rms";
        var alsoSpecs = new List<(string Name, double Limit)>();
        for (int i = 0; i < options.Length; i++)
        {
            string Next() => i + 1 < options.Length ? options[++i] : throw new ArgumentException($"{options[i]} needs a value");
            double Number() => double.Parse(Next(), CultureInfo.InvariantCulture);
            switch (options[i])
            {
                case "--grade": grade = Enum.Parse<ShopGrade>(Next(), ignoreCase: true); break;
                case "--test-wavelength": testWavelength = Number(); break;
                case "--preset": wavefront = WavefrontOptions.Preset(Next()); break;
                case "--fields": symmetricFields = Fields(Next()); break;
                case "--adjust": adjustSpecs.Add(Next()); break;
                case "--primary": polychromatic = false; break;
                case "--no-compensator": compensate = false; break;
                case "--compensator": focusSurface = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--travel": travel = Number(); break;
                case "--tolerances": tolerances = Next(); break;
                case "--distribution": axis = ToleranceSpec.ParseDistribution(Next()); break;
                case "--polar": polar = ParseRadial(Next()); break;
                case "--cross": cross = Enum.Parse<CrossTerms>(Next(), ignoreCase: true); break;
                case "--limit": limit = Number(); break;
                case "--yield": yield = Number(); break;
                case "--shape": shape = Enum.Parse<SpreadShape>(Next(), ignoreCase: true); break;
                case "--montecarlo": trials = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--seed": seed = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--refits": refits = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--no-round": round = false; break;
                case "--no-check": check = false; break;
                case "--joint": joint = true; break;
                case "--tree": tree = Next().ToLowerInvariant(); break;
                case "--criterion": criterionName = Next(); break;
                case "--also": alsoSpecs.Add(ParseAlso(Next())); break;
                case "--report": report = Next(); break;
                case "--write-tolerances": writeTolerances = Next(); break;
                case "--export-trials": exportTrials = Next(); break;
                case "--plots": plots = Next(); break;
                case "--method":
                    method = Next().ToLowerInvariant() switch
                    {
                        "equal" => AllocationMethod.EqualEffect,
                        "cost" => AllocationMethod.CostOptimal,
                        var m => throw new ArgumentException($"no method {m}: equal or cost"),
                    };
                    break;
                default: throw new ArgumentException($"unknown option {options[i]}");
            }
        }
        if (allocate && limit == null) throw new ArgumentException("allocate needs --limit");
        if (joint && (!allocate || alsoSpecs.Count == 0)) throw new ArgumentException("--joint is for allocate with --also");
        if (travel != null && !compensate) throw new ArgumentException("--travel needs a compensator");
        if (plots != null && trials == 0) throw new ArgumentException("--plots needs --montecarlo: it draws the traced lenses");

        var lens = NominalLens.Read(path, symmetricFields);
        var rms = Traceable(new RmsWavefrontCriterion { Options = wavefront, Polychromatic = polychromatic }, lens);
        var compensator = compensate
            ? (focusSurface is int fs ? new FocusCompensator(fs) : FocusCompensator.BackFocus(lens)) with { Travel = travel }
            : null;
        if (adjustSpecs.Count > 0)
            compensator = compensator == null ? throw new ArgumentException("--adjust needs the focus compensator") : compensator with { Adjustments = adjustSpecs.SelectMany(ParseAdjust).ToList() };
        var criterion = Criteria.Create(criterionName, rms, lens, compensator);
        if (compensator?.Travel != null) compensator = compensator.Centred(lens, criterion);
        // Limits are given as each criterion quotes them - an MTF floor for an MTF - and held on W.
        double? limitW = limit is double lq ? criterion.FromQuoted(lq) : null;
        var also = alsoSpecs.Select(s =>
        {
            var c = Criteria.Create(s.Name, rms, lens, compensator);
            return new CriterionLimit(c, c.FromQuoted(s.Limit));
        }).ToList();
        string Bound(ICriterion c, double w) => c.IsFloor ? $"at least {c.Quoted(w):G4}" : $"within {w:G4} {c.Units}";
        string Level(ICriterion c, double w) => c.Quoted(w).ToString("F5", CultureInfo.InvariantCulture);
        foreach (var twice in also.Select(a => a.Criterion.Name).Prepend(criterion.Name).GroupBy(n => n).Where(g => g.Count() > 1))
            throw new ArgumentException($"{twice.Key} is named twice: once, as --criterion or --also, is enough");
        var alsoCriteria = also.Select(a => a.Criterion).ToList();
        var alsoLimits = also.Select(a => a.Limit).ToList();
        var items = tolerances != null
            ? ToleranceSpec.ParseSet(File.ReadAllText(tolerances), testWavelength)
            : DefaultTolerances.Create(lens, grade, testWavelength, compensator);
        var statistics = new StatisticsModel(axis, polar);
        var clock = Stopwatch.StartNew();

        Console.WriteLine($"{Path.GetFileName(path)}: {items.Count} tolerances, " +
                          (tolerances != null ? $"from {Path.GetFileName(tolerances)}" : $"{grade} grade to start") +
                          $", fringes at {testWavelength} µm");
        Console.WriteLine($"criterion   {criterion.Name}" + ReferenceText(criterion));
        Console.WriteLine(CompensatorsLine(compensator, lens, criterion));
        BflNote(criterion, compensator, lens);
        foreach (var a in also)
        {
            Console.WriteLine($"also        {a.Criterion.Name} {Bound(a.Criterion, a.Limit)}" + ReferenceText(a.Criterion));
            BflNote(a.Criterion, compensator, lens);
        }
        Console.WriteLine($"errors     {Report.Describe(statistics)}; cross terms {cross.ToString().ToLowerInvariant()}");
        if (alsoCriteria.Prepend(criterion).Any(c => c.IsFloor))
            Console.WriteLine("note        an MTF is modeled as W² = 1 − MTF: its W² terms below are MTF drops");

        QuadraticModel model;
        Prediction prediction;
        var alsoPredictions = new List<Prediction>();
        var alsoModels = new List<QuadraticModel>();
        double? jointYield = null;
        AllocationResult? allocation = null;
        if (allocate)
        {
            var goal = new AllocationGoal(limitW!.Value, yield, shape)
            {
                Travel = travel, Joint = joint,
                AdjustmentTravels = compensator?.Adjustments.Select(x => x.Travel).ToList() ?? new List<double?>(),
            };
            var limits = ShopLimits.FromGrades(lens, items);
            var budget = Budget.Allocate(lens, items, limits, criterion, compensator, goal, statistics, method, cross, refits, round, also);
            allocation = budget.Allocation;
            var a = allocation;
            // The models at the allocated values, as the prediction is.
            var widths = Koch.HalfWidths(a.Items);
            model = Koch.WithHalfWidths(budget.Model, widths);
            alsoModels = budget.AlsoModels.Select(m => Koch.WithHalfWidths(m, widths)).ToList();
            prediction = a.Prediction;
            alsoPredictions = a.Also.ToList();
            jointYield = a.JointYield;
            items = a.Items.ToList();
            Console.WriteLine($"goal        {yield:P1} of lenses {Bound(criterion, limitW.Value)} ({shape.ToString().ToLowerInvariant()} spread)" +
                              (travel is double t2 ? $" and refocused within ±{t2:G4} mm" : "") +
                              string.Concat(also.Select(x => $" and {Bound(x.Criterion, x.Limit)} {x.Criterion.Name}")) + $"   method {method}   refits {refits}");
            if (joint)
                Console.WriteLine($"            {yield:P1} within every limit at once: each criterion held to {a.MarginalYield:P2}" +
                                  (a.Feasible && a.JointYield < yield ? $", which still leaves only {a.JointYield:P1} within them all" : ""));
            if (!a.Feasible)
            {
                var missed = new[] { (Criterion: criterion, Level: a.Prediction.Quantile(a.MarginalYield), Limit: limitW.Value) }
                    .Concat(also.Select((x, c) => (Criterion: x.Criterion, Level: a.Also[c].Quantile(a.MarginalYield), Limit: x.Limit)))
                    .Where(m => m.Level > m.Limit)
                    .Select(m => $"{m.Criterion.Name} {m.Criterion.Quoted(m.Level):G4} {m.Criterion.Units} against {m.Criterion.Quoted(m.Limit):G4}".Replace("  ", " ")).ToList();
                if (travel is double tm && a.Travel is { } tp && tp.Needed(yield) > tm) missed.Add($"travel ±{tp.Needed(yield):G4} mm against ±{tm:G4}");
                for (int k = 0; k < a.AdjustmentTravels.Count; k++)
                    if (compensator!.Adjustments[k].Travel is double reach && a.AdjustmentTravels[k].Travel.Needed(yield) > reach)
                        missed.Add($"{a.AdjustmentTravels[k].Label} ±{a.AdjustmentTravels[k].Travel.Needed(yield):G4} against ±{reach:G4}");
                Console.WriteLine("            NOT FEASIBLE: every tolerance at its tightest still misses the goal" +
                                  (missed.Count > 0 ? ": " + string.Join("; ", missed) : ""));
            }
            Console.WriteLine();
            Console.WriteLine("type  surf          min          max      tightest       loosest   mean of W²     σ² of W²");
            var byTerm = prediction.Contributions.SelectMany(c => c.Terms.Select(t => (t, c))).ToDictionary(p => p.t, p => p.c);
            foreach (var g in Allocator.AllocationGroups(items))
            {
                int i = g[0];
                var item = items[i];
                var shares = g.Select(k => byTerm[k]).Distinct().ToList();
                string code = g.Length == 2 ? item.Tolerance.Code[..^1] + "*" : item.Tolerance.Code;
                string flag = Adjusted(item.Tolerance, compensator) ? " adjusted" : a.AtTightest(i) ? " tightest" : a.AtLoosest(i) ? " loosest" : "";
                Console.WriteLine($"{code} {ToleranceSpec.Surfaces(item.Tolerance),5} {item.Min,12:G4} {item.Max,12:G4}  {a.Limits[i].Tightest,12:G4}  {a.Limits[i].Loosest,12:G4}" +
                                  $"  {shares.Sum(c => c.Mean),11:E3}  {shares.Sum(c => c.Variance),11:E3}{flag}");
            }
            var groups = Allocator.AllocationGroups(items);
            if (groups.Any(g => g.Length == 2))
                Console.WriteLine("* an x-y pair, such as TST* for TSTX and TSTY: one error in some direction, given one value; " +
                                  "its mean and σ² are the two together");
            if (groups.Any(g => Adjusted(items[g[0]].Tolerance, compensator)))
                Console.WriteLine("adjusted: an error a compensator takes out, so its value does not matter");
            Console.WriteLine($"cost (Adams, Σ tightest/t)  {a.Cost:F2}");
        }
        else
        {
            var models = QuadraticModel.Build(lens, items, alsoCriteria.Prepend(criterion).ToList(), compensator, cross);
            model = models[0];
            alsoModels = models.Skip(1).ToList();
            prediction = Koch.Predict(model, statistics) with { Shape = shape };
            if (also.Count > 0)
            {
                alsoPredictions = alsoModels.Select(m => Koch.Predict(m, statistics) with { Shape = shape }).ToList();
                if (limitW is double lj)
                    jointYield = Koch.JointYield(alsoModels.Prepend(model).ToList(), alsoLimits.Prepend(lj).ToList(), statistics);
            }
            Console.WriteLine();
            Console.WriteLine("largest contributions to the mean of W² and its variance");
            Console.WriteLine("tolerance           mean of W²     σ² of W²");
            foreach (var c in prediction.Contributions.OrderByDescending(c => c.Mean + Math.Sqrt(Math.Max(0.0, c.Variance))).Take(12))
                Console.WriteLine($"{c.Label,-16} {c.Mean,13:E3} {c.Variance,12:E3}");
        }
        var travelPrediction = TravelPrediction.Of(model, statistics);
        foreach (var m in alsoModels.Prepend(model))
        {
            string Pairs(IReadOnlyList<(int I, int J)> pairs) =>
                string.Join(", ", pairs.Take(4).Select(p => $"{Code(m.Terms[p.I].Item)} with {Code(m.Terms[p.J].Item)}")) +
                (pairs.Count > 4 ? $" and {pairs.Count - 4} more" : "");
            static string Code(ToleranceItem t) => $"{t.Tolerance.Code} {ToleranceSpec.Surfaces(t.Tolerance)}";
            string of = alsoModels.Count > 0 ? $" ({m.Criterion})" : "";
            if (m.PartlyTraced.Count > 0)
                Console.WriteLine($"note        {m.PartlyTraced.Count} pair(s) could not be traced at both maxima, so their cross terms were taken nearer nominal{of}: {Pairs(m.PartlyTraced)}");
            if (m.Untraced.Count > 0)
                Console.WriteLine($"WARNING: {m.Untraced.Count} pair(s) could not be traced together even at a quarter of their maxima; their cross terms are left out{of}: " +
                                  $"{Pairs(m.Untraced)}. The tolerances are too loose for this lens: tighten them, and confirm with --montecarlo");
        }

        if (tree is "kind" or "element")
        {
            var root = tree == "kind" ? BudgetTree.ByKind(items) : BudgetTree.ByElement(lens, items);
            Console.WriteLine();
            Console.WriteLine(criterion.IsFloor
                ? $"error budget by {tree}: mean and σ added to W², and each node alone as the MTF drop mean + 2σ"
                : $"error budget by {tree}: mean and σ added to W², and each node alone as √(mean + 2σ) {criterion.Units}");
            PrintNode(BudgetTree.Evaluate(root, prediction), 0, criterion.IsFloor);
        }
        else if (tree != "none") throw new ArgumentException($"no tree {tree}: kind, element or none");

        Console.WriteLine();
        string crossShare = $"(cross terms {prediction.CrossVariance / Math.Max(prediction.VarianceQ, 1e-300):P0} of the variance)";
        if (criterion.IsFloor)
        {
            // Drawn on the spot synthesis: W² = 1 − MTF, so the mean MTF is 1 − mean W², its σ is σ(W²),
            // and its skew the opposite of W²'s - all of the sampled lenses, the quadratic's being only a guide.
            var (mq, vq, tq) = prediction.SampledMoments;
            Console.WriteLine($"synthesis   nominal MTF {Level(criterion, Math.Sqrt(prediction.Q0))}   mean MTF {1.0 - mq:F5}   " +
                              $"σ(MTF) {Math.Sqrt(vq):F5}  skew {(vq > 0.0 ? -tq / Math.Pow(vq, 1.5) : 0.0):F2}   ({Prediction.Draws} lenses drawn on the linear spot)");
            Console.WriteLine($"            84.1 % above {Level(criterion, prediction.Quantile(0.841))}" +
                              $"   97.7 % above {Level(criterion, prediction.ProbableWorst)} ({prediction.Shape.ToString().ToLowerInvariant()})" +
                              (limitW is double l ? $"   yield at {limit:G4}: {prediction.Yield(l):P1}" : ""));
        }
        else
        {
            Console.WriteLine($"Koch        nominal {Math.Sqrt(prediction.Q0):F5}   √mean W² {prediction.RootMeanSquare:F5}   " +
                              $"σ(W²) {prediction.SigmaQ:E3}  skew {prediction.Skewness:F2}  {crossShare}");
            Console.WriteLine($"            mean+2σ {prediction.Level(2.0):F5} (normal)   84.1 % {prediction.Quantile(0.841):F5}   97.7 % {prediction.ProbableWorst:F5} ({prediction.Shape.ToString().ToLowerInvariant()})" +
                              (limitW is double l ? $"   yield at {l:G4}: {prediction.Yield(l):P1}" : ""));
        }
        for (int c = 0; c < also.Count; c++)
        {
            var ac = also[c].Criterion;
            Console.WriteLine(ac.IsFloor
                ? $"also        {ac.Name}: mean MTF {1.0 - alsoPredictions[c].SampledMoments.Mean:F5}   97.7 % above {Level(ac, alsoPredictions[c].ProbableWorst)}" +
                  $"   yield at {ac.Quoted(also[c].Limit):G4}: {alsoPredictions[c].Yield(also[c].Limit):P1}"
                : $"also        {ac.Name}: √mean W² {alsoPredictions[c].RootMeanSquare:F5}   97.7 % {alsoPredictions[c].ProbableWorst:F5}" +
                  $"   yield at {also[c].Limit:G4}: {alsoPredictions[c].Yield(also[c].Limit):P1}");
        }
        if (jointYield is double jy)
            Console.WriteLine($"            within every limit at once: {jy:P1} of lenses (each yield above is of one criterion alone)");
        if (travelPrediction != null)
            Console.WriteLine($"            {FocusLabel(compensator!)} moves {Math.Round(travelPrediction.Mean, 4) + 0.0:+0.0000;-0.0000} ± {travelPrediction.Sigma:F4} mm: " +
                              $"±{travelPrediction.Needed(0.9545):F4} mm for 95.4 %, ±{travelPrediction.Needed(0.9973):F4} mm for 99.7 %");
        if (compensator != null)
            foreach (var ((label, move), k) in TravelPrediction.OfAdjustments(model, statistics).Select((x, k) => (x, k)))
            {
                string u = compensator.Adjustments[k].Units;
                Console.WriteLine($"            {label} moves {Math.Round(move.Mean, 4) + 0.0:+0.0000;-0.0000} ± {move.Sigma:F4} {u}: " +
                                  $"±{move.Needed(0.9545):F4} {u} for 95.4 %, ±{move.Needed(0.9973):F4} {u} for 99.7 %");
            }

        ModelCheck? modelCheck = null;
        var alsoChecks = new List<ModelCheck>();
        if (check)
        {
            // The same lenses for every criterion, each traced once.
            var checks = ModelCheck.Run(lens, alsoModels.Prepend(model).ToList(), statistics, alsoCriteria.Prepend(criterion).ToList(), compensator);
            modelCheck = checks[0];
            alsoChecks = checks.Skip(1).ToList();
            for (int c = 0; c < checks.Count; c++)
            {
                var mcheck = checks[c];
                string which = also.Count == 0 ? "" : $" ({(c == 0 ? criterion.Name : also[c - 1].Criterion.Name)})";
                Console.WriteLine($"model check {mcheck.Lenses.Count} lenses traced against the {(mcheck.Quoted != null ? "spot synthesis" : "quadratic model")}{which}: within {mcheck.RmsError:P1} RMS, " +
                                  $"{mcheck.MaxError:P1} at worst" + (mcheck.RelativeToSpread ? " of the spread" : mcheck.Quoted != null ? " of the MTF's scatter" : "") +
                                  (mcheck.Untraceable > 0 ? $", {mcheck.Untraceable} untraceable" : ""));
                if (mcheck.Passes && mcheck.RmsError > 0.10)
                    Console.WriteLine("caution: the model is off by more than 10 % RMS; check the prediction with --montecarlo");
                if (!mcheck.Passes)
                    Console.WriteLine(mcheck.Quoted != null
                        ? $"WARNING: the spot is not linear in these tolerances (synthesis off by more than {mcheck.Limit:P0} of the scatter); " +
                          "trust the Monte Carlo (--montecarlo)"
                        : $"WARNING: the lens is not quadratic in these tolerances (model off by more than {mcheck.Limit:P0} RMS); " +
                          "trust the Monte Carlo (--montecarlo), not Koch");
            }
        }

        MonteCarloResult? mc = null;
        if (trials > 0)
        {
            mc = MonteCarlo.Run(lens, items, statistics, criterion, compensator, trials, seed, alsoCriteria);
            double seMean = Math.Sqrt(mc.VarianceQ / mc.Trials);
            double skew = mc.ThirdQ / Math.Pow(mc.VarianceQ, 1.5);
            Console.WriteLine(criterion.IsFloor
                ? $"Monte Carlo {trials} lenses, seed {seed}: mean MTF {1.0 - mc.MeanQ:F5} ± {seMean:F5} against the synthesis {1.0 - prediction.SampledMoments.Mean:F5}" +
                  $"   σ(MTF) {Math.Sqrt(mc.VarianceQ):F5}  skew {-skew:F2}"
                : $"Monte Carlo {trials} lenses, seed {seed}: √mean W² {Math.Sqrt(mc.MeanQ):F5}   σ(W²) {Math.Sqrt(mc.VarianceQ):E3}  skew {skew:F2}" +
                  $"   mean W² {mc.MeanQ:E4} ± {seMean:E1} against Koch {prediction.MeanQ:E4}");
            string above = criterion.IsFloor ? " above" : "";
            Console.WriteLine($"            median {Level(criterion, mc.Percentile(0.5))}   84.1 %{above} {Level(criterion, mc.Percentile(0.841))}" +
                              $"   97.7 %{above} {Level(criterion, mc.Percentile(Prediction.Yield97))}" +
                              (limitW is double l2 ? $"   yield at {limit:G4}: {mc.Yield(l2):P1}" : "") +
                              (mc.Untraceable > 0 ? $"   {mc.Untraceable} untraceable" : ""));
            for (int c = 0; c < also.Count; c++)
            {
                var r = new MonteCarloResult(mc.Also[c], Array.Empty<double>());
                var ac = also[c].Criterion;
                Console.WriteLine($"            {ac.Name}: median {Level(ac, r.Percentile(0.5))}   97.7 %{(ac.IsFloor ? " above" : "")} {Level(ac, r.Percentile(Prediction.Yield97))}" +
                                  $"   yield at {ac.Quoted(also[c].Limit):G4}: {r.Yield(also[c].Limit):P1}");
            }
            if (also.Count > 0 && limitW is double lm)
                Console.WriteLine($"            within every limit at once: {mc.JointYield(lm, alsoLimits):P1}");
            if (compensator != null)
            {
                bool limited = travel != null || compensator.Adjustments.Any(x => x.Travel != null);
                Console.WriteLine($"            {FocusLabel(compensator)} moved {mc.CompensatorRange.Min:+0.0000;-0.0000} to {mc.CompensatorRange.Max:+0.0000;-0.0000} mm" +
                                  (limited ? $"; {mc.CompensatorAtLimit} of {trials} ran out of travel" : ""));
                for (int k = 0; k < compensator.Adjustments.Count; k++)
                {
                    var (lo, hi) = mc.AdjustmentRange(k);
                    Console.WriteLine($"            {compensator.Adjustments[k].Label} moved {lo:+0.0000;-0.0000} to {hi:+0.0000;-0.0000} {compensator.Adjustments[k].Units}");
                }
            }
        }

        if (exportTrials != null) ExportTrials(exportTrials, lens, items, statistics, rms, compensator, seed, Math.Min(trials, 10));
        if (plots != null && mc != null)
            WritePlots(plots, Path.GetFileName(path), criterion, prediction, limitW, also, alsoPredictions, mc, trials, seed,
                       compensator, travelPrediction, compensator == null ? new List<(string, TravelPrediction)>() : TravelPrediction.OfAdjustments(model, statistics));

        if (report != null)
            File.WriteAllText(report, Report.ToJson(Report.Build(Path.GetFileName(path), lens, criterion.Name, compensator, statistics,
                                                                 items, prediction, travelPrediction, limitW, allocation, mc, seed, modelCheck, criterion) with
            {
                Units = criterion.Units,
                Reference = (criterion as DeviationCriterion)?.Reference,
                Also = also.Count == 0 ? null : also.Select((x, c) => new Report.Requirement(
                    x.Criterion.Name, x.Criterion.Units, x.Limit, (x.Criterion as DeviationCriterion)?.Reference,
                    Report.SpreadOf(alsoPredictions[c], x.Limit, x.Criterion),
                    mc == null ? null : new MonteCarloResult(mc.Also[c], Array.Empty<double>()).Yield(x.Limit))
                {
                    ModelCheck = alsoChecks.Count == 0 ? null : Report.CheckOf(alsoChecks[c]),
                }).ToList(),
                JointYield = jointYield,
                Joint = allocation?.Goal.Joint == true ? true : null,
                MarginalYield = allocation?.Goal.Joint == true ? allocation.MarginalYield : null,
                Adjustments = compensator is { Adjustments.Count: > 0 } ca
                    ? TravelPrediction.OfAdjustments(model, statistics).Select((x, k) => new Report.AdjustmentTravel(
                          x.Label, ca.Adjustments[k].Units, x.Travel.Mean, x.Travel.Sigma, x.Travel.Needed(0.9545), x.Travel.Needed(0.9973),
                          ca.Adjustments[k].Travel, mc?.AdjustmentRange(k).Min, mc?.AdjustmentRange(k).Max)).ToList()
                    : null,
                MonteCarloJointYield = mc != null && also.Count > 0 && limitW is double lr ? mc.JointYield(lr, alsoLimits) : null,
            }));
        if (writeTolerances != null)
            File.WriteAllText(writeTolerances, $"# {Path.GetFileName(path)}\n" + ToleranceSpec.FormatSet(items));
        Console.WriteLine($"{clock.Elapsed.TotalSeconds:F1} s");
        return 0;
    }

    /// <summary>
    /// The first Monte Carlo lenses as .zmx files, refocused, with the criterion of each field and
    /// wavelength as this module computes it - to open in OpticStudio and compare lens by lens.
    /// </summary>
    /// <summary>
    /// `--plots`: the Monte Carlo drawn - each criterion as a histogram and as a cumulative
    /// fraction against Koch's prediction, each compensator's moves against the move predicted -
    /// as SVG files and one HTML page that holds them all.
    /// </summary>
    private static void WritePlots(string directory, string lensName, ICriterion criterion, Prediction prediction, double? limitW,
                                   IReadOnlyList<CriterionLimit> also, IReadOnlyList<Prediction> alsoPredictions,
                                   MonteCarloResult mc, int trials, int seed, FocusCompensator? compensator,
                                   TravelPrediction? focusPrediction, IReadOnlyList<(string Label, TravelPrediction Travel)> adjustmentPredictions)
    {
        Directory.CreateDirectory(directory);
        var charts = new List<(string Heading, Tolerancing.Drawings.Chart Chart)>();
        string? noPng = null;
        // Each chart as SVG and, painted by SkiaSharp, as PNG; without its native library, SVG alone.
        void Save(string stem, string heading, Tolerancing.Drawings.Chart chart)
        {
            File.WriteAllText(Path.Combine(directory, stem + ".svg"), chart.ToSvg());
            if (noPng == null)
                try { ChartPng.Write(chart, Path.Combine(directory, stem + ".png")); }
                catch (Exception e) when (e is DllNotFoundException or TypeInitializationException or BadImageFormatException)
                {
                    noPng = e.GetBaseException().Message;
                }
            charts.Add((heading, chart));
        }
        string Slug(string text) => string.Concat(text.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-').Replace("--", "-");

        // A criterion as it is quoted - an MTF as an MTF - and Koch's fraction of lenses at or below each value.
        void Criterion(string stem, ICriterion c, Prediction p, IReadOnlyList<double> wValues, double? limit)
        {
            var values = wValues.Select(w => double.IsFinite(w) ? c.Quoted(w) : double.NaN).ToList();
            double Cdf(double q) => c.IsFloor ? 1.0 - p.Yield(c.FromQuoted(q)) : p.Yield(c.FromQuoted(q));
            var traced = new MonteCarloResult(wValues, Array.Empty<double>());
            string level = c.IsFloor ? "97.7 % of lenses above" : "97.7 % level";
            var markers = new List<Tolerancing.Drawings.ChartMarker>
            {
                new(c.Quoted(Math.Sqrt(Math.Max(0.0, p.Q0))), "nominal", "#2a9d8f"),
                new(c.Quoted(p.ProbableWorst), $"Koch {level}", "#c8102e"),
                new(c.Quoted(traced.Percentile(Prediction.Yield97)), $"traced {level}", "#1d4e89"),
            };
            if (limit is double l) markers.Add(new(c.Quoted(l), "limit", "#e9a400"));
            // The name carries its units - "RMS wavefront (waves, polychromatic)", "EFL change (mm)".
            string predicted = c.IsFloor ? "predicted (spot synthesis)" : "predicted (Koch)";
            Save($"{stem}", $"{c.Name}: the traced lenses",
                 Tolerancing.Drawings.Charts.Histogram($"{c.Name}, {trials} lenses", c.Name, values, Cdf, predicted, markers));
            Save($"{stem}-cumulative", $"{c.Name}: cumulative",
                 Tolerancing.Drawings.Charts.Cumulative($"{c.Name}, cumulative", c.Name, values, Cdf, predicted, markers,
                                                        c.IsFloor ? 1.0 - Prediction.Yield97 : Prediction.Yield97));
        }

        Criterion("criterion", criterion, prediction, mc.Criteria, limitW);
        for (int k = 0; k < also.Count; k++)
            Criterion($"also-{k + 1}-{Slug(also[k].Criterion.Name)}", also[k].Criterion, alsoPredictions[k], mc.Also[k], also[k].Limit);

        // Each compensator's moves, from where it sets the nominal lens, against a normal of the predicted mean and spread.
        void Move(string label, string units, IReadOnlyList<double> moves, TravelPrediction? predicted, double? travel)
        {
            var markers = new List<Tolerancing.Drawings.ChartMarker>();
            if (predicted != null)
            {
                double r = predicted.Needed(0.9545);
                markers.Add(new(-r, "", "#7b2cbf", $"predicted 95.4 % range ±{r:G4}"));
                markers.Add(new(r, "", "#7b2cbf", ""));
            }
            if (travel is double t)
            {
                markers.Add(new(-t, "", "#e9a400", $"travel ±{t:G4}"));
                markers.Add(new(t, "", "#e9a400", ""));
            }
            var svg = Tolerancing.Drawings.Charts.Histogram($"{label} moves, {trials} lenses", $"{label} ({units})", moves,
                                                           predicted == null ? null : predicted.Fraction, "predicted (normal, Koch's mean and σ)", markers);
            Save($"move-{Slug(label)}", $"{label}: its moves", svg);
        }
        if (compensator != null)
        {
            Move(FocusLabel(compensator), "mm", mc.Compensators, focusPrediction, compensator.Travel);
            for (int k = 0; k < compensator.Adjustments.Count; k++)
            {
                var a = compensator.Adjustments[k];
                Move(a.Label, a.Units, mc.Adjustments.Select(x => x[k]).ToList(),
                     k < adjustmentPredictions.Count ? adjustmentPredictions[k].Travel : null, a.Travel);
            }
        }

        var intro = new List<string>
        {
            $"{lensName}: {trials} lenses built at random to the tolerance set and traced in full (seed {seed}), against Koch's prediction.",
            $"{criterion.Name}: Koch's 97.7 % level {criterion.Quoted(prediction.ProbableWorst):G4}, the traced lenses' {criterion.Quoted(mc.Percentile(Prediction.Yield97)):G4}" +
            (limitW is double lw ? $"; yield at {criterion.Quoted(lw):G4}: Koch {prediction.Yield(lw):P1}, traced {mc.Yield(lw):P1}" : "") + ".",
        };
        if (mc.Untraceable > 0) intro.Add($"{mc.Untraceable} lenses could not be traced; they fail every limit and are left out of the histograms.");
        File.WriteAllText(Path.Combine(directory, "monte-carlo.html"),
                          Tolerancing.Drawings.Charts.Page($"Monte Carlo: {lensName}", intro, charts));
        Console.WriteLine($"plots       {charts.Count} charts as SVG{(noPng == null ? " and PNG" : "")}, and monte-carlo.html, in {directory}");
        if (noPng != null) Console.WriteLine($"note        no PNG copies: SkiaSharp could not load its native library ({noPng})");
    }

    private static void ExportTrials(string directory, NominalLens lens, IReadOnlyList<ToleranceItem> items, StatisticsModel statistics,
                                     RmsWavefrontCriterion criterion, FocusCompensator? compensator, int seed, int count)
    {
        Directory.CreateDirectory(directory);
        for (int k = 0; k < count; k++)
        {
            var x = MonteCarlo.Errors(items, statistics, seed, k);
            var perturbed = lens.Perturb();
            for (int i = 0; i < items.Count; i++) items[i].Tolerance.Apply(perturbed, x[i]);
            var c = compensator?.Solve(perturbed, criterion);
            var model = perturbed.Build();
            string file = Path.Combine(directory, $"trial_{k}.zmx");
            ZemaxExport.Write(perturbed, file);
            Console.WriteLine($"trial {k}: {file}   criterion {criterion.Evaluate(model):F6}" + (c is { } cc ? $"   back focus {cc.Change:+0.000000;-0.000000} mm" : ""));
            var system = model.System;
            var sites = new FieldSites(model);
            for (int f = 0; f < system.Fields.Count; f++)
            {
                var (on, at) = sites[f];
                Console.WriteLine($"   field {f}  " + string.Join("  ", Enumerable.Range(0, system.Wavelengths.Count).Select(w =>
                    WavefrontCalculator.Compute(on, at, w, ImagePlane.For(on, at, criterion.EffectiveOptions), criterion.Sampling).Statistics.RmsStandardDeviation.ToString("F6"))));
            }
        }
    }

    private static int DrawingsCommand(string path, string[] options)
    {
        string? tolerances = null, settingsFile = null, outDir = null;
        Tolerancing.Drawings.DrawingStandard? standard = null;
        var adjustSpecs = new List<string>();
        double testWavelength = PowerFringes.HeNe;
        for (int i = 0; i < options.Length; i++)
        {
            string Next() => i + 1 < options.Length ? options[++i] : throw new ArgumentException($"{options[i]} needs a value");
            switch (options[i])
            {
                case "--tolerances": tolerances = Next(); break;
                case "--settings": settingsFile = Next(); break;
                case "--out": outDir = Next(); break;
                case "--adjust": adjustSpecs.Add(Next()); break;
                case "--test-wavelength": testWavelength = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--standard":
                    standard = Next().ToLowerInvariant() switch
                    {
                        "iso" => Tolerancing.Drawings.DrawingStandard.Iso10110,
                        "asme" or "mil" or "ansi" => Tolerancing.Drawings.DrawingStandard.Asme,
                        var s => throw new ArgumentException($"no standard {s}: iso or asme"),
                    };
                    break;
                default: throw new ArgumentException($"unknown option {options[i]}");
            }
        }
        if (tolerances == null || outDir == null) throw new ArgumentException("drawings needs --tolerances and --out");

        var lens = NominalLens.Read(path);
        var items = ToleranceSpec.ParseSet(File.ReadAllText(tolerances), testWavelength);
        var settings = settingsFile == null
            ? new Tolerancing.Drawings.DrawingSettings()
            : Tolerancing.Drawings.DrawingSettings.FromJson(File.ReadAllText(settingsFile));
        if (standard is { } st) settings = settings with { Standard = st };
        if (settings.Project.Length == 0) settings = settings with { Project = Path.GetFileNameWithoutExtension(path) };

        Directory.CreateDirectory(outDir);
        var drawings = Tolerancing.Drawings.ElementDrawings.Build(lens, items, settings, testWavelength,
                                                                adjustSpecs.SelectMany(ParseAdjust).ToList());
        var sheets = new List<Tolerancing.Drawings.Sheet>();
        foreach (var d in drawings)
        {
            var sheet = Tolerancing.Drawings.Layout.Draw(d, settings);
            sheets.Add(sheet);
            string stem = Path.Combine(outDir, string.Concat(d.Number.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' ? '_' : c)));
            Tolerancing.Drawings.PdfWriter.Write(stem + ".pdf", new[] { sheet });
            Tolerancing.Drawings.DxfWriter.Write(stem + ".dxf", sheet);
            Tolerancing.Drawings.SvgWriter.Write(stem + ".svg", sheet);
            Console.WriteLine($"{d.Number}  {d.Title,-22} {d.Material.Name,-10} CT {d.Thickness:F3}  Ø{d.Diameter:F2}  edge {d.EdgeThickness:F2}");
            foreach (var w in d.Warnings.Concat(sheet.Warnings)) Console.WriteLine($"      warning: {w}");
        }
        var mirrors = Enumerable.Range(1, lens.BackFocusSurface).Where(s => lens.System.Surfaces[s].IsMirror).ToList();
        if (mirrors.Count > 0)
            Console.WriteLine($"mirror{(mirrors.Count > 1 ? "s" : "")} at surface{(mirrors.Count > 1 ? "s" : "")} {string.Join(", ", mirrors)} not drawn: a lens file gives no substrate");
        if (sheets.Count > 0) Tolerancing.Drawings.PdfWriter.Write(Path.Combine(outDir, "drawings.pdf"), sheets);
        Console.WriteLine($"{drawings.Count} element drawings ({settings.Standard}) in {outDir}: one .pdf, .dxf and .svg each, and drawings.pdf with all");
        return 0;
    }

    /// <summary>A further criterion and its limit, written criterion:limit.</summary>
    private static (string Name, double Limit) ParseAlso(string text)
    {
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || !double.TryParse(text[(colon + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double limit) || !(limit > 0.0))
            throw new ArgumentException($"--also {text}: write it criterion:limit, as efl:0.25");
        return (text[..colon], limit);
    }

    /// <summary>For a departure criterion, the nominal value it is measured from.</summary>
    private static string ReferenceText(ICriterion criterion) => criterion is DeviationCriterion d
        ? "; nominal " + string.Join(", ", d.Reference.Select(v => v.ToString("F6", CultureInfo.InvariantCulture))) + " " + d.Units
        : "";

    /// <summary>The back focus is the change the back-focus compensator makes: say so.</summary>
    /// <summary>
    /// Every compensator on one line, the focus first: "compensators focus (surface 8, back focus);
    /// decenter x 5-6; image tilt x within ±0.5 deg", or "compensators none".
    /// </summary>
    private static string CompensatorsLine(FocusCompensator? compensator, NominalLens lens, ICriterion criterion, string focusNote = "")
    {
        if (compensator == null) return "compensators none";
        string focus = $"focus (surface {compensator.Surface}, {(compensator.Surface == lens.BackFocusSurface ? "back focus" : "refocusing")})" +
                       (criterion is IFocusedCriterion ? " for the sharpest image" : "") +
                       (compensator.Travel is double t ? $" within ±{t:G4} mm" : "") + focusNote;
        return "compensators " + string.Join("; ", compensator.Adjustments
            .Select(a => a.Label + (a.Travel is double at ? $" within ±{at:G4} {a.Units}" : "")).Prepend(focus));
    }

    /// <summary>The focus compensator as the reports name it: "focus (surface 8)".</summary>
    private static string FocusLabel(FocusCompensator compensator) => $"focus (surface {compensator.Surface})";

    /// <summary>
    /// Whether an adjustment takes the error out: an element's decenter or tilt that the element is
    /// aligned by, or the thickness of a space that is set at assembly.
    /// </summary>
    private static bool Adjusted(Tolerance t, FocusCompensator? compensator) =>
        compensator != null && (t is ThicknessChange th && th.Surface == compensator.Surface ||
            compensator.Adjustments.Any(a => (a, t) switch
            {
                (ElementDecenterAdjustment d, ElementDecentre e) => d.First == e.Surface && d.Last == e.Last && d.Axis == e.Axis,
                (ElementTiltAdjustment d, ElementTilt e) => d.First == e.Surface && d.Last == e.Last && d.Axis == e.Axis,
                (SpaceAdjustment s, ThicknessChange th2) => s.Surface == th2.Surface,
                _ => false,
            }));

    private static void BflNote(ICriterion criterion, FocusCompensator? compensator, NominalLens lens)
    {
        if (criterion is BflCriterion && compensator?.Surface == lens.BackFocusSurface)
            Console.WriteLine("note        the back focus is what the back-focus compensator refocuses; its change is the compensator's move." +
                              " For a fixed-focus lens use --no-compensator");
    }

    /// <summary>The criterion without the fields the nominal lens cannot be traced at, saying which.</summary>
    private static RmsWavefrontCriterion Traceable(RmsWavefrontCriterion criterion, NominalLens lens)
    {
        var result = criterion.WithoutUntraceableFields(lens.Model, out var excluded);
        if (excluded.Count > 0)
            Console.WriteLine($"warning: no ray of field{(excluded.Count > 1 ? "s" : "")} " +
                              string.Join(", ", excluded.Select(f => lens.System.Fields[f].Y.ToString("G6", CultureInfo.InvariantCulture))) +
                              " passes the nominal lens; left out of the criterion");
        return result;
    }

    /// <summary>One node of a budget tree and those under it; for an MTF, each node alone as the drop mean + 2σ, the square of its W.</summary>
    private static void PrintNode(NodeResult node, int depth, bool drop = false)
    {
        string name = new string(' ', 2 * depth) + node.Name;
        string allowance = node.Allowance is double a ? $"   allowed {a:F5}{(node.WithinAllowance ? "" : "  OVER")}" : "";
        double alone = drop ? node.Equivalent * node.Equivalent : node.Equivalent;
        Console.WriteLine($"{name,-34} {node.Mean,11:E3} {node.Sigma,11:E3} {alone,9:F5}{allowance}");
        foreach (var c in node.Children) PrintNode(c, depth + 1, drop);
    }

    private static RadialDistribution ParseRadial(string text)
    {
        var parts = text.ToLowerInvariant().Split(':');
        return parts[0] switch
        {
            "disc" => RadialDistribution.Disc,
            "ring" => RadialDistribution.Ring,
            "rayleigh" => RadialDistribution.Rayleigh(parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 2.0),
            _ => throw new ArgumentException($"no radial distribution {text}: disc, ring or rayleigh[:k]"),
        };
    }
}
