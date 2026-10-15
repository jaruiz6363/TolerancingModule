using System.Text.Json;
using Tolerancing.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>The console holds one writer at a time, so the tests that capture it run one by one.</summary>
[CollectionDefinition("Console", DisableParallelization = true)]
public class ConsoleCollection { }

/// <summary>
/// The command-line tool end to end, as a user chains it: statistics with a report and the set
/// written out, allocation, drawings from the allocated set in both standards.
/// </summary>
[Collection("Console")]
public class CliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tol-cli-{Guid.NewGuid():N}");

    public CliTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    internal static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (o, e) = (Console.Out, Console.Error);
        var (so, se) = (new StringWriter(), new StringWriter());
        Console.SetOut(so);
        Console.SetError(se);
        try { return (Cli.Program.Main(args), so.ToString(), se.ToString()); }
        finally { Console.SetOut(o); Console.SetError(e); }
    }

    private string P(string name) => Path.Combine(_dir, name);

    [Fact]
    public void NominalPrintsFirstOrderDataAndEachField()
    {
        var (code, output, _) = Run("nominal", ToleranceTests.Triplet);
        Assert.Equal(0, code);
        Assert.Contains("EFL 50.", output);
        Assert.Equal(3, output.Split('\n').Count(l => l.TrimStart().StartsWith("0 ") || l.TrimStart().StartsWith("10 ") || l.TrimStart().StartsWith("14 ")));
    }

    [Fact]
    public void AllocationCanHoldTheFocalLengthInMillimetres()
    {
        var (code, output, err) = Run("allocate", ToleranceTests.Triplet, "--criterion", "efl", "--no-compensator", "--limit", "0.25",
                                      "--tree", "none", "--no-check", "--report", P("efl.json"));
        Assert.True(code == 0, err);
        Assert.Contains("criterion   EFL change (mm); nominal 50.", output);
        Assert.Contains("within 0.25 mm", output);
        var report = Report.FromJson(File.ReadAllText(P("efl.json")));
        Assert.Equal("mm", report.Units);
        Assert.Single(report.Reference!);
        Assert.True(report.Feasible);
        Assert.True(report.Prediction.Level977 <= 0.25 + 1e-9);

        var bfl = Run("sensitivity", ToleranceTests.Triplet, "--criterion", "bfl", "--top", "3");
        Assert.True(bfl.Code == 0, bfl.Err);
        Assert.Contains("the back focus is what the back-focus compensator refocuses", bfl.Out);
        Assert.NotEqual(0, Run("sensitivity", ToleranceTests.Triplet, "--criterion", "mtf").Code);
    }

    [Fact]
    public void AllocationCanMeetSeveralCriteriaAtOnce()
    {
        File.WriteAllText(P("set.tol"), "TTHI 2 0.1\nTTHI 4 0.1\nTIND 3 0.001\nTEDX 3-4 0.05\nTEDY 3-4 0.05\nTETX 6-7 0.1\nTETY 6-7 0.1\n");
        var (code, output, err) = Run("allocate", ToleranceTests.Triplet, "--tolerances", P("set.tol"), "--criterion", "efl", "--no-compensator",
                                      "--limit", "0.3", "--also", "boresight-mm:0.1", "--tree", "none", "--no-check",
                                      "--montecarlo", "60", "--report", P("both.json"));
        Assert.True(code == 0, err);
        Assert.Contains("also        boresight (mm at the image) within 0.1 mm", output);
        Assert.Contains("and within 0.1 mm boresight", output);
        Assert.Contains("within every limit at once", output);
        var report = Report.FromJson(File.ReadAllText(P("both.json")));
        Assert.True(report.Feasible);
        var boresight = Assert.Single(report.Also!);
        Assert.Equal("mm", boresight.Units);
        Assert.Equal(0.1, boresight.Limit);
        Assert.True(boresight.Prediction.Level977 <= 0.1 + 1e-9);
        Assert.True(report.Prediction.Level977 <= 0.3 + 1e-9);
        Assert.NotNull(boresight.MonteCarloYield);
        Assert.InRange(report.JointYield!.Value, 0.0, Math.Min(report.Prediction.Yield!.Value, boresight.Prediction.Yield!.Value) + 1e-9);
        Assert.NotNull(report.MonteCarloJointYield);

        var checkedRun = Run("statistics", ToleranceTests.Triplet, "--tolerances", P("set.tol"), "--criterion", "efl", "--no-compensator",
                             "--limit", "0.3", "--also", "boresight-mm:0.1", "--tree", "none", "--report", P("checked.json"));
        Assert.True(checkedRun.Code == 0, checkedRun.Err);
        Assert.Contains("traced against the quadratic model (EFL change (mm))", checkedRun.Out);
        Assert.Contains("traced against the quadratic model (boresight (mm at the image))", checkedRun.Out);
        var checkedReport = Report.FromJson(File.ReadAllText(P("checked.json")));
        Assert.Equal(12, checkedReport.ModelCheck!.Lenses);
        Assert.Equal(12, checkedReport.Also![0].ModelCheck!.Lenses);

        Assert.Equal(1, Run("statistics", ToleranceTests.Triplet, "--also", "efl").Code);                     // no limit
        Assert.Equal(1, Run("statistics", ToleranceTests.Triplet, "--criterion", "efl", "--also", "efl:0.1").Code);   // twice
        var infeasible = Run("allocate", ToleranceTests.Triplet, "--tolerances", P("set.tol"), "--criterion", "efl", "--no-compensator",
                             "--limit", "0.3", "--also", "boresight-mm:1e-6", "--tree", "none", "--no-check");
        Assert.True(infeasible.Code == 0, infeasible.Err);
        Assert.Contains("NOT FEASIBLE: every tolerance at its tightest still misses the goal: boresight", infeasible.Out);
    }

    [Fact]
    public void AnMtfLimitIsAFloorOnTheMtfItself()
    {
        var (code, output, err) = Run("statistics", ToleranceTests.Triplet, "--criterion", "mtf:0:20:t", "--limit", "0.3",
                                      "--tree", "none", "--no-check", "--montecarlo", "40", "--report", P("mtf.json"));
        Assert.True(code == 0, err);
        Assert.Contains("criterion   MTF T at 20 cy/mm, field 0", output);
        Assert.Contains("W² = 1 − MTF", output);
        Assert.Contains("97.7 % above", output);
        Assert.Contains("yield at 0.3:", output);
        var report = Report.FromJson(File.ReadAllText(P("mtf.json")));
        var quoted = report.Prediction.Quoted!;
        Assert.True(quoted.Floor);
        Assert.Equal(0.3, quoted.Limit!.Value, 12);
        Assert.InRange(quoted.Level977, 0.15, quoted.Median);          // a floor: the 97.7 % lens is the worse one
        Assert.True(report.MonteCarlo!.Quoted!.Floor);

        Assert.NotEqual(0, Run("statistics", ToleranceTests.Triplet, "--criterion", "mtf:5:20:t").Code);    // no such field
    }

    [Fact]
    public void AJointAllocationHasItsYieldWithinEveryLimit()
    {
        File.WriteAllText(P("set.tol"), "TTHI 2 0.1\nTTHI 4 0.1\nTIND 3 0.001\nTEDX 3-4 0.05\nTEDY 3-4 0.05\nTETX 6-7 0.1\nTETY 6-7 0.1\n");
        var (code, output, err) = Run("allocate", ToleranceTests.Triplet, "--tolerances", P("set.tol"), "--criterion", "efl", "--no-compensator",
                                      "--limit", "0.2", "--also", "boresight-mm:0.1", "--joint", "--method", "cost", "--tree", "none", "--no-check",
                                      "--report", P("joint.json"));
        Assert.True(code == 0, err);
        Assert.Contains("97.7 % within every limit at once: each criterion held to", output);
        var report = Report.FromJson(File.ReadAllText(P("joint.json")));
        Assert.True(report.Joint);
        Assert.True(report.Feasible);
        Assert.True(report.JointYield >= Prediction.Yield97, $"joint yield {report.JointYield}");
        Assert.InRange(report.MarginalYield!.Value, Prediction.Yield97, 1.0 - (1.0 - Prediction.Yield97) / 2.0 + 1e-12);

        Assert.Equal(1, Run("allocate", ToleranceTests.Triplet, "--limit", "1", "--joint").Code);          // nothing to be joint with
        Assert.Equal(1, Run("statistics", ToleranceTests.Triplet, "--also", "efl:0.1", "--joint").Code);   // allocate only
    }

    [Fact]
    public void StatisticsAllocationAndDrawingsChainThroughTheirFiles()
    {
        // A small set, so the chain is quick: the middle element and one air space.
        File.WriteAllText(P("set.tol"), "TFRN 3 5\nTFRN 4 5\nTTHI 4 -0.02 0.1 normal:3\nTEDX 3-4 0.02\nTEDY 3-4 0.02\nTSTX 3 0.05\nTSTY 3 0.05\nTIND 3 0.001\n");

        var stats = Run("statistics", ToleranceTests.Triplet, "--tolerances", P("set.tol"), "--montecarlo", "20",
                        "--tree", "element", "--report", P("stats.json"), "--travel", "1.0");
        Assert.True(stats.Code == 0, stats.Err);
        Assert.Contains("error budget by element", stats.Out);
        Assert.Contains("Monte Carlo 20 lenses", stats.Out);
        var report = Report.FromJson(File.ReadAllText(P("stats.json")));
        Assert.Equal(8, report.Tolerances.Count);
        Assert.Equal(20, report.MonteCarlo!.Trials);
        Assert.Equal(1.0, report.Compensator!.Allowed);

        var alloc = Run("allocate", ToleranceTests.Triplet, "--tolerances", P("set.tol"), "--limit", "1.5", "--method", "cost",
                        "--tree", "none", "--write-tolerances", P("allocated.tol"), "--report", P("alloc.json"));
        Assert.True(alloc.Code == 0, alloc.Err);
        var allocated = ToleranceSpec.ParseSet(File.ReadAllText(P("allocated.tol")));
        Assert.Equal(8, allocated.Count);
        Assert.Equal(0.04 / 0.06, allocated[2].Center / allocated[2].HalfWidth, 5);     // the bias kept in proportion, to the file's six figures
        Assert.Equal(Distribution.Normal(3.0), allocated[2].Distribution);
        Assert.NotNull(Report.FromJson(File.ReadAllText(P("alloc.json"))).Feasible);

        foreach (var standard in new[] { "iso", "asme" })
        {
            var dir = P($"dwg-{standard}");
            var dwg = Run("drawings", ToleranceTests.Triplet, "--tolerances", P("allocated.tol"), "--out", dir, "--standard", standard);
            Assert.True(dwg.Code == 0, dwg.Err);
            foreach (var n in new[] { "OPT-01", "OPT-02", "OPT-03" })
                foreach (var ext in new[] { ".pdf", ".dxf", ".svg" })
                    Assert.True(new FileInfo(Path.Combine(dir, n + ext)).Length > 1000, $"{n}{ext}");
            Assert.True(File.Exists(Path.Combine(dir, "drawings.pdf")));
        }
    }

    [Fact]
    public void SettingsAreWrittenWithEveryDefaultAndReadBack()
    {
        Assert.Equal(0, Run("settings", P("s.json")).Code);
        var json = File.ReadAllText(P("s.json"));
        Assert.Contains("\"scratchDig\": \"60-40\"", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Iso10110", doc.RootElement.GetProperty("standard").GetString());
    }

    [Fact]
    public void AMirrorSystemSaysItsMirrorsAreNotDrawn()
    {
        var lens = P("mirror.zmx");
        File.WriteAllText(lens, "VERS 221026 6 20120530 20120530\nMODE SEQ\nUNIT MM X W X CM MR CPMM\nENPD 20\nFTYP 0 0 1 1 0 0 0 1\n" +
                                "XFLN 0\nYFLN 0\nFWGN 1\nWAVM 1 0.55 1\nPWAV 1\nSURF 0\n  CURV 0.0 0 0 0 0 \"\"\n  DISZ INFINITY\n" +
                                "SURF 1\n  STOP\n  CURV -0.005 0 0 0 0 \"\"\n  CONI -1\n  DISZ -100\n  GLAS MIRROR 0 0 1.5 40 0 0 0 0 0 0\n" +
                                "SURF 2\n  CURV 0.0 0 0 0 0 \"\"\n  DISZ 0\n");
        File.WriteAllText(P("m.tol"), "TFRN 1 2\nTSTX 1 0.01\n");
        var (code, output, err) = Run("drawings", lens, "--tolerances", P("m.tol"), "--out", P("m"));
        Assert.True(code == 0, err);
        Assert.Contains("mirror at surface 1 not drawn", output);
        Assert.Contains("0 element drawings", output);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("allocate")]
    public void UnknownCommandsPrintTheUsage(string command)
    {
        var (code, _, err) = Run(command);
        Assert.Equal(2, code);
        Assert.Contains("usage:", err);
    }

    [Fact]
    public void BadInputIsAnErrorMessageNotACrash()
    {
        Assert.Equal(1, Run("allocate", ToleranceTests.Triplet).Code);                               // no --limit
        Assert.Equal(1, Run("statistics", ToleranceTests.Triplet, "--cross", "sideways").Code);
        Assert.Equal(1, Run("nominal", P("missing.zmx")).Code);
        File.WriteAllText(P("bad.tol"), "TFRN 3 5\nTTHI 4 0.2 0.1\n");
        var (code, _, err) = Run("statistics", ToleranceTests.Triplet, "--tolerances", P("bad.tol"));
        Assert.Equal(1, code);
        Assert.Contains("line 2", err);
    }
}

