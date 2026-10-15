using System.Text;
using System.Xml.Linq;
using Tolerancing.Core;
using Tolerancing.Drawings;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>Each value on an element drawing is the one the lens and its tolerance set give.</summary>
public class ElementDrawingTests
{
    internal static readonly string Doublet = Path.Combine(AppContext.BaseDirectory, "TestData", "cemented_doublet.zmx");

    private static readonly DrawingSettings Settings = new() { Date = "2026-10-12" };

    [Fact]
    public void EveryPieceOfGlassGetsADrawingAndItsValuesComeFromTheBudget()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = DefaultTolerances.Create(lens, ShopGrade.Precision);
        var drawings = ElementDrawings.Build(lens, items, Settings);

        Assert.Equal(new[] { 1, 3, 6 }, drawings.Select(d => d.First));
        var middle = drawings[1];
        var g = DefaultTolerances.For(ShopGrade.Precision);
        Assert.Equal("F2", middle.Material.Name);
        Assert.Equal("SCHOTT", middle.Material.Catalog);
        Assert.Equal(1.0, middle.Thickness);
        Assert.Equal((-g.ThicknessMm, g.ThicknessMm), (middle.ThicknessMinus, middle.ThicknessPlus));
        Assert.Equal(g.Fringes, middle.Front.PowerFringes, 12);
        Assert.Equal(g.SurfaceTiltDeg * 60.0, middle.Back.TiltArcmin, 12);
        Assert.Equal(g.Index, middle.Material.NdTolerance, 12);
        Assert.Equal(g.AbbePercent, middle.Material.VdTolerancePercent, 9);
        Assert.Equal(632.8, middle.TestWavelengthNm, 9);
        Assert.Empty(middle.Warnings);

