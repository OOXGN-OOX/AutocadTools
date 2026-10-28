// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using Autodesk.AutoCAD.Geometry;

namespace AutocadTools.Services
{
    /// <summary>
    /// Calculates affine transformation parameters for aligning objects using three point pairs.
    /// Implements mathematical formulas ported from Lisp code.
    /// </summary>
    public class TransformCalculator
    {
        private const double Epsilon = 1e-9;
        private const double DistanceEpsilon = 1e-6;

        /// <summary>
        /// Represents transformation parameters: rotation angles and scale factors.
        /// </summary>
        public class TransformationParams
        {
            public double Rotation1 { get; set; }
            public double Rotation2 { get; set; }
            public double ScaleX { get; set; }
            public double ScaleY { get; set; }
        }

        /// <summary>
        /// Calculates transformation parameters from three source points to three target points.
        /// </summary>
        public TransformationParams CalculateTransformation(
            Point3d source1, Point3d source2, Point3d source3,
            Point3d target1, Point3d target2, Point3d target3)
        {
            // Проверка на совпадение точек
            if (source1.DistanceTo(source2) < DistanceEpsilon || source1.DistanceTo(source3) < DistanceEpsilon ||
                target1.DistanceTo(target2) < DistanceEpsilon || target1.DistanceTo(target3) < DistanceEpsilon)
                throw new ArgumentException("Точки слишком близко.");
            if ((source2 - source1).CrossProduct(source3 - source1).Length < Epsilon ||
                (target2 - target1).CrossProduct(target3 - target1).Length < Epsilon)
                throw new ArgumentException("Три точки не должны лежать на одной прямой.");

            // Расчет базовых углов
            double angS1S2 = GetAngle2d(source1, source2);
            double angT1T2 = GetAngle2d(target1, target2);

            // Разница углов (Beta)
            double beta = GetAngle2d(source1, source3) - angS1S2;
            double beta1 = GetAngle2d(target1, target3) - angT1T2;

            // Дистанции
            double distS1S2 = source1.DistanceTo(source2);
            double distT1T2 = target1.DistanceTo(target2);
            double distS1S3 = source1.DistanceTo(source3);
            double distT1T3 = target1.DistanceTo(target3);

            // Коэффициент K
            double k = (distT1T2 * distS1S3) / (distS1S2 * distT1T3);

            // Тригонометрия
            double cosBeta = Math.Cos(beta);
            double sinBeta = Math.Sin(beta);
            double cosBeta1 = Math.Cos(beta1);
            double sinBeta1 = Math.Sin(beta1);

            // Расчет x1, x2 и альфа (Математика из Lisp)
            double x1 = CalculateX1(cosBeta, cosBeta1, sinBeta, k);
            double x2 = CalculateX2(cosBeta, cosBeta1, sinBeta, k);

            double numeratorA = CalculateAlphaNumerator(cosBeta, cosBeta1, k, x2);
            double alpha = CalculateAlpha(numeratorA, cosBeta, sinBeta, k, x1);
            double alpha1 = CalculateAlpha1(numeratorA, cosBeta, cosBeta1, sinBeta1, k, x1);

            // --- ИСПРАВЛЕНИЕ: Убрана жесткая проверка (Throw), добавлен безопасный расчет ---
            // Lisp просто делит. Если знаменатель 0, Lisp падает. 
            // Мы делаем безопасное деление: если угол совпал с осью, берем прямое отношение длин.

            double scaleY = SolveScale(distT1T2, Math.Sin(alpha1), distS1S2, Math.Sin(alpha));
            double scaleX = SolveScale(distT1T2, Math.Cos(alpha1), distS1S2, Math.Cos(alpha));

            return new TransformationParams
            {
                Rotation1 = alpha - angS1S2,
                Rotation2 = angT1T2 - alpha1,
                ScaleX = scaleX,
                ScaleY = scaleY
            };
        }