[Collection("Console")]
public class CliErrorTests
{
    [Fact]
    public void AnErrorRaisedInsideAParallelEvaluationIsAMessageNotACrash()
    {
        // A radius change on the plane stop surface: refused inside the sensitivity run's Parallel.For.
        string set = Path.Combine(Path.GetTempPath(), $"tol-plane-{Guid.NewGuid():N}.tol");
        File.WriteAllText(set, "TRAD 5 1\n");
        try
        {
            var (o, e) = (Console.Out, Console.Error);
            var err = new StringWriter();
            Console.SetOut(new StringWriter());
            Console.SetError(err);
            int code;
            try { code = Cli.Program.Main(new[] { "statistics", ToleranceTests.Triplet, "--tolerances", set, "--no-check" }); }
            finally { Console.SetOut(o); Console.SetError(e); }
            Assert.Equal(1, code);
            Assert.Contains("plane", err.ToString());
        }
        finally { File.Delete(set); }
    }

    /// <summary>Every compensator on one line, the focus by name in the travels, and the allocation table's marks explained.</summary>
    [Fact]
    public void AReportNamesEveryCompensatorAndExplainsItsMarks()
    {
        var (code, output, _) = CliTests.Run("allocate", ToleranceTests.Triplet, "--limit", "2.5", "--method", "cost",
                                    "--adjust", "decenter:6-7:0.3", "--adjust", "image-tilt", "--no-check");
        Assert.Equal(0, code);
        Assert.Contains("compensators focus (surface 7, back focus); decenter x 6-7 within ±0.3 mm; decenter y 6-7 within ±0.3 mm; image tilt x; image tilt y", output);
        Assert.Contains("focus (surface 7) moves", output);
        Assert.DoesNotContain("compensator moves", output);
        var lines = output.Split('\n').Select(l => l.TrimEnd()).ToList();
        Assert.Contains(lines, l => l.StartsWith("TED*   6-7") && l.EndsWith(" adjusted"));
        Assert.Contains(lines, l => l.StartsWith("TED*   3-4") && !l.EndsWith(" adjusted"));
        Assert.Contains("* an x-y pair, such as TST* for TSTX and TSTY", output);
        Assert.Contains("adjusted: an error a compensator takes out", output);

        var (_, plain, _) = CliTests.Run("sensitivity", ToleranceTests.Triplet, "--top", "2");
        Assert.Contains("compensators focus (surface 7, back focus), nominal moved", plain);
        var (_, none, _) = CliTests.Run("sensitivity", ToleranceTests.Triplet, "--top", "2", "--no-compensator");
        Assert.Contains("compensators none", none);
    }
}