        // A biconcave element: both surfaces concave on the part.
        Assert.Equal("CC", ElementDrawing.Shape(middle.Front, front: true));
        Assert.Equal("CC", ElementDrawing.Shape(middle.Back, front: false));
        Assert.Equal("CX", ElementDrawing.Shape(drawings[0].Front, front: true));
    }

    [Fact]
    public void TheEdgeIsTheClearApertureAndAMarginRoundedUpAndItsThicknessIsTheSagsDifference()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var d = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), Settings)[0];
        Assert.Equal(14.0, d.Front.ClearAperture, 12);                  // DIAM 7
        Assert.Equal(15.0, d.Diameter, 12);                             // 14 + 1, to the half millimetre
        var s = lens.System.Surfaces;
        Assert.Equal(2.5 - s[1].Sag(7.5) + s[2].Sag(7.5), d.EdgeThickness, 12);
        Assert.Contains(d.Warnings, w => w.Contains("edge thickness"));
    }

    [Fact]
    public void FringesCountedAtAnotherWavelengthAreRestatedAtTheDrawingsOne()
    {
        // The first power tolerance sets the drawing's wavelength. Another counted at 546.1 nm is a sag
        // error of 4 × 0.5461/2 µm, which is 4 × 0.5461/0.6328 fringes at 632.8 nm.
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = new[]
        {
            ToleranceItem.Symmetric(new PowerFringes(1, 0.6328), 4.0),
            ToleranceItem.Symmetric(new PowerFringes(2, 0.5461), 4.0),
        };
        var d = ElementDrawings.Build(lens, items, Settings)[0];
        Assert.Equal(4.0, d.Front.PowerFringes, 12);
        Assert.Equal(4.0 * 0.5461 / 0.6328, d.Back.PowerFringes, 12);
    }

    [Fact]
    public void ToleranceSetsLeftOutAreFilledWithTheDefaultsAndSaidSo()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var d = ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(), Settings)[1];
        Assert.Equal(Settings.DefaultFringes, d.Front.PowerFringes);
        Assert.Equal(Settings.DefaultTiltArcmin, d.Front.TiltArcmin);
        Assert.Equal(Settings.DefaultIndex, d.Material.NdTolerance);
        Assert.Equal(9, d.Warnings.Count);      // power, irregularity and tilt of each surface, index, Abbe, thickness
    }

    [Fact]
    public void ABiasedThicknessIsDrawnAsItsTwoLimits()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = new[] { new ToleranceItem(new ThicknessChange(3), -0.02, 0.08) };
        var d = ElementDrawings.Build(lens, items, Settings)[1];
        Assert.Equal((-0.02, 0.08), (d.ThicknessMinus, d.ThicknessPlus));
    }

    [Fact]
    public void ACementedDoubletIsTwoDrawingsTheirSharedSurfaceUncoatedAndTheirMountTolerancesTheGroups()
    {
        var lens = ToleranceTests.Read(Doublet);
        var items = DefaultTolerances.Create(lens);
        var drawings = ElementDrawings.Build(lens, items, Settings with { Coating = "AR" });

        Assert.Equal(2, drawings.Count);
        var (crown, flint) = (drawings[0], drawings[1]);
        Assert.Equal(("N-BK7", "N-SF5"), (crown.Material.Name, flint.Material.Name));
        Assert.False(crown.Front.Cemented);
        Assert.True(crown.Back.Cemented);
        Assert.True(flint.Front.Cemented);
        Assert.Null(crown.Back.Coating);
        Assert.Equal("AR", crown.Front.Coating);
        Assert.Contains("CEMENTED", crown.Title);
        // The group 1-3 moves as one in its cell: both pieces carry its decentre and tilt.
        Assert.Equal(4, crown.MountTolerances.Count);
        Assert.All(flint.MountTolerances, m => Assert.Contains("group 1-3", m));
    }

    [Fact]
    public void CementedPartnersShareOneDiameterTheSmallestEitherCouldTake()
    {
        // The biconvex crown made 3 mm thick: at the clear aperture plus a millimetre its edge is under
        // half a millimetre, so it alone would be drawn smaller than the flint.
        var lens = ToleranceTests.Read(Doublet, s => s.Surfaces[1].Thickness = 3.0);
        var alone = ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(), Settings);
        Assert.Equal(alone[0].Diameter, alone[1].Diameter);
        var crown = alone[0];
        double aperture = Math.Max(crown.Front.ClearAperture, crown.Back.ClearAperture);
        Assert.True(crown.Diameter >= aperture);
        // The piece whose size changed says so; its partner, which set it, does not.
        Assert.Single(alone, d => d.Warnings.Any(w => w.Contains("to match its cemented partner")));

        // A singlet is left alone.
        var triplet = ElementDrawings.Build(ToleranceTests.Read(ToleranceTests.Triplet), Array.Empty<ToleranceItem>(), Settings);
        Assert.DoesNotContain(triplet, d => d.Warnings.Any(w => w.Contains("cemented partner")));
    }

    [Fact]
    public void TheWedgeOfTwoTiltedSurfacesIsTheirRootSumOfSquares()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = new[]
        {
            ToleranceItem.Symmetric(new SurfaceTilt(3, Axis.X), 3.0 / 60.0),
            ToleranceItem.Symmetric(new SurfaceTilt(4, Axis.X), 4.0 / 60.0),
        };
        var d = ElementDrawings.Build(lens, items, Settings)[1];
        double wedge = 5.0 / 60.0 * Math.PI / 180.0;
        Assert.Equal(wedge, d.WedgeRadians, 14);
        Assert.Equal(d.Diameter * Math.Tan(wedge), d.EdgeThicknessDifference, 14);
        Assert.Equal((d.Material.Nd - 1.0) * 5.0, d.DeviationArcmin, 10);
    }

    [Fact]
    public void AnAsphereIsDrawnThroughItsSagAndItsCoefficientsAreNoted()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet, s =>
        {
            s.Surfaces[1].Conic = -0.8;
            s.Surfaces[1].AsphericCoefficients[1] = -2e-5;     // the r⁴ term
        });
        var items = DefaultTolerances.Create(lens);
        var d = ElementDrawings.Build(lens, items, Settings)[0];
        Assert.True(d.Front.IsAspheric);
        Assert.NotNull(d.Front.DecentreMm);                   // the default set decentres aspheres
        Assert.Equal(2.5 - lens.System.Surfaces[1].Sag(7.5) + lens.System.Surfaces[2].Sag(7.5), d.EdgeThickness, 12);

        var text = string.Join("\n", Layout.Draw(d, Settings).Marks.OfType<Label>().Select(l => l.Text));
        Assert.Contains("aspheric", text);
        Assert.Contains("k = -0.800000", text);
        Assert.Contains("A4 = -2E-05", text);
    }

    [Theory]
    [InlineData(-0.01, 0.01, "k = -0.800000 ±0.01")]
    [InlineData(-0.005, 0.02, "k = -0.800000 +0.02/-0.005")]
    public void AConicToleranceIsStatedWithTheConic(double min, double max, string stated)
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet, s => s.Surfaces[1].Conic = -0.8);
        var items = new[] { new ToleranceItem(new ConicChange(1), min, max), new ToleranceItem(new ConicChange(3), min, max) };
        var drawings = ElementDrawings.Build(lens, items, Settings);
        Assert.Equal((min, max), drawings[0].Front.ConicTolerance);

        foreach (var standard in new[] { DrawingStandard.Iso10110, DrawingStandard.Asme })
        {
            var text = string.Join(" ", Layout.Draw(drawings[0], Settings with { Standard = standard }).Marks.OfType<Label>().Select(l => l.Text));
            Assert.Contains(stated, text, StringComparison.OrdinalIgnoreCase);
        }

        // Surface 3 is a sphere: a conic tolerance there is analyzed but has nothing to state.
        Assert.Null(drawings[1].Front.ConicTolerance);
        Assert.Contains(drawings[1].Warnings, w => w.Contains("surface 3: a conic tolerance on a sphere"));
    }

    [Fact]
    public void AGroupTiltsPivotIsInItsMountNote()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = ToleranceSpec.ParseSet("TETX 1-4 0.05 pivot:6.2\nTETY 1-4 0.05\n");
        var d = ElementDrawings.Build(lens, items, Settings)[0];
        Assert.Contains(d.MountTolerances, m => m.Contains("about the axis 6.2 mm after surface 1") && m.Contains("group 1-4"));
        Assert.Contains(d.MountTolerances, m => m.StartsWith("tilt") && !m.Contains("about"));
    }

    [Fact]
    public void SettingsReadBackFromTheirJson()
    {
        var s = new DrawingSettings { Standard = DrawingStandard.Asme, ScratchDig = "40-20", Coatings = new() { [2] = "BBAR" } };
        var back = DrawingSettings.FromJson(s.ToJson());
        Assert.Equal(DrawingStandard.Asme, back.Standard);
        Assert.Equal("40-20", back.ScratchDig);
        Assert.Equal("BBAR", back.Coatings[2]);
        Assert.Equal(DrawingStandard.Iso10110, DrawingSettings.FromJson("{ \"scratchDig\": \"20-10\", }").Standard);
    }

    private const string Custom = """
        {
          "numberPrefix": "DBL-",
          "coating": "AR1",
          "coatings": { "3": "V-coat" },
          "coatingSpecs": { "AR1": "Broadband AR 450-650 nm, Ravg ≤ 0.5 % per surface, MIL-C-675C" },
          "notes": [ "Clean per shop procedure 12." ],
          "elements": {
            "1": { "title": "CROWN, ACHROMAT", "number": "100-2045", "description": "Front of objective" },
            "DBL-02": { "notes": [ "Flint: handle with gloves." ] }
          }
        }
        """;

    [Fact]
    public void SettingsCanRenameAnElementDescribeItAndWriteOutACoatingInFull()
    {
        var lens = ToleranceTests.Read(Doublet);
        var drawings = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), DrawingSettings.FromJson(Custom) with { Date = "2026-10-12" });
        var (crown, flint) = (drawings[0], drawings[1]);

        Assert.Equal(("100-2045", "CROWN, ACHROMAT", "Front of objective"), (crown.Number, crown.Title, crown.Description));
        Assert.Equal("DBL-02", flint.Number);
        Assert.Contains("CEMENTED", flint.Title);                     // left as the drawing has it
        Assert.Equal(new[] { "Clean per shop procedure 12." }, crown.Notes);
        Assert.Equal(new[] { "Clean per shop procedure 12.", "Flint: handle with gloves." }, flint.Notes);
        // Only the crown's front is AR1; the flint's back is the named V-coat, which has no spec to write out.
        Assert.Equal(new[] { "AR1" }, crown.CoatingNotes.Select(c => c.Name));
        Assert.Empty(flint.CoatingNotes);
        Assert.Equal("V-coat", flint.Back.Coating);

        foreach (var standard in new[] { DrawingStandard.Iso10110, DrawingStandard.Asme })
        {
            var sheet = Layout.Draw(crown, new DrawingSettings { Standard = standard });
            var text = string.Join("\n", sheet.Marks.OfType<Label>().Select(l => l.Text));
            Assert.Contains("100-2045", text);
            Assert.Contains("Front of objective", text);
            Assert.Contains("Ravg ≤ 0.5 % per surface", text);           // as written, not upper-cased
            Assert.Contains("Clean per shop procedure 12.", text);
            Assert.Empty(sheet.Warnings);
        }
    }

    [Fact]
    public void ARenamedElementIsTheOneItsCementedPartnerNames()
    {
        var lens = ToleranceTests.Read(Doublet);
        var settings = Settings with { Elements = new() { ["1"] = new ElementOverride { Number = "100-2045" },
                                                          ["2"] = new ElementOverride { Number = "100-2046" } } };
        var drawings = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), settings);
        Assert.Equal(new[] { "100-2045", "100-2046" }, drawings.Select(d => d.Number));
        Assert.DoesNotContain(drawings.SelectMany(d => d.Warnings), w => w.Contains("OPT-"));
    }

    [Fact]
    public void SettingsNamingNoElementOrRepeatingANumberAreRefused()
    {
        var lens = ToleranceTests.Read(Doublet);
        var items = DefaultTolerances.Create(lens);
        var stray = Assert.Throws<ArgumentException>(() => ElementDrawings.Build(lens, items,
            Settings with { Elements = new() { ["4"] = new ElementOverride { Title = "X" } } }));
        Assert.Contains("1 (OPT-01)", stray.Message);
        Assert.Throws<ArgumentException>(() => ElementDrawings.Build(lens, items,
            Settings with { Elements = new() { ["1"] = new ElementOverride { Number = "OPT-02" } } }));
    }

    [Fact]
    public void NotesThatDoNotFitAreReportedNotDroppedSilently()
    {
        var lens = ToleranceTests.Read(Doublet);
        var many = Enumerable.Range(1, 40).Select(i => $"Note {i}: a long line of shop instructions that takes room.").ToList();
        foreach (var standard in new[] { DrawingStandard.Iso10110, DrawingStandard.Asme })
        {
            var settings = Settings with { Standard = standard, Notes = many };
            var d = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), settings)[0];
            Assert.Contains(Layout.Draw(d, settings).Warnings, w => w.Contains("did not fit"));
        }
    }
}

