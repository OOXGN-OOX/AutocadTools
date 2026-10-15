// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AutocadTools.Models;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Services
{
    public class AutoCADService : IAutoCADService, IStampSignatureService, IStampTypographyService
    {
        private Document _document;
        public StampProperties LoadStampProperties()
        {
            _document = AcadApp.DocumentManager.MdiActiveDocument;
            var properties = new StampProperties();
            if (_document == null) return properties;
            var builder = new DatabaseSummaryInfoBuilder(_document.Database.SummaryInfo);
            foreach (System.Collections.DictionaryEntry entry in builder.CustomPropertyTable)
                properties.SetValue(entry.Key.ToString(), entry.Value?.ToString());
            StampSignatureTracking.Start();
            return properties;
        }
        public Dictionary<string, StampSignature> LoadSignatures() => _document == null
            ? new Dictionary<string, StampSignature>() : StampSignatureDwgService.Load(_document.Database);

        public StampTypography LoadTypography() => _document == null ? new StampTypography() : StampTextLayoutService.ReadTypography(_document.Database);

        public void SaveStampProperties(StampProperties properties) => SaveStamp(properties, null);

        public void SaveStamp(StampProperties properties, Dictionary<string, StampSignature> signatures)
        {
            if (_document == null || AcadApp.DocumentManager.MdiActiveDocument != _document)
                throw new InvalidOperationException("Активный чертёж изменился. Нажмите «Обновить из чертежа» перед редактированием другого DWG.");
            using (_document.LockDocument())
            {
                var database = _document.Database;
                var original = database.SummaryInfo;
                var builder = new DatabaseSummaryInfoBuilder(original);
                foreach (var property in properties.Properties)
                    if (!string.IsNullOrWhiteSpace(property.Key)) builder.CustomPropertyTable[property.Key] = property.Value ?? " ";
                try
                {
                    using (var transaction = database.TransactionManager.StartTransaction())
                    {
                        database.SummaryInfo = builder.ToDatabaseSummaryInfo();
                        StampTextLayoutService.Apply(database);
                        if (signatures != null) StampSignatureDwgService.Save(database, signatures);
                        transaction.Commit();
                    }
                }
                catch { database.SummaryInfo = original; throw; }
                database.EvaluateFields(FieldEvaluationContext.Demand);
                _document.Editor.Regen();
                _document.Editor.WriteMessage("\n[Autocad Tools] Свойства штампа и подписи применены.\n");
            }
        }
    }
}