[Collection("Console")]
public class CliPlotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tol-plots-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void PlotsDrawEachCriterionAndCompensatorAndNeedAMonteCarlo()
    {
        var (code, output, _) = CliTests.Run("statistics", ToleranceTests.Triplet, "--limit", "2.5", "--also", "efl:0.5",
                                             "--adjust", "tilt:6-7", "--montecarlo", "20", "--seed", "3", "--no-check", "--plots", _dir);
        Assert.Equal(0, code);
        Assert.Contains($"plots       7 charts as SVG and PNG, and monte-carlo.html, in {_dir}", output);
        foreach (var file in new[] { "criterion.svg", "criterion-cumulative.svg", "also-1-efl-change-mm.svg", "also-1-efl-change-mm-cumulative.svg",
                                     "move-focus-surface-7.svg", "move-tilt-x-6-7.svg", "move-tilt-y-6-7.svg", "monte-carlo.html" })
            Assert.True(File.Exists(Path.Combine(_dir, file)), file);
        string page = File.ReadAllText(Path.Combine(_dir, "monte-carlo.html"));
        Assert.Contains("20 lenses built at random", page);

        // Each chart as a PNG too, at twice its 760 by 440: the signature, then IHDR's width and height.
        var pngs = Directory.GetFiles(_dir, "*.png");
        Assert.Equal(7, pngs.Length);
        foreach (var png in pngs)
        {
            var bytes = File.ReadAllBytes(png);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);
            int Int(int at) => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
            Assert.Equal((1520, 880), (Int(16), Int(20)));
        }

        var (failed, _, error) = CliTests.Run("statistics", ToleranceTests.Triplet, "--plots", _dir, "--no-check");
        Assert.NotEqual(0, failed);
        Assert.Contains("--plots needs --montecarlo", error);
    }
}