/// <summary>The sheets: everything on the sheet, and each writer's file well formed.</summary>
public class SheetTests
{
    public static IEnumerable<object[]> Sheets()
    {
        foreach (var standard in new[] { DrawingStandard.Iso10110, DrawingStandard.Asme })
            foreach (var lens in new[] { ToleranceTests.Triplet, ElementDrawingTests.Doublet })
                yield return new object[] { standard, lens };
    }

    private static List<Sheet> Draw(DrawingStandard standard, string path)
    {
        var lens = ToleranceTests.Read(path);
        var settings = new DrawingSettings { Standard = standard, Date = "2026-10-12", Project = "test" };
        return ElementDrawings.Build(lens, DefaultTolerances.Create(lens), settings).Select(d => Layout.Draw(d, settings)).ToList();
    }

    [Theory]
    [MemberData(nameof(Sheets))]
    public void EverythingIsInsideTheFrame(DrawingStandard standard, string lens)
    {
        foreach (var sheet in Draw(standard, lens))
        {
            foreach (var mark in sheet.Marks)
            {
                switch (mark)
                {
                    case Polyline p:
                        Assert.All(p.Points, q => { Assert.InRange(q.X, 10.0, sheet.Width - 10.0); Assert.InRange(q.Y, 10.0, sheet.Height - 10.0); });
                        break;
                    case Label t:
                        double width = Helvetica.Width(PdfWriter.Sanitize(t.Text), t.Height, t.Bold);
                        double left = t.Align switch { Align.Center => t.At.X - width / 2, Align.Right => t.At.X - width, _ => t.At.X };
                        Assert.InRange(left, 10.0, sheet.Width - 10.0);
                        Assert.InRange(left + width, 10.0, sheet.Width - 10.0);
                        Assert.InRange(t.At.Y, 10.0, sheet.Height - 10.0);
                        break;
                }
            }
        }
    }

