// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AutocadTools.Services
{
    public static class FormatInsertService
    {
        public const string BlockName = "WM_FORMAT";
        public static string TemplatePath => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Assets", "WM_FORMAT.dwg");

        public static ObjectId EnsureDefinition(Database target, string templatePath = null)
        {
            using (var transaction = target.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)transaction.GetObject(target.BlockTableId, OpenMode.ForRead);
                if (table.Has(BlockName))
                {
                    var definition = (BlockTableRecord)transaction.GetObject(table[BlockName], OpenMode.ForRead);
                    if (definition.IsFromExternalReference) throw new InvalidOperationException("Имя WM_FORMAT занято внешней ссылкой.");
                    return definition.ObjectId;
                }
            }
            var path = templatePath ?? TemplatePath;
            if (!File.Exists(path)) throw new FileNotFoundException("Не найден файл форматки в составе плагина.", path);
            using (var source = new Database(false, true))
            {
                source.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                source.CloseInput(true);
                ObjectId definitionId;
                using (var transaction = source.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)transaction.GetObject(source.BlockTableId, OpenMode.ForRead);
                    definitionId = table.Has(BlockName) ? table[BlockName] : ObjectId.Null;
                }
                if (!definitionId.IsNull)
                {
                    // Clone the named definition and its dependent symbols, not a wrapper around the drawing.
                    var mapping = new IdMapping();
                    source.WblockCloneObjects(new ObjectIdCollection(new[] { definitionId }), target.BlockTableId,
                        mapping, DuplicateRecordCloning.Ignore, false);
                    return mapping[definitionId].Value;
                }
                // A WBLOCK-exported file can contain the block geometry directly in ModelSpace.
                return target.Insert(BlockName, source, false);
            }
        }

        public static ObjectId Insert(Database target, ObjectId definitionId, Point3d position, double scale = 1)
        {
            if (!double.IsFinite(scale) || scale <= 0 || scale > 1000000) throw new ArgumentOutOfRangeException(nameof(scale));
            // Summary properties must exist before fields are attached/evaluated.
            StampSchema.EnsureProperties(target);
            using (var transaction = target.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)transaction.GetObject(target.LayerTableId, OpenMode.ForRead);
                ObjectId layerId;
                if (layers.Has(BlockName))
                {
                    var layer = (LayerTableRecord)transaction.GetObject(layers[BlockName], OpenMode.ForRead);
                    if (layer.IsLocked) throw new InvalidOperationException("Слой WM_FORMAT заблокирован. Разблокируйте его перед вставкой.");
                    layerId = layer.ObjectId;
                }
                else
                {
                    layers.UpgradeOpen();
                    var layer = new LayerTableRecord { Name = BlockName };
                    layerId = layers.Add(layer); transaction.AddNewlyCreatedDBObject(layer, true);
                }
                var space = (BlockTableRecord)transaction.GetObject(target.CurrentSpaceId, OpenMode.ForWrite);
                var block = new BlockReference(position, definitionId);
                block.SetDatabaseDefaults(target);
                block.LayerId = layerId;
                block.ScaleFactors = new Scale3d(scale);
                var result = space.AppendEntity(block);
                transaction.AddNewlyCreatedDBObject(block, true);
                var definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead);
                foreach (ObjectId id in definition)
                {
                    if (!(transaction.GetObject(id, OpenMode.ForRead) is AttributeDefinition attribute) || attribute.Constant) continue;
                    var reference = new AttributeReference();
                    reference.SetAttributeFromBlock(attribute, block.BlockTransform);
                    reference.TextString = attribute.TextString;
                    block.AttributeCollection.AppendAttribute(reference);
                    transaction.AddNewlyCreatedDBObject(reference, true);
                    string code = null;
                    if (attribute.HasFields)
                    {
                        var sourceField = (Field)transaction.GetObject(attribute.GetField(), OpenMode.ForRead);
                        code = sourceField.GetFieldCode(FieldCodeFlags.AddMarkers);
                        code = code.Replace("?BlockRefId", @"%<\_ObjId " + block.ObjectId.OldIdPtr.ToString() + ">%", StringComparison.Ordinal);
                    }
                    else if (attribute.Tag.StartsWith("ДОЛЖНОСТЬ", StringComparison.OrdinalIgnoreCase) && int.TryParse(attribute.Tag.Substring(9), out int role) && role >= 1 && role <= 6)
                        code = @"%<\AcVar CustomDP.Должн." + role + ">%";
                    if (code != null)
                    {
                        var field = new Field(code, true);
                        reference.SetField(field);
                        transaction.AddNewlyCreatedDBObject(field, true);
                        field.EvaluationOption = FieldEvaluationOptions.Automatic;
                        field.Evaluate((int)FieldEvaluationContext.Demand, target);
                    }
                    else if (string.IsNullOrWhiteSpace(reference.TextString) || reference.TextString == "Наименование чертежа") reference.TextString = " ";
                    if (reference.IsMTextAttribute) reference.UpdateMTextAttribute();
                }
                StampSignatureDwgService.Synchronize(target);
                StampTextLayoutService.Apply(target);
                transaction.Commit();
                target.EvaluateFields(FieldEvaluationContext.Demand);
                StampSignatureTracking.Start();
                return result;
            }
        }
    }
}
