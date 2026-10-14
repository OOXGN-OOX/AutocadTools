// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;

namespace AutocadTools.Services
{
    public sealed class MultiCopyLayout
    {
        public const int MaximumCopies = 10000;
        public int Count { get; }
        public double Step { get; }
        private MultiCopyLayout(int count, double step) { Count = count; Step = step; }

        public static MultiCopyLayout Create(double length, double spacing, int segments, bool divide, bool closed = false)
        {
            if (double.IsNaN(length) || double.IsInfinity(length) || length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (divide && (segments < 1 || segments > MaximumCopies))
                throw new ArgumentOutOfRangeException(nameof(segments), "Число сегментов должно быть от 1 до 10000.");
            if (!divide && (double.IsNaN(spacing) || double.IsInfinity(spacing) || spacing <= 0))
                throw new ArgumentOutOfRangeException(nameof(spacing), "Шаг должен быть положительным.");
            if (length < 1e-9) return new MultiCopyLayout(0, 0);
            double step = divide ? length / segments : spacing;
            double count = divide ? segments : Math.Floor(length / spacing + 1e-9);
            if (closed && count > 0 && Math.Abs(count * step - length) <= Math.Max(1e-9, length * 1e-9)) count--;
            if (count > MaximumCopies)
                throw new InvalidOperationException("Более 10000 копий. Увеличьте шаг.");
            return new MultiCopyLayout((int)count, step);
        }
    }
}