    /// <summary>Text keeps 1 mm clear of the frame, measured with Helvetica's own widths: all-capital notes once ran into it.</summary>
    [Theory]
    [MemberData(nameof(Sheets))]
    public void TextKeepsClearOfTheFrame(DrawingStandard standard, string lens)
    {
        foreach (var sheet in Draw(standard, lens))
            foreach (var t in sheet.Marks.OfType<Label>())
            {
                var (left, right, _, _) = Box(t);
                Assert.True(left >= 11.0 && right <= sheet.Width - 11.0, $"{sheet.Name}: \"{t.Text}\" spans {left:F1} to {right:F1} mm");
            }
    }

    /// <summary>
    /// No dimension or extension line runs through text: a thickness wider written than the
    /// element is thick once had both extension lines through it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sheets))]
    public void NoDimensionLineCrossesText(DrawingStandard standard, string lens)
    {
        foreach (var sheet in Draw(standard, lens))
        {
            var boxes = sheet.Marks.OfType<Label>().Select(t => (t, box: Box(t))).ToList();
            foreach (var line in sheet.Marks.OfType<Polyline>().Where(p => p.Layer == Layers.Dimensions && !p.Filled))
                for (int k = 0; k + 1 < line.Points.Count; k++)
                    foreach (var (t, (x0, x1, y0, y1)) in boxes)
                        Assert.False(Crosses(line.Points[k], line.Points[k + 1], x0, x1, y0, y1),
                                     $"{sheet.Name}: a dimension line crosses \"{t.Text}\"");
        }
    }

