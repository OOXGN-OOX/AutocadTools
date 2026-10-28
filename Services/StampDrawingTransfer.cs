// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    internal static class StampDrawingTransfer
    {
        internal static void Write(Document document, StampProperties properties)
        {
            if (!string.IsNullOrEmpty(document.CommandInProgress))
                throw new InvalidOperationException("В чертеже выполняется команда. Завершите её и повторите перенос.");
            using (document.LockDocument()) Write(document.Database, properties);
            document.Editor.Regen();
        }

        internal static void Write(Database database, StampProperties properties)
        {
            var original = database.SummaryInfo;
            var builder = new DatabaseSummaryInfoBuilder(original);
            foreach (var key in StampSchema.Keys)
                builder.CustomPropertyTable[key] = properties.GetValue(key);
            try
            {
                using (var transaction = database.TransactionManager.StartTransaction())
                {
                    database.SummaryInfo = builder.ToDatabaseSummaryInfo();
                    StampTextLayoutService.Apply(database);
                    transaction.Commit();
                }
                database.EvaluateFields(FieldEvaluationContext.Demand);
            }
            catch { database.SummaryInfo = original; throw; }
        }
    }
}
