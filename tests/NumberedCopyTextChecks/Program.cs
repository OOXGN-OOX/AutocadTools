// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using AutocadTools.Models;
using AutocadTools.Services;

int count = 0;
void Check(bool condition, string message) { count++; if (!condition) throw new Exception(message); }
void Reject(Action action, string message)
{ try { action(); } catch (Exception e) when (e is ArgumentException || e is OverflowException) { Check(true, message); return; } throw new Exception(message); }
string Format(string text, NumberedCopyOptions options, int index, bool formatted = false) =>
    NumberedCopyText.Format(new NumberedCopySource("test", text, formatted), options, index);
var options = new NumberedCopyOptions { Start = 7, Digits = 3 };
Check(Format("П1-007 DN50", options, 1) == "П008-007 DN50", "Only selected numeric occurrence changes");
options.NumberIndex = 1;
Check(Format("П1-007 DN50", options, 1) == "П1-008 DN50", "Hyphen in identifiers is a separator");
Check(NumberedCopyText.Find("П1-007 DN50")[1].Digits == 3, "Infer leading zero width");
options = new NumberedCopyOptions { Start = 1.20m, Step = 0.05m };
Check(Format("Отм. 1,20 м", options, 3) == "Отм. 1,35 м", "Comma decimals without binary rounding drift");
Check(Format("1.20", options, 100) == "6.20", "Dot decimal separator and source precision");
options.TrimFractionZeros = true;
Check(Format("1.20", options, 100) == "6.2", "Trim only fractional zeros");
options = new NumberedCopyOptions { Start = 100, Step = 0, TrimFractionZeros = true };
Check(Format("100", options, 1) == "100", "Do not trim integer zeros; allow zero increment");
options = new NumberedCopyOptions { Start = -1.5m, Step = -0.5m, Digits = 3 };
Check(Format("(-1.50)", options, 1) == "(-002.00)", "Negative decimal padding excludes the sign");
options.Start = 0; options.Step = 1;
Check(Format("+001", options, 2) == "+002", "Preserve explicit plus sign");
options.KeepSurrounding = false; options.Prefix = "В-"; options.Suffix = "а";
Check(Format("+001", options, 2) == "В-+002а", "Replace surrounding text with explicit prefix/suffix");
string mtext = @"{\fISOCPEUR|b0|i0;\C1;\H0.7x;\W0.8;П-007}\P{\H2;DN50} \U+00B0 %%123";
var numbers = NumberedCopyText.Find(mtext, true);
Check(numbers.Count == 2 && numbers[0].Text == "007" && numbers[1].Text == "50", "MText font, color, height, width and Unicode controls are excluded");
options = new NumberedCopyOptions { Start = 7, Digits = 3 };
Check(Format(mtext, options, 1, true) == mtext.Replace("007", "008"), "Preserve MText inline formatting and other numbers byte-for-byte");
Check(NumberedCopyText.Find(@"%<\AcObjProp Object(%<\_ObjId 123456>%).Length \f \""%lu2%pr2\"">% 15", true).Count == 1,
    "Exclude complete nested fields, including field format precision");
Check(NumberedCopyText.Find(@"\S1/2;", true).Count == 2, "Stacked fraction content remains selectable");
Check(NumberedCopyText.Find("2147483648")[0].Value == 2147483648m, "Numbers are not limited to Int32");
Check(NumberedCopyText.TryDecimal(" -0,25 ", out var parsed) && parsed == -0.25m, "Localized numeric input");
Check(!NumberedCopyText.TryDecimal("NaN", out _) && !NumberedCopyText.TryDecimal("1e3", out _), "Reject non-decimal input");
options.Start = decimal.MaxValue;
Reject(() => Format("1", options, 1), "Reject arithmetic overflow");
options = new NumberedCopyOptions { Digits = -1 };
Reject(() => options.Validate(), "Reject negative digit count");
options = new NumberedCopyOptions { Mode = NumberedCopyMode.Curve, Distance = double.NaN };
Reject(() => options.Validate(), "Reject non-finite path step");
options.Vertices = true; options.Distance = double.NaN;
options.Validate(); Check(true, "Ignore unused step in vertex mode");
options.OffsetX = double.PositiveInfinity;
Reject(() => options.Validate(), "Reject non-finite path offset");
Console.WriteLine($"PASS: {count} numbered-copy text and validation checks.");