    /// <summary>A label's extent: left, right, baseline and capital height.</summary>
    private static (double Left, double Right, double Bottom, double Top) Box(Label t)
    {
        double width = Helvetica.Width(PdfWriter.Sanitize(t.Text), t.Height, t.Bold);
        double left = t.Align switch { Align.Center => t.At.X - width / 2, Align.Right => t.At.X - width, _ => t.At.X };
        return (left, left + width, t.At.Y, t.At.Y + t.Height);
    }

    /// <summary>Whether the segment a-b passes through the inside of the box (Liang-Barsky).</summary>
    private static bool Crosses(Pt a, Pt b, double x0, double x1, double y0, double y1)
    {
        double t0 = 0.0, t1 = 1.0, dx = b.X - a.X, dy = b.Y - a.Y;
        foreach (var (p, q) in new[] { (-dx, a.X - x0), (dx, x1 - a.X), (-dy, a.Y - y0), (dy, y1 - a.Y) })
        {
            if (p == 0.0) { if (q <= 0.0) return false; continue; }
            double r = q / p;
            if (p < 0.0) t0 = Math.Max(t0, r); else t1 = Math.Min(t1, r);
            if (t0 >= t1) return false;
        }
        return true;
    }

    [Fact]
    public void TheSheetSizesAreTheStandardsOwn()
    {
        Assert.Equal((297.0, 210.0), (Draw(DrawingStandard.Iso10110, ToleranceTests.Triplet)[0].Width, Draw(DrawingStandard.Iso10110, ToleranceTests.Triplet)[0].Height));
        var ansi = Draw(DrawingStandard.Asme, ToleranceTests.Triplet)[0];
        Assert.Equal(11.0 * 25.4, ansi.Width, 9);
        Assert.Equal(8.5 * 25.4, ansi.Height, 9);
    }

    [Fact]
    public void TheIsoTableCarriesTheCodedIndications()
    {
        var text = string.Join("\n", Draw(DrawingStandard.Iso10110, ToleranceTests.Triplet)[1].Marks.OfType<Label>().Select(l => l.Text));
        Assert.Contains("3/10(2)", text);         // commercial: 10 fringes of power, 2 of irregularity
        Assert.Contains("4/3.44'", text);         // 0.001 rad
        Assert.Contains("5/3x0.16", text);
        Assert.Contains("0/20", text);
        Assert.Contains("nd = 1.62004 ± 0.0020", text);
    }

