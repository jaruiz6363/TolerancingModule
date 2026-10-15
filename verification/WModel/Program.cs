using Tolerancing.Core;

// Verification section 3.3: Koch 1978 fitted his quadratic to the performance measure itself, W;
// the module fits it to W² (theory, section 3.1). Both models are built here from the same traced
// data - the sensitivity run and every pair at both maxima - so they differ only in what the
// parabolas are fitted to, and both are set against lenses traced in full.
//
//   dotnet run -c Release -- <lens> [tolerance file] [traced lenses (4000)] [seed (3)]

// W² and σ in UTF-8 whatever the console's code page, which is put back on exit.
var consoleEncoding = Console.OutputEncoding;
Console.OutputEncoding = new System.Text.UTF8Encoding(false);
AppDomain.CurrentDomain.ProcessExit += (_, _) => Console.OutputEncoding = consoleEncoding;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: WModel <lens> [tolerance file] [traced lenses] [seed]");
    return 2;
}
var lens = NominalLens.Read(args[0]);
var items = args.Length > 1 && args[1] != "-" ? ToleranceSpec.ParseSet(File.ReadAllText(args[1])) : DefaultTolerances.Create(lens);
int traced = args.Length > 2 ? int.Parse(args[2]) : 4000;
int seed = args.Length > 3 ? int.Parse(args[3]) : 3;
const int draws = 200_000, drawSeed = 101;
var statistics = StatisticsModel.Default;
var criterion = new RmsWavefrontCriterion();
var compensator = FocusCompensator.BackFocus(lens);

// The module's model with every pair traced, and with its default cross terms from the slopes.
var squared = QuadraticModel.Build(lens, items, criterion, compensator, CrossTerms.All);
var slopes = QuadraticModel.Build(lens, items, criterion, compensator, CrossTerms.Wavefront);
if (squared.PartlyTraced.Count + squared.Untraced.Count > 0)
    Console.WriteLine($"note: {squared.PartlyTraced.Count + squared.Untraced.Count} pair(s) not traced at both maxima; their W is not exact here");

// The traced values, recovered exactly: each parabola in W² passes through its ends, and each cross
// term is the pair's W² less Q0 and the two ends' changes.
int n = items.Count;
double q0 = squared.Q0, w0 = Math.Sqrt(q0);
var t = squared.Terms.Select(term => term.Item.Max).ToArray();
double WAt(params (int I, double X)[] errors) =>
    Math.Sqrt(q0 + errors.Sum(e => squared.Terms[e.I].Change(e.X))
                 + (errors.Length == 2 && squared.Cross.TryGetValue((errors[0].I, errors[1].I), out var c) ? c * errors[0].X * errors[1].X : 0.0));

// Koch's model in W: the same parabola through each tolerance's ends and nominal, in W; each cross
// term the pair's W less W0 and the two ends' changes in W.
var terms = squared.Terms.Select((term, i) => QuadraticModel.FitParabola(term.Item, WAt((i, term.Item.Min)), w0, WAt((i, t[i])))).ToList();
var cross = new Dictionary<(int, int), double>();
for (int i = 0; i < n; i++)
    for (int j = i + 1; j < n; j++)
    {
        double c = (WAt((i, t[i]), (j, t[j])) - WAt((i, t[i])) - WAt((j, t[j])) + w0) / (t[i] * t[j]);
        if (c != 0.0) cross[(i, j)] = c;
    }
var linear = new QuadraticModel("W", w0, terms, cross);

// Each model drawn on the module's own error draws; W² read back as W, W used as it is.
double[] Sample(Func<double[], double> w, int count, int s)
{
    var v = new double[count];
    Parallel.For(0, count, k => v[k] = w(MonteCarlo.Errors(items, statistics, s, k)));
    return v;
}
double FromSquare(QuadraticModel m, double[] x) => Math.Sqrt(Math.Max(0.0, m.Evaluate(x)));
var inW = Sample(linear.Evaluate, draws, drawSeed);
var inW2 = Sample(x => FromSquare(squared, x), draws, drawSeed);
var inW2Slopes = Sample(x => FromSquare(slopes, x), draws, drawSeed);

// Lenses traced in full, and each model at the same errors, lens by lens.
var mc = MonteCarlo.Run(lens, items, statistics, criterion, compensator, traced, seed);
var real = mc.Criteria.Where(double.IsFinite).ToArray();
double Rms(Func<double[], double> w) =>
    Math.Sqrt(Enumerable.Range(0, traced).Where(k => double.IsFinite(mc.Criteria[k]))
                        .Average(k => Math.Pow(w(MonteCarlo.Errors(items, statistics, seed, k)) - mc.Criteria[k], 2)));

static double Level(double[] w, double p)
{
    var s = w.OrderBy(v => v).ToArray();
    return s[Math.Clamp((int)Math.Ceiling(p * s.Length) - 1, 0, s.Length - 1)];
}
double meanSq = real.Average(v => v * v);
double se = Math.Sqrt(real.Select(v => v * v).Average(v => (v - meanSq) * (v - meanSq)) / real.Length);

Console.WriteLine($"{Path.GetFileName(args[0])}: {n} tolerances{(args.Length > 1 && args[1] != "-" ? $" from {Path.GetFileName(args[1])}" : ", commercial grade")}; nominal W {w0:F4} waves");
Console.WriteLine($"traced: {real.Length} lenses (seed {seed}){(real.Length < traced ? $", {traced - real.Length} untraceable" : "")}; models: {draws} draws each");
Console.WriteLine();
Console.WriteLine($"{"",-31}{"mean W²",12}{"mean W",10}{"84.1 %",10}{"97.7 %",10}{"W < 0",8}{"lens by lens",14}");
Console.WriteLine($"{"Traced",-31}{meanSq,12:F4}{real.Average(),10:F4}{Level(real, 0.841),10:F4}{Level(real, 0.977),10:F4}{"",8}{"",14}");
Console.WriteLine($"{"",-31}{"± " + se.ToString("F4"),12}");
void Row(string name, double[] w, Func<double[], double> model) =>
    Console.WriteLine($"{name,-31}{w.Average(v => v * v),12:F4}{w.Average(),10:F4}{Level(w, 0.841),10:F4}{Level(w, 0.977),10:F4}" +
                      $"{100.0 * w.Count(v => v < 0) / w.Length,7:F1}%{Rms(model),14:F4}");
Row("Model in W (Koch)", inW, linear.Evaluate);
Row("Model in W², every pair", inW2, x => FromSquare(squared, x));
Row("Model in W², slopes (default)", inW2Slopes, x => FromSquare(slopes, x));
Console.WriteLine();
Console.WriteLine("lens by lens: the RMS, over the traced lenses, of each model's W less the traced W, in waves");
return 0;
