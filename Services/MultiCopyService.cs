// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AutocadTools.Services
{
    public static class MultiCopyService
    {
        public static Vector3d[] AlongLine(Point3d start, Point3d end, double spacing, int segments, bool divide)
        {
            var vector = end - start;
            var layout = MultiCopyLayout.Create(vector.Length, spacing, segments, divide);
            if (layout.Count == 0) return Array.Empty<Vector3d>();
            var direction = vector.GetNormal();
            return Enumerable.Range(1, layout.Count).Select(i => direction * (i * layout.Step)).ToArray();
        }

        public static Vector3d[] AlongCurve(Curve curve, Point3d pick, double spacing, int segments, bool divide)
        {
            double length = curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
            var layout = MultiCopyLayout.Create(length, spacing, segments, divide, curve.Closed);
            if (layout.Count == 0) return Array.Empty<Vector3d>();
            double originDistance = 0;
            bool reverse = !curve.Closed && pick.DistanceTo(curve.EndPoint) < pick.DistanceTo(curve.StartPoint);
            if (curve.Closed)
                originDistance = curve.GetDistAtPoint(curve.GetClosestPointTo(pick, false));
            else if (reverse)
                originDistance = length;
            var origin = curve.GetPointAtDist(originDistance);
            var offsets = new Vector3d[layout.Count];
            for (int i = 0; i < offsets.Length; i++)
            {
                double distance = originDistance + (reverse ? -1 : 1) * (i + 1) * layout.Step;
                if (curve.Closed) distance %= length;
                offsets[i] = curve.GetPointAtDist(Math.Max(0, Math.Min(length, distance))) - origin;
            }
            return offsets;
        }

        public static int Copy(Database db, ObjectId[] sourceIds, IReadOnlyList<Vector3d> offsets)
        {
            if (offsets.Count == 0) return 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = sourceIds.Distinct().Where(id => !id.IsNull && !id.IsErased).ToArray();
                foreach (var id in ids)
                {
                    var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (entity.OwnerId != db.CurrentSpaceId) throw new InvalidOperationException("Выберите объекты текущего пространства.");
                    if (((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead)).IsLocked)
                        throw new InvalidOperationException("Объект находится на заблокированном слое.");
                }
                var selected = new HashSet<ObjectId>(ids);
                var dictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var groups = new List<(string Description, bool Selectable, ObjectId[] Members)>();
                foreach (DBDictionaryEntry entry in dictionary)
                {
                    var group = (Group)tr.GetObject(entry.Value, OpenMode.ForRead);
                    var members = group.GetAllEntityIds().Where(selected.Contains).ToArray();
                    if (members.Length > 1) groups.Add((group.Description, group.Selectable, members));
                }
                int created = 0;
                foreach (var offset in offsets)
                {
                    using (var mapping = new IdMapping())
                    {
                        db.DeepCloneObjects(new ObjectIdCollection(ids), db.CurrentSpaceId, mapping, false);
                        var clones = new Dictionary<ObjectId, ObjectId>();
                        foreach (IdPair pair in mapping)
                        {
                            if (!pair.IsCloned || !selected.Contains(pair.Key)) continue;
                            var clone = (Entity)tr.GetObject(pair.Value, OpenMode.ForWrite);
                            clone.TransformBy(Matrix3d.Displacement(offset));
                            clones.Add(pair.Key, pair.Value);
                            created++;
                        }
                        foreach (var original in groups)
                        {
                            if (!dictionary.IsWriteEnabled) dictionary.UpgradeOpen();
                            var group = new Group(original.Description, original.Selectable);
                            dictionary.SetAt("MultiCopy_" + Guid.NewGuid().ToString("N"), group);
                            tr.AddNewlyCreatedDBObject(group, true);
                            group.Append(new ObjectIdCollection(original.Members.Select(id => clones[id]).ToArray()));
                            group.SetAnonymous();
                        }
                    }
                }
                tr.Commit();
                return created;
            }
        }
    }
}