    [Fact]
    public void TheUsNotesCarryScratchDigAndWedge()
    {
        var text = string.Join("\n", Draw(DrawingStandard.Asme, ToleranceTests.Triplet)[1].Marks.OfType<Label>().Select(l => l.Text));
        Assert.Contains("MIL-PRF-13830B", text);
        Assert.Contains("ASME Y14.18", text);
        Assert.Contains("EDGE THICKNESS DIFFERENCE", text);
        Assert.Contains("60-40", text);
    }

    [Theory]
    [InlineData(4.3, 4.0)]
    [InlineData(3.99, 2.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.3, 0.25)]
    [InlineData(25.0, 10.0)]
    public void ViewsAreDrawnToAStandardScale(double most, double scale) => Assert.Equal(scale, Layout.StandardScale(most));

    [Fact]
    public void NotesWrapToTheirWidth()
    {
        var lines = Layout.Wrap("ONE TWO THREE FOUR FIVE SIX SEVEN EIGHT NINE TEN", 30.0, 2.5);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(Helvetica.Width(l, 2.5) <= 30.0));
        Assert.Equal("ONE TWO THREE FOUR FIVE SIX SEVEN EIGHT NINE TEN", string.Join(" ", lines));
    }

    [Fact]
    public void ThePdfsCrossReferenceTableFindsEveryObject()
    {
        var sheets = Draw(DrawingStandard.Iso10110, ToleranceTests.Triplet);
        byte[] pdf = PdfWriter.ToBytes(sheets);
        string text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);

        int startxref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", text[startxref..]);
        var lines = text[startxref..].Split('\n');
        int count = int.Parse(lines[1].Split(' ')[1]);
        for (int id = 1; id < count; id++)
        {
            long offset = long.Parse(lines[2 + id][..10]);
            Assert.StartsWith($"{id} 0 obj", text[(int)offset..]);
        }
        Assert.Contains($"/Count {sheets.Count}", text);
        foreach (int length in System.Text.RegularExpressions.Regex.Matches(text, @"/Length (\d+) >>\nstream\n").Select(m => int.Parse(m.Groups[1].Value)))
            Assert.True(length > 0);
    }

    [Fact]
    public void PdfTextIsInWinAnsi()
    {
        Assert.Equal("vd ± 0.5 Ø 3°", PdfWriter.Sanitize("νd ± 0.5 ⌀ 3°"));
        Assert.Equal("lambda <= 1", PdfWriter.Sanitize("λ ≤ 1"));
    }

    [Fact]
    public void TheDxfIsR12WithEveryMarkAsAnEntity()
    {
        var sheet = Draw(DrawingStandard.Asme, ToleranceTests.Triplet)[0];
        var lines = DxfWriter.ToText(sheet).Split('\n');
        Assert.Equal(0, (lines.Length - 1) % 2);                 // group code, value; a last newline
        var pairs = Enumerable.Range(0, lines.Length / 2).Select(i => (lines[2 * i].Trim(), lines[2 * i + 1])).ToList();
        Assert.Contains(("1", "AC1009"), pairs);
        Assert.Equal(("0", "EOF"), pairs[^1]);
        int entities = pairs.Count(p => p.Item1 == "0" && p.Item2 is "LINE" or "POLYLINE" or "SOLID" or "TEXT");
        Assert.Equal(sheet.Marks.Count, entities);
        foreach (var layer in Layers.All) Assert.Contains(("2", layer), pairs);
        Assert.Equal("%%c15.00 %%p0.1", DxfWriter.Escape("Ø15.00 ±0.1"));
    }

    [Fact]
    public void TheSvgIsWellFormed()
    {
        var sheet = Draw(DrawingStandard.Iso10110, ToleranceTests.Triplet)[0];
        var svg = XDocument.Parse(SvgWriter.ToText(sheet));
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(sheet.Marks.OfType<Label>().Count(), svg.Descendants(ns + "text").Count());
        Assert.Equal("0 0 297 210", svg.Root!.Attribute("viewBox")!.Value);
    }
}

