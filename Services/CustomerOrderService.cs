// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public sealed class CustomerOrderService
    {
        public void Execute(Document doc, CustomerOrderOptions options)
        {
            if (doc == null) return;

            if (options.CreateNewFile)
            {
                ExecuteOnCopyFile(doc, options);
                return;
            }

            ExecuteOnCurrentDocument(doc, options);
        }

        private void ExecuteOnCurrentDocument(Document doc, CustomerOrderOptions options)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                int explodedBlocks = 0;
                int explodedPolylines = 0;
                int explodedText = 0;

                if (options.ExplodeBlocks)
                    explodedBlocks = ExplodeAllBlocksRecursive(db, tr, ed);

                if (options.ExplodePolylines)
                    explodedPolylines = ExplodeAllPolylinesInLayouts(db, tr, ed);

                if (options.ExplodeText)
                    explodedText = ExplodeAllTextInLayouts(db, tr, ed);

                if (options.PurgeUnused)
                    PurgeAll(db, tr, ed);

                if (options.ExplodeBlocks)
                    ReportRemainingBlockRefs(db, tr, ed);

                tr.Commit();

                ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Готово. Blocks exploded: {explodedBlocks}, Polylines exploded: {explodedPolylines}, Text exploded: {explodedText}\n");
            }

            if (options.BlockCopy)
            {
                CopyProtectionService.Enable(doc);
                doc.Editor.WriteMessage("\n[Autocad Tools CustomerOrder] Защита от копирования включена для текущего документа.\n");
            }
        }

        private void ExecuteOnCopyFile(Document originalDoc, CustomerOrderOptions options)
        {
            var ed = originalDoc.Editor;
            var originalDb = originalDoc.Database;

            var newPath = BuildDatedCopyPath(originalDoc);

            using (originalDoc.LockDocument())
            {
                ed.WriteMessage($"\n[Autocad Tools CustomerOrder] SaveCopy -> {newPath}\n");
                originalDb.SaveAs(newPath, false, DwgVersion.Current, originalDb.SecurityParameters);
            }

            using (var workDb = new Database(false, true))
            {
                workDb.ReadDwgFile(newPath, FileShare.ReadWrite, true, "");
                workDb.CloseInput(true);

                int explodedBlocks = 0;
                int explodedPolylines = 0;
                int explodedText = 0;

                using (var tr = workDb.TransactionManager.StartTransaction())
                {
                    if (options.ExplodeBlocks)
                        explodedBlocks = ExplodeAllBlocksRecursive(workDb, tr, ed);

                    if (options.ExplodePolylines)
                        explodedPolylines = ExplodeAllPolylinesInLayouts(workDb, tr, ed);

                    if (options.ExplodeText)
                        explodedText = ExplodeAllTextInLayouts(workDb, tr, ed);

                    if (options.PurgeUnused)
                        PurgeAll(workDb, tr, ed);

                    if (options.ExplodeBlocks)
                        ReportRemainingBlockRefs(workDb, tr, ed);

                    tr.Commit();
                }

                workDb.SaveAs(newPath, DwgVersion.Current);

                ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Готово (в копии). Blocks exploded: {explodedBlocks}, Polylines exploded: {explodedPolylines}, Text exploded: {explodedText}\n");
            }

            if (options.BlockCopy)
            {
                ed.WriteMessage("\n[Autocad Tools CustomerOrder] ВНИМАНИЕ: 'Запретить копирование' работает только в открытом документе текущей сессии, а не как защита файла.\n");
            }
        }

        private static string BuildDatedCopyPath(Document doc)
        {
            var src = doc.Name ?? string.Empty;

            if (!Path.IsPathRooted(src) || !File.Exists(src))
                throw new InvalidOperationException("Файл не сохранён на диск. Сначала сохрани DWG, затем запускай 'Сформировать заказчику'.");

            var dir = Path.GetDirectoryName(src) ?? "";
            var name = Path.GetFileNameWithoutExtension(src);
            var dated = $"{name}_{DateTime.Now:yy.MM.dd}.dwg";

            return Path.Combine(dir, dated);
        }

        // ==========================================================================
        // РАСЧЛЕНЕНИЕ ТЕКСТА (MTEXT -> DBText с сохранением позиционирования)
        // ==========================================================================
        private static int ExplodeAllTextInLayouts(Database db, Transaction tr, Editor ed)
        {
            int count = 0;

            foreach (var btrId in GetLayoutBtrs_NoDuplicates(db, tr))
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                var idsSnapshot = new List<ObjectId>();
                foreach (ObjectId id in btr) idsSnapshot.Add(id);

                foreach (var id in idsSnapshot)
                {
                    if (!id.IsValid || id.IsErased) continue;

                    if (tr.GetObject(id, OpenMode.ForRead, false) is MText mtext)
                    {
                        if (ConvertMTextToDBText(mtext, btr, tr, ed))
                            count++;
                    }
                }
            }

            ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Text explode (MTEXT->DBText) обработано: {count}\n");
            return count;
        }

        /// <summary>
        /// Конвертирует MTEXT в один DBText, чтобы не ловить смещения от Explode().
        /// Ключевое: всегда выставляем Position + AlignmentPoint и вызываем AdjustAlignment после добавления в БД. [web:100][web:103]
        /// </summary>
        private static bool ConvertMTextToDBText(MText mtext, BlockTableRecord owner, Transaction tr, Editor ed)
        {
            try
            {
                var db = mtext.Database;

                // Опорная точка MTEXT
                Point3d loc = mtext.Location;

                var txt = new DBText();
                txt.SetDatabaseDefaults(db);

                // Содержимое: DBText не понимает mtext.Contents с форматированием, поэтому берём "Text".
                // Если нужно именно с кодами полей/формата — это другой режим (тогда лучше ExplodeFragments). [web:103]
                txt.TextString = mtext.Text;

                txt.LayerId = mtext.LayerId;
                txt.Color = mtext.Color;
                txt.LinetypeId = mtext.LinetypeId;
                txt.LinetypeScale = mtext.LinetypeScale;
                txt.LineWeight = mtext.LineWeight;
                txt.TextStyleId = mtext.TextStyleId;

                txt.Height = mtext.TextHeight;
                txt.Rotation = mtext.Rotation;

                // Важно для не-XY плоскостей
                txt.Normal = mtext.Normal;

                // ВАЖНО: чтобы не улетало в (0,0,0), задаём ОБА поля.
                txt.Position = loc;
                txt.AlignmentPoint = loc;

                ConvertMTextAttachmentToDBTextAlignment(mtext, txt);

                owner.AppendEntity(txt);
                tr.AddNewlyCreatedDBObject(txt, true);

                // После добавления в БД корректно пересчитывается выравнивание. [web:100]
                txt.AdjustAlignment(db);

                mtext.UpgradeOpen();
                mtext.Erase();

                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Autocad Tools CustomerOrder] ConvertMTextToDBText failed (Handle={mtext.Handle}). Причина: {ex.Message}\n");
                return false;
            }
        }

        private static void ConvertMTextAttachmentToDBTextAlignment(MText mtext, DBText txt)
        {
            // MTEXT.Attachment влияет на то, как Location интерпретируется. [web:128]
            // Здесь сопоставляем attachment к HorizontalMode/VerticalMode и используем AlignmentPoint=Location. [web:120][web:128]
            var loc = mtext.Location;

            switch (mtext.Attachment)
            {
                case AttachmentPoint.TopLeft:
                    txt.HorizontalMode = TextHorizontalMode.TextLeft;
                    txt.VerticalMode = TextVerticalMode.TextTop;
                    break;
                case AttachmentPoint.TopCenter:
                    txt.HorizontalMode = TextHorizontalMode.TextCenter;
                    txt.VerticalMode = TextVerticalMode.TextTop;
                    break;
                case AttachmentPoint.TopRight:
                    txt.HorizontalMode = TextHorizontalMode.TextRight;
                    txt.VerticalMode = TextVerticalMode.TextTop;
                    break;

                case AttachmentPoint.MiddleLeft:
                    txt.HorizontalMode = TextHorizontalMode.TextLeft;
                    txt.VerticalMode = TextVerticalMode.TextVerticalMid;
                    break;
                case AttachmentPoint.MiddleCenter:
                    txt.HorizontalMode = TextHorizontalMode.TextCenter;
                    txt.VerticalMode = TextVerticalMode.TextVerticalMid;
                    break;
                case AttachmentPoint.MiddleRight:
                    txt.HorizontalMode = TextHorizontalMode.TextRight;
                    txt.VerticalMode = TextVerticalMode.TextVerticalMid;
                    break;

                case AttachmentPoint.BottomLeft:
                    txt.HorizontalMode = TextHorizontalMode.TextLeft;
                    txt.VerticalMode = TextVerticalMode.TextBottom;
                    break;
                case AttachmentPoint.BottomCenter:
                    txt.HorizontalMode = TextHorizontalMode.TextCenter;
                    txt.VerticalMode = TextVerticalMode.TextBottom;
                    break;
                case AttachmentPoint.BottomRight:
                    txt.HorizontalMode = TextHorizontalMode.TextRight;
                    txt.VerticalMode = TextVerticalMode.TextBottom;
                    break;

                default:
                    // Fallback
                    txt.HorizontalMode = TextHorizontalMode.TextLeft;
                    txt.VerticalMode = TextVerticalMode.TextBase;
                    break;
            }

            // Чтобы AutoCAD не пытался использовать (0,0,0) — закрепляем оба.
            txt.Position = loc;
            txt.AlignmentPoint = loc;
        }

        // ==========================================================================
        // РЕКУРСИВНОЕ РАСЧЛЕНЕНИЕ БЛОКОВ + BURST атрибутов + УДАЛЕНИЕ ATTDEF
        // ==========================================================================
        private static int ExplodeAllBlocksRecursive(Database db, Transaction tr, Editor ed)
        {
            int totalExploded = 0;

            foreach (var btrId in GetLayoutBtrs_NoDuplicates(db, tr))
            {
                int pass = 0;
                const int passMax = 100;

                while (pass < passMax)
                {
                    pass++;

                    var blockRefs = CollectAllBlockRefsInSpace(btrId, tr);
                    if (blockRefs.Count == 0)
                    {
                        ed.WriteMessage($"\n[Autocad Tools CustomerOrder] BTR={btrId.Handle}, pass={pass}: нет блоков для взрыва.\n");
                        break;
                    }

                    ed.WriteMessage($"\n[Autocad Tools CustomerOrder] BTR={btrId.Handle}, pass={pass}: найдено BlockReference={blockRefs.Count}\n");

                    int explodedInPass = 0;

                    foreach (var brId in blockRefs)
                    {
                        if (!brId.IsValid || brId.IsErased) continue;

                        try
                        {
                            var br = tr.GetObject(brId, OpenMode.ForWrite, false) as BlockReference;
                            if (br == null || br.IsErased) continue;

                            ForceEnableExplodable(br.BlockTableRecord, tr, ed);
                            explodedInPass += ExplodeBlockReferenceRecursiveCount(db, tr, br, ed);
                        }
                        catch (System.Exception ex)
                        {
                            ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Не удалось взорвать BlockRef={brId.Handle}. Причина: {ex.Message}\n");
                        }
                    }

                    totalExploded += explodedInPass;
                    ed.WriteMessage($"\n[Autocad Tools CustomerOrder] BTR={btrId.Handle}, pass={pass}: взорвано={explodedInPass}\n");

                    if (explodedInPass == 0)
                        break;
                }

                if (pass >= passMax)
                    ed.WriteMessage($"\n[Autocad Tools CustomerOrder] ВНИМАНИЕ: достигнут лимит проходов по взрыву блоков для BTR={btrId.Handle}. Возможна рекурсия/зацикливание.\n");
            }

            return totalExploded;
        }

        private static List<ObjectId> CollectAllBlockRefsInSpace(ObjectId btrId, Transaction tr)
        {
            var list = new List<ObjectId>();
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForRead, false) is BlockReference)
                    list.Add(id);
            }

            return list;
        }

        private static void ForceEnableExplodable(ObjectId btrId, Transaction tr, Editor ed)
        {
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

                if (btr.IsFromExternalReference || btr.IsDependent)
                    return;

                if (!btr.Explodable)
                {
                    btr.UpgradeOpen();
                    btr.Explodable = true;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Не удалось снять запрет Explodable у определения блока (BTR={btrId.Handle}). Причина: {ex.Message}\n");
            }
        }

        private static int ExplodeBlockReferenceRecursiveCount(Database db, Transaction tr, BlockReference br, Editor ed)
        {
            int explodedCount = 0;
            var appended = new List<ObjectId>();

            ObjectEventHandler handler = (s, e) =>
            {
                try
                {
                    if (e?.DBObject != null && !e.DBObject.IsErased)
                        appended.Add(e.DBObject.ObjectId);
                }
                catch { }
            };

            try
            {
                // BURST: атрибуты -> текст со значениями (и удаляем сами AttributeReference). [web:69]
                BurstAttributes(br, tr, ed);

                db.ObjectAppended += handler;

                br.ExplodeToOwnerSpace();
                explodedCount++;

                db.ObjectAppended -= handler;

                if (!br.IsErased)
                    br.Erase();

                // Удаляем теги ATTDEF, появившиеся после взрыва. [web:118]
                int removedAttDef = RemoveAttributeDefinitionsFromAppended(appended, tr);

                if (removedAttDef > 0)
                    ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Удалено AttributeDefinition (тегов): {removedAttDef}\n");

                // Рекурсивно взрываем вложенные блоки
                foreach (var id in appended)
                {
                    if (!id.IsValid || id.IsErased) continue;

                    if (tr.GetObject(id, OpenMode.ForRead, false) is BlockReference nestedBr)
                    {
                        ForceEnableExplodable(nestedBr.BlockTableRecord, tr, ed);
                        nestedBr.UpgradeOpen();
                        explodedCount += ExplodeBlockReferenceRecursiveCount(db, tr, nestedBr, ed);
                    }
                }

                return explodedCount;
            }
            catch (System.Exception ex)
            {
                try { db.ObjectAppended -= handler; } catch { }
                ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Explode failed for Handle={br.Handle}. Причина: {ex.Message}\n");
                return 0;
            }
        }

        private static int RemoveAttributeDefinitionsFromAppended(List<ObjectId> appendedIds, Transaction tr)
        {
            int removedCount = 0;

            foreach (var id in appendedIds)
            {
                if (!id.IsValid || id.IsErased) continue;

                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead, false) is AttributeDefinition attDef)
                    {
                        attDef.UpgradeOpen();
                        attDef.Erase();
                        removedCount++;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return removedCount;
        }

        private static void BurstAttributes(BlockReference br, Transaction tr, Editor ed)
        {
            if (br.AttributeCollection == null || br.AttributeCollection.Count == 0)
                return;

            BlockTableRecord owner;
            try
            {
                owner = (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForWrite);
            }
            catch
            {
                return;
            }

            var attrIds = new List<ObjectId>();
            foreach (ObjectId id in br.AttributeCollection)
                if (id.IsValid && !id.IsErased) attrIds.Add(id);

            foreach (var attrId in attrIds)
            {
                try
                {
                    var ar = tr.GetObject(attrId, OpenMode.ForWrite, false) as AttributeReference;
                    if (ar == null || ar.IsErased) continue;
                    if (ar.Invisible) { ar.Erase(); continue; } // скрытые удаляем, чтобы не остались как "мусор" [web:69]

                    var txt = new DBText();
                    txt.SetDatabaseDefaults(br.Database);

                    txt.TextString = ar.TextString;

                    txt.LayerId = ar.LayerId;
                    txt.Color = ar.Color;
                    txt.LinetypeId = ar.LinetypeId;
                    txt.LinetypeScale = ar.LinetypeScale;
                    txt.LineWeight = ar.LineWeight;

                    txt.TextStyleId = ar.TextStyleId;
                    txt.Height = ar.Height;
                    txt.Rotation = ar.Rotation;
                    txt.Oblique = ar.Oblique;
                    txt.WidthFactor = ar.WidthFactor;

                    // Важно: Normal + обе точки, иначе возможны смещения/0,0,0 при выравнивании
                    txt.Normal = ar.Normal;

                    txt.Position = ar.Position;
                    txt.AlignmentPoint = ar.AlignmentPoint;

                    txt.HorizontalMode = ar.HorizontalMode;
                    txt.VerticalMode = ar.VerticalMode;

                    owner.AppendEntity(txt);
                    tr.AddNewlyCreatedDBObject(txt, true);

                    txt.AdjustAlignment(br.Database);

                    // Требование: "удаляй сами теги" — удаляем AttributeReference после переноса значения
                    ar.Erase();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[Autocad Tools CustomerOrder] BurstAttributes: ошибка атрибута блока Handle={br.Handle}. Причина: {ex.Message}\n");
                }
            }
        }

        // ==========================================================================
        // РАСЧЛЕНЕНИЕ ПОЛИЛИНИЙ
        // ==========================================================================
        private static int ExplodeAllPolylinesInLayouts(Database db, Transaction tr, Editor ed)
        {
            int count = 0;

            foreach (var btrId in GetLayoutBtrs_NoDuplicates(db, tr))
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                var idsSnapshot = new List<ObjectId>();
                foreach (ObjectId id in btr) idsSnapshot.Add(id);

                foreach (var id in idsSnapshot)
                {
                    if (!id.IsValid || id.IsErased) continue;

                    if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent)
                        continue;

                    if (ent is Polyline || ent is Polyline2d || ent is Polyline3d)
                    {
                        if (TryExplodeEntityToOwnerSpace(ent, tr, ed))
                            count++;
                    }
                }
            }

            return count;
        }

        private static bool TryExplodeEntityToOwnerSpace(Entity ent, Transaction tr, Editor ed)
        {
            try
            {
                var owner = (BlockTableRecord)tr.GetObject(ent.OwnerId, OpenMode.ForWrite);

                var col = new DBObjectCollection();
                ent.Explode(col);

                if (col.Count == 0) return false;

                foreach (DBObject dbo in col)
                {
                    if (dbo is Entity e)
                    {
                        owner.AppendEntity(e);
                        tr.AddNewlyCreatedDBObject(e, true);
                    }
                    else
                    {
                        dbo.Dispose();
                    }
                }

                ent.UpgradeOpen();
                ent.Erase();
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Explode entity failed (Handle={ent.Handle}). Причина: {ex.Message}\n");
                return false;
            }
        }

        // ==========================================================================
        // ОЧИСТКА (PURGE)
        // ==========================================================================
        private static void PurgeAll(Database db, Transaction tr, Editor ed)
        {
            int pass = 0;
            int erasedTotal = 0;

            while (true)
            {
                pass++;
                var ids = CollectPurgableCandidateIds(db, tr);
                int before = ids.Count;

                db.Purge(ids);
                if (ids.Count == 0)
                {
                    ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Purge: pass={pass}, candidate={before}, purged=0\n");
                    break;
                }

                int erased = 0;
                foreach (ObjectId id in ids)
                {
                    try
                    {
                        var dbo = tr.GetObject(id, OpenMode.ForWrite, false);
                        if (dbo != null && !dbo.IsErased)
                        {
                            dbo.Erase();
                            erased++;
                        }
                    }
                    catch { }
                }

                erasedTotal += erased;
                ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Purge: pass={pass}, candidate={before}, purged={ids.Count}, erased={erased}\n");

                if (erased == 0) break;
                if (pass > 10) break;
            }

            ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Purge total erased: {erasedTotal}\n");
        }

        private static ObjectIdCollection CollectPurgableCandidateIds(Database db, Transaction tr)
        {
            var ids = new ObjectIdCollection();

            void AddFromSymbolTable(ObjectId tableId)
            {
                var table = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);
                foreach (ObjectId id in table)
                    ids.Add(id);
            }

            AddFromSymbolTable(db.LayerTableId);
            AddFromSymbolTable(db.LinetypeTableId);
            AddFromSymbolTable(db.TextStyleTableId);
            AddFromSymbolTable(db.DimStyleTableId);
            AddFromSymbolTable(db.RegAppTableId);
            AddFromSymbolTable(db.BlockTableId);

            return ids;
        }

        private static void ReportRemainingBlockRefs(Database db, Transaction tr, Editor ed)
        {
            int total = 0;
            foreach (var btrId in GetLayoutBtrs_NoDuplicates(db, tr))
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead, false) is BlockReference)
                        total++;
                }
            }
            ed.WriteMessage($"\n[Autocad Tools CustomerOrder] Осталось BlockReference в пространствах: {total}\n");
        }

        private static IEnumerable<ObjectId> GetLayoutBtrs_NoDuplicates(Database db, Transaction tr)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            var msId = bt[BlockTableRecord.ModelSpace];
            yield return msId;

            foreach (ObjectId btrId in bt)
            {
                if (btrId == msId) continue;

                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsLayout && !btr.IsFromExternalReference && !btr.IsDependent)
                    yield return btrId;
            }
        }
    }
}
