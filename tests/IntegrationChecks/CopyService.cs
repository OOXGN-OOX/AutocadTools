using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
namespace AutocadTools.Services
{
    internal static class CopyService
    {
        internal static int Copy(Transaction transaction, BlockTableRecord destination, IEnumerable<Entity> sources, Vector3d offset)
            => MultiCopyService.Copy(destination.Database, sources.Select(e => e.ObjectId).ToArray(), new[] { offset });
    }
}