/// <summary>A title block whose values may be long, and an element aligned at assembly.</summary>
public class TitleBlockAndAdjustmentTests
{
    private static readonly DrawingSettings Settings = new() { Date = "2026-10-15" };

    private static List<Label> Labels(Sheet sheet) => sheet.Marks.OfType<Label>().ToList();

    [Theory]
    [InlineData(DrawingStandard.Iso10110)]
    [InlineData(DrawingStandard.Asme)]
    public void ALongProjectShrinksToItsCellAndALongerOneIsCutShortAndSaidSo(DrawingStandard standard)
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var d = ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(), Settings)[0];
        const double cell = 120 - 22 - 1.5;

        string longName = "PPP_TRIPLET_ANY_GLASS_UC_T1_AfterSplit_AfterAshpere_MultiStart";
        var sheet = Layout.Draw(d, Settings with { Standard = standard, Project = longName });
        var project = Labels(sheet).Single(l => l.Text == longName);
        Assert.True(project.Height < 2.5 && Helvetica.Width(project.Text, project.Height) <= cell);
        Assert.DoesNotContain(sheet.Warnings, w => w.StartsWith("title block"));
        foreach (var label in new[] { "TITLE", "DRAWING", "MATERIAL", "SCALE", "STANDARD", "PROJECT", "DRAWN" })
            Assert.Contains(Labels(sheet), l => l.Text == label);

        string longer = string.Concat(Enumerable.Repeat(longName + " ", 3)).Trim();
        sheet = Layout.Draw(d, Settings with { Standard = standard, Project = longer });
        var cut = Labels(sheet).Single(l => l.Text.EndsWith("..."));
        Assert.StartsWith(longName[..20], cut.Text);
        Assert.True(Helvetica.Width(cut.Text, cut.Height) <= cell);
        Assert.Contains(sheet.Warnings, w => w.StartsWith("title block project") && w.Contains("shortened"));

        // A short value keeps the usual size.
        Assert.Equal(2.5, Labels(Layout.Draw(d, Settings with { Standard = standard, Project = "Triplet" })).Single(l => l.Text == "Triplet").Height);
    }

    /// <summary>
    /// An element decentered to align the lens is drawn as set at assembly, not held to the mount
    /// tolerance the set gives it; its tilt, and a group's decenter around it, are still tolerances.
    /// </summary>
    [Fact]
    public void AnElementAlignedAtAssemblyIsDrawnSoAndKeepsItsOtherMountTolerances()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = ToleranceSpec.ParseSet("TEDX 3-4 0.2\nTEDY 3-4 0.2\nTETX 3-4 0.05\nTEDX 1-4 0.03\n");
        var adjust = new Adjustment[]
        {
            new ElementDecenterAdjustment(3, 4, Axis.X) { Travel = 0.3 },
            new ElementDecenterAdjustment(3, 4, Axis.Y) { Travel = 0.3 },
        };

        var plain = ElementDrawings.Build(lens, items, Settings)[1].MountTolerances;
        Assert.Contains("decenter x ±0.2 mm", plain);

        var middle = ElementDrawings.Build(lens, items, Settings, adjustments: adjust)[1].MountTolerances;
        Assert.DoesNotContain(middle, m => m.StartsWith("decenter x ±0.2") || m.StartsWith("decenter y ±0.2"));
        Assert.Contains("decenter x and y set at assembly to align the lens, within ±0.3 mm", middle);
        Assert.Contains(middle, m => m.StartsWith("tilt x ±0.05°"));
        Assert.Contains(middle, m => m.StartsWith("decenter x ±0.03 mm") && m.Contains("group 1-4"));

        // The front element is not adjusted; without a travel the note gives none.
        var front = ElementDrawings.Build(lens, items, Settings, adjustments: adjust)[0].MountTolerances;
        Assert.DoesNotContain(front, m => m.Contains("set at assembly"));
        var untravelled = ElementDrawings.Build(lens, items, Settings,
            adjustments: new Adjustment[] { new ElementTiltAdjustment(3, 4, Axis.X), new ElementTiltAdjustment(3, 4, Axis.Y) })[1].MountTolerances;
        Assert.Contains("tilt x and y set at assembly to align the lens", untravelled);
        Assert.DoesNotContain(untravelled, m => m.StartsWith("tilt x ±"));
    }
}
