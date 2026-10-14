// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// Copyright (c) 2026 OOXGN. All rights reserved.

using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AutocadTools.Services
{
    public static class AlignSkewTransform
    {
        public static Matrix3d CreateMatrix(Point3d sourceBase, Point3d targetBase,
            TransformCalculator.TransformationParams parameters)
        {
            var scale = new Matrix3d(new[]
            {
                parameters.ScaleX, 0.0, 0.0, 0.0,
                0.0, parameters.ScaleY, 0.0, 0.0,
                0.0, 0.0, 1.0, 0.0,
                0.0, 0.0, 0.0, 1.0
            });

            // The same order as rotating the definition and exploding its scaled reference.
            return Matrix3d.Displacement(targetBase - Point3d.Origin)
                * Matrix3d.Rotation(parameters.Rotation2, Vector3d.ZAxis, Point3d.Origin)
                * scale
                * Matrix3d.Rotation(parameters.Rotation1, Vector3d.ZAxis, Point3d.Origin)
                * Matrix3d.Displacement(Point3d.Origin - sourceBase);
        }

        public static void TransformText(Entity entity, Matrix3d transformation)
        {
            Point3d anchor;
            Vector3d direction;
            Vector3d normal;
            if (entity is DBText text)
            {
                // Justified text is located by AlignmentPoint, not by Position.
                anchor = text.IsDefaultAlignment ? text.Position : text.AlignmentPoint;
                normal = text.Normal;
                direction = new Vector3d(Math.Cos(text.Rotation), Math.Sin(text.Rotation), 0)
                    .TransformBy(Matrix3d.PlaneToWorld(normal));
            }
            else if (entity is MText mtext)
            {
                anchor = mtext.Location;
                direction = mtext.Direction;
                normal = mtext.Normal;
            }
            else
            {
                return;
            }

            // Transform the anchor and baseline, but apply only a rigid motion to the glyphs.
            // This preserves height, width, obliquing, formatting and justification.
            Vector3d targetDirection = direction.TransformBy(transformation);
            targetDirection -= normal * targetDirection.DotProduct(normal);
            double angle = Math.Atan2(normal.DotProduct(direction.CrossProduct(targetDirection)),
                direction.DotProduct(targetDirection));
            var rotation = Matrix3d.Rotation(angle, normal, anchor);
            var displacement = Matrix3d.Displacement(anchor.TransformBy(transformation) - anchor);
            entity.TransformBy(displacement * rotation);
        }
    }
}