        /// <summary>
        /// Безопасный расчет масштаба: (len1 * factor1) / (len2 * factor2)
        /// </summary>
        private double SolveScale(double len1, double factor1, double len2, double factor2)
        {
            double denominator = len2 * factor2;

            // Если знаменатель близок к нулю (сингулярность угла),
            // возвращаем отношение длин как лучший вариант (или 1.0)
            if (Math.Abs(denominator) < 1e-12)
            {
                // Попытка спасти ситуацию: если числитель тоже 0, то масштаб неопределен (пусть будет 1)
                // Если числитель не 0, а знаменатель 0 -> бесконечность. Вернем len1/len2 как fallback.
                return len1 / len2;
            }
            return (len1 * factor1) / denominator;
        }

        private double CalculateX1(double cosB, double cosB1, double sinB, double k)
        {
            double val = (cosB * cosB1) - k + (Math.Pow(sinB, 2) * k);
            return Math.Abs(val) < Epsilon ? Math.PI : val;
        }

        private double CalculateX2(double cosB, double cosB1, double sinB, double k)
        {
            double cb2 = Math.Pow(cosB, 2), cb3 = Math.Pow(cosB, 3), cb4 = Math.Pow(cosB, 4), cb5 = Math.Pow(cosB, 5), cb6 = Math.Pow(cosB, 6);
            double sb2 = Math.Pow(sinB, 2), sb4 = Math.Pow(sinB, 4), sb6 = Math.Pow(sinB, 6);
            double k2 = Math.Pow(k, 2), k3 = Math.Pow(k, 3), k4 = Math.Pow(k, 4);
            double cb1_2 = Math.Pow(cosB1, 2);

            double sum = 4 * cb1_2 * k2 * cb4 - 4 * cosB1 * k * cb3 + 4 * cosB1 * k3 * cb3 - 8 * cosB1 * k3 * cb5
                         + cb2 - 2 * k2 * cb2 + 4 * k2 * cb4 + k4 * cb2 - 4 * k4 * cb4 + 4 * k4 * cb6
                         + 4 * k2 * cb2 * cb1_2 * sb2 - 8 * k3 * cosB * cosB1 * sb2 + 8 * k3 * cosB * cosB1 * sb4
                         + 4 * sb2 * k4 - 8 * sb4 * k4 + 4 * sb6 * k4;

            double val = (sum > 0) ? Math.Sqrt(sum) : 0;
            return Math.Abs(val) < Epsilon ? Math.PI : val;
        }

        private double CalculateAlphaNumerator(double cosB, double cosB1, double k, double x2)
        {
            return -2 * cosB1 * k * Math.Pow(cosB, 2) + cosB - (Math.Pow(k, 2) * cosB) + (2 * Math.Pow(k, 2) * Math.Pow(cosB, 3)) - x2;
        }

        private double CalculateAlpha(double numA, double cosB, double sinB, double k, double x1)
        {
            double den = 2 * k * sinB * x1;
            // Если знаменатель 0, atan не сработает корректно, вернем 0
            if (Math.Abs(den) < Epsilon) return 0;
            return -Math.Atan(numA / den);
        }

        private double CalculateAlpha1(double numA, double cosB, double cosB1, double sinB1, double k, double x1)
        {
            if (Math.Abs(x1) < Epsilon) return 0;
            
            double termKCosB = -k * cosB;
            double termComplex = numA / (-2.0 * x1);
            double sum = termKCosB + termComplex;
            
            double y_arg;
            if (Math.Abs(cosB1) < Epsilon)
                y_arg = sum * 1e15;
            else
                y_arg = sum / cosB1; // Lisp делит на cos(b1)

            return Math.Atan2(y_arg, sinB1);
        }

        private double GetAngle2d(Point3d p1, Point3d p2)
        {
            return (p2 - p1).Convert2d(new Plane()).Angle;
        }
    }
}

