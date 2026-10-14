// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using AutocadTools.Services;

int checks = 0;
void Check(double length, double spacing, int segments, bool divide, bool closed, int count, double step)
{
    var result = MultiCopyLayout.Create(length, spacing, segments, divide, closed);
    if (result.Count != count || Math.Abs(result.Step - step) > 1e-8) throw new Exception("Incorrect copy layout");
    checks++;
}
void Reject(Action action)
{
    try { action(); } catch (ArgumentException) { checks++; return; } catch (InvalidOperationException) { checks++; return; }
    throw new Exception("Invalid parameters accepted");
}
Check(90, 1, 3, true, false, 3, 30);
Check(90, 1, 1, true, false, 1, 90);
Check(90, 1, 3, true, true, 2, 30);
Check(90, 30, 0, false, false, 3, 30);
Check(90, 30, 0, false, true, 2, 30);
Check(100, 30, 0, false, false, 3, 30);
Check(10, 30, 0, false, false, 0, 30);
Check(0.3, 0.1, 0, false, false, 3, 0.1);
Check(0, 10, 3, true, false, 0, 0);
Check(10000, 1, 0, false, false, 10000, 1);
Reject(() => MultiCopyLayout.Create(10001, 1, 0, false));
Reject(() => MultiCopyLayout.Create(10, 0, 0, false));
Reject(() => MultiCopyLayout.Create(10, double.NaN, 0, false));
Reject(() => MultiCopyLayout.Create(double.PositiveInfinity, 1, 0, false));
Reject(() => MultiCopyLayout.Create(10, 1, 0, true));
Reject(() => MultiCopyLayout.Create(10, 1, 10001, true));
Console.WriteLine($"PASS: {checks} multi-copy layout checks.");
