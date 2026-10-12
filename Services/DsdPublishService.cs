// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.PlottingServices;
using AutocadTools.Models;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Services
{
    public static class DsdPublishService
    {
        private static PublishJob _job;
        public static bool IsBusy => _job != null;

        public static bool Publish(string dsdPath, string pdfPath, string outputPath,
            IReadOnlyList<BatchPlotItem> items, bool separateSheets, bool background)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || IsBusy || !File.Exists(dsdPath)) return false;
            var job = new PublishJob(doc, pdfPath, outputPath, items, separateSheets);
            _job = job;
            try
            {
                job.Subscribe();
                var path = dsdPath.Replace("\\", "/").Replace("\"", "\\\"");
                // Background publication opens the DWG from disk; persist the generated page setups first.
                doc.SendStringToExecute($"(progn (setvar \"FILEDIA\" 0) (setvar \"BACKGROUNDPLOT\" {(background ? 3 : 0)}) (command \"_.QSAVE\") (command \"_.-PUBLISH\" \"{path}\") (princ)) ", true, false, false);
                return true;
            }
            catch { job.Dispose(); throw; }
        }

        public static void Shutdown() => _job?.Dispose();

        private sealed class PublishJob : IDisposable
        {
            private readonly Document _doc;
            private readonly string _pdfPath, _outputPath;
            private readonly IReadOnlyList<BatchPlotItem> _items;
            private readonly bool _separateSheets;
            private readonly object _oldFileDia, _oldBackgroundPlot;
            private readonly DateTime _created = DateTime.UtcNow;
            private bool _started, _finished, _failed, _disposed;

            public PublishJob(Document doc, string pdfPath, string outputPath,
                IReadOnlyList<BatchPlotItem> items, bool separateSheets)
            {
                _doc = doc; _pdfPath = pdfPath; _outputPath = outputPath;
                _items = items; _separateSheets = separateSheets;
                _oldFileDia = AcadApp.GetSystemVariable("FILEDIA");
                _oldBackgroundPlot = AcadApp.GetSystemVariable("BACKGROUNDPLOT");
            }

            public void Subscribe()
            {
                _doc.CommandWillStart += OnCommandStart;
                _doc.CommandCancelled += OnCommandFailed;
                _doc.CommandFailed += OnCommandFailed;
                AcadApp.Publisher.EndPublish += OnPublishEnd;
                AcadApp.Publisher.CancelledOrFailedPublishing += OnPublishFailed;
                AcadApp.DocumentManager.DocumentToBeDestroyed += OnDocumentClosing;
                AcadApp.Idle += OnIdle;
            }

            private static bool IsPublish(string command) =>
                string.Equals(command?.TrimStart('_', '.', '-'), "PUBLISH", StringComparison.OrdinalIgnoreCase);

            private void OnCommandStart(object sender, CommandEventArgs e)
            {
                if (IsPublish(e.GlobalCommandName)) _started = true;
            }
            private void OnCommandFailed(object sender, CommandEventArgs e)
            {
                if (IsPublish(e.GlobalCommandName)) { _started = true; _failed = true; _finished = true; }
            }
            private void OnPublishEnd(object sender, EventArgs e)
            {
                if (_started) _finished = true;
            }
            private void OnPublishFailed(object sender, EventArgs e)
            {
                if (_started) { _failed = true; _finished = true; }
            }
            private void OnDocumentClosing(object sender, DocumentCollectionEventArgs e)
            {
                if (e.Document == _doc) Dispose();
            }

            private void OnIdle(object sender, EventArgs e)
            {
                if (_disposed) return;
                if (!_finished)
                {
                    if (!_started && DateTime.UtcNow - _created > TimeSpan.FromMinutes(2))
                    {
                        _doc.Editor.WriteMessage("\n[Autocad Tools] Публикация не стартовала.\n");
                        Dispose();
                    }
                    return;
                }
                if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting) return;
                try
                {
                    if (_failed) { _doc.Editor.WriteMessage("\n[Autocad Tools] Публикация отменена или завершилась с ошибкой.\n"); return; }
                    if (!File.Exists(_pdfPath)) throw new IOException("Публикация завершилась без ожидаемого PDF.");
                    using (var stream = File.Open(_pdfPath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    if (!PdfPostProcessingService.ProcessPdf(_pdfPath, _items, _doc.Editor))
                        throw new IOException("Не удалось обработать PDF. Исходный PDF сохранён: " + _pdfPath);
                    PdfPostProcessingService.DeliverPdf(_pdfPath, _outputPath, _items, _separateSheets);
                    _doc.Editor.WriteMessage($"\n[Autocad Tools] PDF готов: {_outputPath}\n");
                }
                catch (System.Exception ex) { _doc.Editor.WriteMessage($"\n[Autocad Tools] {ex.Message}\n"); }
                finally { Dispose(); }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _doc.CommandWillStart -= OnCommandStart;
                _doc.CommandCancelled -= OnCommandFailed;
                _doc.CommandFailed -= OnCommandFailed;
                AcadApp.Publisher.EndPublish -= OnPublishEnd;
                AcadApp.Publisher.CancelledOrFailedPublishing -= OnPublishFailed;
                AcadApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentClosing;
                AcadApp.Idle -= OnIdle;
                try { AcadApp.SetSystemVariable("FILEDIA", _oldFileDia); } catch { }
                try { AcadApp.SetSystemVariable("BACKGROUNDPLOT", _oldBackgroundPlot); } catch { }
                if (ReferenceEquals(_job, this)) _job = null;
            }
        }
    }
}
