using AutocadTools.Infrastructure;
using AutocadTools.Models;
using AutocadTools.Services;
using AutocadTools.ViewModels;
using ClosedXML.Excel;

var results = Path.Combine(AppContext.BaseDirectory, "results", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(results);
int checks = 0;
void Assert(bool value, string message) { if (!value) throw new Exception(message); checks++; }

var grid = new[] { ("A", 0.0, 100.0), ("B", 100.0, 101.0), ("C", 0.5, 0.0), ("D", 100.5, 0.5) };
foreach (var scale in new[] { 1, 100 })
{
    var scaled = grid.Select(p => (p.Item1, X: p.Item2 * scale, Y: p.Item3 * scale)).Reverse().ToArray();
    foreach (var pair in new[] { (PlotSearchOrder.TopToBottom, "ABCD"), (PlotSearchOrder.BottomToTop, "CDAB"),
        (PlotSearchOrder.LeftToRight, "ACBD"), (PlotSearchOrder.RightToLeft, "BDAC") })
        Assert(string.Concat(PlotFrameOrdering.Sort(scaled, p => p.X, p => p.Y, pair.Item1, 2 * scale)
            .Select(p => p.Item1)) == pair.Item2, "Frame direction or scale tolerance failed: " + pair);
}
Assert(PlotFrameOrdering.Sort(Array.Empty<int>(), i => i, i => i, PlotSearchOrder.TopToBottom, 10).Count == 0, "Empty frame ordering failed");
var chain = new[] { 100, 94, 88 };
Assert(PlotFrameOrdering.Sort(chain, i => -i, i => i, PlotSearchOrder.TopToBottom, 10).SequenceEqual(new[] { 100, 94, 88 }), "Row tolerance chained across distinct rows");

var settingsFile = Path.Combine(results, "settings.json");
string binder = Path.Combine(results, "Папка PDF", "Чертёж_Binder.pdf");
Assert(PdfOutputPaths.OpenTargets(binder, false, false, false, null).Length == 0, "Disabled post-print actions still opened output");
Assert(PdfOutputPaths.OpenTargets(binder, false, true, false, null).Single() == Path.GetDirectoryName(binder), "Binder folder action points elsewhere");
Assert(PdfOutputPaths.OpenTargets(binder, false, false, true, null).Single() == binder, "Binder file action points elsewhere");
Assert(PdfOutputPaths.OpenTargets(binder, false, true, true, null).Length == 2, "Combined post-print actions failed");
Assert(PdfOutputPaths.OpenTargets(results, true, true, false, "07").Single() == results, "Sheet folder action failed");
Assert(PdfOutputPaths.OpenTargets(results, true, false, true, "07/А").Single() == Path.Combine(results, "001_Лист_07_А.pdf"), "First generated sheet path/sanitization failed");
Assert(PdfOutputPaths.SheetFileName(1, null) == "002_Лист_2.pdf", "Missing sheet number fallback changed");
Assert(PdfOutputPaths.OpenTargets(results, true, false, true, "07", Path.Combine(results, "001_Лист_07_3.pdf")).Single()
    .EndsWith("001_Лист_07_3.pdf"), "Post-print action opened an old sheet instead of the indexed output");
AtomicFile.WriteAllText(settingsFile, "original");
AtomicFile.WriteAllText(settingsFile, "replacement");
Assert(File.ReadAllText(settingsFile) == "replacement", "Atomic replacement failed");
using (var locked = File.Open(settingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
{
    try { AtomicFile.WriteAllText(settingsFile, "must not win"); throw new Exception("Locked write accepted"); }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}
Assert(File.ReadAllText(settingsFile) == "replacement", "Failed save destroyed prior settings");
Assert(Directory.GetFiles(results, "*.tmp").Length == 0, "Temporary settings leaked");

var strip = new StripTextOptions();
Assert(MTextFormatting.Strip(@"{\C1;Красный}\PВторая строка", strip) == "Красный\nВторая строка", "Formatting/line feed failed");
Assert(MTextFormatting.Strip(@"First\PSecond;\pql;Third", strip) == "First\nSecond;Third", "Paragraph formatting consumed content");
Assert(MTextFormatting.Strip(@"\\C1; literal \{braces\}", strip) == @"\\C1; literal \{braces\}", "Escaped literal content changed");
Assert(MTextFormatting.Strip(@"\Hinvalid;keep", strip) == @"\Hinvalid;keep", "Malformed control code consumed text");
Assert(MTextFormatting.Strip(@"\S1#2;", new StripTextOptions { Stacking = true }) == "1/2", "Stack conversion failed");
Assert(MTextFormatting.Strip("1;\t2", strip) == "1; 2", "Tab reset deleted a number");
Assert(MTextFormatting.Strip(@"\C1;Keep", new StripTextOptions { Color = false }) == @"\C1;Keep", "Disabled option applied");
var longText = string.Concat(Enumerable.Repeat(@"\H", 50000));
var watch = System.Diagnostics.Stopwatch.StartNew();
Assert(MTextFormatting.Strip(longText, strip) == longText, "Unterminated code changed");
watch.Stop();
Assert(watch.Elapsed < TimeSpan.FromSeconds(2), "Unterminated input caused nonlinear parsing");

var numbering = new AutoNumSettings { Prefix = "P-", Suffix = "-S", Digits = 3 };
Assert(CalculatorEngine.Evaluate("(L + 50) * 1,15", 100) == 172.5, "Calculator length/parentheses/decimal comma failed");
Assert(CalculatorEngine.Evaluate("A / 2 + 10%", area: 100) == 50.1, "Calculator area/percentage failed");
Assert(CalculatorEngine.Evaluate("-2^2 + 2^3^2") == 508, "Calculator precedence/right associativity failed");
Assert(CalculatorEngine.Evaluate("1e3 ÷ 2 × 3") == 1500, "Calculator scientific notation/unicode operators failed");
foreach (var expression in new[] { "1/0", "1+", "(2+3", "2 3", "System.IO.File.Delete()", "1e999", new string('(', 100) })
{
    try { CalculatorEngine.Evaluate(expression); throw new Exception("Invalid calculator expression accepted: " + expression); }
    catch (FormatException) { }
    catch (ArithmeticException) { }
}
Assert(numbering.FormatNumber(-7) == "P--007-S", "Negative padded number failed");
Assert(numbering.FormatNumber(int.MinValue).Contains("-2147483648"), "Minimum integer failed");
numbering.Digits = int.MaxValue;
Assert(numbering.FormatNumber(1).Length < 30, "Invalid padding allocated unbounded text");

var now = DateTime.UtcNow;
var pdfReady = new PublishFileReadiness();
Assert(!pdfReady.Observe(now, 100, now, true), "Publication accepted a newly created PDF");
Assert(!pdfReady.Observe(now.AddSeconds(1), 100, now, true), "PDF stability delay missing");
Assert(!pdfReady.Observe(now.AddSeconds(2), 110, now.AddSeconds(2), true), "Growing PDF treated as complete");
Assert(pdfReady.Observe(now.AddSeconds(4), 110, now.AddSeconds(2), true), "Stable PDF not accepted without a publisher event");
Assert(!pdfReady.Observe(now.AddSeconds(5), 110, now.AddSeconds(2), false), "Locked PDF treated as complete");
Assert(!pdfReady.Observe(now.AddSeconds(6), 110, now.AddSeconds(2), true), "Writer lock did not reset stability delay");
Assert(!pdfReady.Observe(now.AddSeconds(8), 110, now.AddSeconds(8), true), "Same-size PDF rewrite not detected");
Assert(pdfReady.Observe(now.AddSeconds(10), 110, now.AddSeconds(8), true), "Rewritten stable PDF not accepted");
var session = new FileEditSession(now, now, 100);
Assert(!session.IsReady(now.AddSeconds(3), false, now, 100), "Imported before editor opened");
Assert(!session.IsReady(now.AddSeconds(4), true, now, 100), "Imported while locked");
Assert(session.IsReady(now.AddSeconds(5), false, now, 100), "Editor close not detected");
session = new FileEditSession(now, now, 100);
Assert(!session.IsReady(now.AddSeconds(4), false, now.AddSeconds(4), 110), "Import raced an unlocked editor save");
Assert(!session.IsReady(now.AddSeconds(5), false, now.AddSeconds(4), 110), "Stability delay missing");
Assert(session.IsReady(now.AddSeconds(6), false, now.AddSeconds(4), 110), "Unlocked editor save not detected");

var xlsx = Path.Combine(results, "workbook.xlsx");
using (var workbook = new XLWorkbook())
{
    var sheet = workbook.AddWorksheet("Table");
    sheet.Cell(5, 3).Value = "Only C5";
    workbook.SaveAs(xlsx);
}
var data = ExcelWorkbookService.Read(xlsx);
Assert(data.Rows == 5 && data.Columns == 3 && data.Values[4, 2] == "Only C5", "Sparse cell position was lost");
using (var workbook = new XLWorkbook())
{
    workbook.AddWorksheet("Скрытый").Hide();
    workbook.AddWorksheet("Ведомость").Cell(2, 2).Value = "Первый видимый";
    workbook.SaveAs(xlsx);
}
data = ExcelWorkbookService.Read(xlsx, true);
Assert(data.Rows == 2 && data.Values[1, 1] == "Первый видимый", "Ordinary workbook worksheet selection failed");
try { ExcelWorkbookService.Read(xlsx); throw new Exception("Editing workbook without Table sheet accepted"); }
catch (InvalidDataException) { checks++; }
var values = new string[4, 3];
values[0, 0] = "Merged"; values[3, 2] = "Текст\\С";
ExcelWorkbookService.Save(xlsx, new TableWorkbookData(4, 3, values, new() { new ExcelMerge(0, 0, 0, 1) }));
data = ExcelWorkbookService.Read(xlsx);
Assert(data.Rows == 4 && data.Columns == 3 && data.Values[3, 2] == values[3, 2] && data.Merges.Count == 1, "Workbook round trip failed");
ExcelWorkbookService.Save(xlsx, new TableWorkbookData(3, 2, new string[3, 2], new()));
data = ExcelWorkbookService.Read(xlsx);
Assert(data.Rows == 3 && data.Columns == 2, "Empty table dimensions lost");
using (var workbook = new XLWorkbook())
{
    workbook.AddWorksheet("Table");
    var meta = workbook.AddWorksheet("_meta");
    meta.Cell(1, 1).Value = 1048576; meta.Cell(1, 2).Value = 16384;
    workbook.SaveAs(xlsx);
}
try { ExcelWorkbookService.Read(xlsx); throw new Exception("Oversized table accepted"); }
catch (InvalidDataException) { checks++; }

var model = new TestModel();
int notifications = 0;
model.PropertyChanged += (_, _) => notifications++;
model.Value = 1; model.Value = 1;
Assert(notifications == 1, "Duplicate property notifications");
model.Dispose(); model.Dispose();
Assert(model.CleanupCount == 1, "ViewModel disposed more than once");
Console.WriteLine($"PASS: {checks} core regressions; atomic settings, MTEXT, numbering, editor completion, XLSX, view-model lifetime.");
Console.WriteLine(results);

sealed class TestModel : BaseViewModel
{
    private int _value;
    public int CleanupCount;
    public int Value { get => _value; set => SetProperty(ref _value, value); }
    protected override void OnDisposing() => CleanupCount++;
}
