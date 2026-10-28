// Copyright (c) 2026 OOXGN.
// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Services
{
    public static class SelectionLengthService
    {
        private static readonly HashSet<Document> Documents = new HashSet<Document>();
        private static Pane _pane;
        private static bool _started, _dirty, _paneUnavailable;
        private static DateTime _retryPaneAt, _nextRefreshUtc;
        private static string _displayedText;
        private static string _previousMacro, _ownedMacro;

        public static double Calculate(Transaction transaction, IEnumerable<ObjectId> ids)
        {
            double total = 0;
            foreach (var id in ids)
            {
                if (id.IsNull || !id.IsValid || id.IsErased) continue;
                try
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, false) is Entity entity &&
                        CurveGeometry.TryGetLength(entity, includeCircles: false, out var length)) total += length;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { /* An erased or unsupported object must not break selection. */ }
            }
            return total;
        }

        public static void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                foreach (Document doc in AcadApp.DocumentManager) Attach(doc);
                AcadApp.DocumentManager.DocumentCreated += OnCreated;
                AcadApp.DocumentManager.DocumentActivated += OnActivated;
                AcadApp.DocumentManager.DocumentToBeDestroyed += OnClosing;
                AcadApp.Idle += OnIdle;
                _dirty = true;
            }
            catch { Stop(); throw; }
        }

        private static void Attach(Document doc)
        {
            if (!Documents.Add(doc)) return;
            doc.ImpliedSelectionChanged += OnChanged;
            doc.CommandEnded += OnChanged;
            doc.CommandCancelled += OnChanged;
            doc.CommandFailed += OnChanged;
        }
        private static void Detach(Document doc)
        {
            if (!Documents.Remove(doc)) return;
            doc.ImpliedSelectionChanged -= OnChanged;
            doc.CommandEnded -= OnChanged;
            doc.CommandCancelled -= OnChanged;
            doc.CommandFailed -= OnChanged;
        }
        private static void OnCreated(object sender, DocumentCollectionEventArgs e) { Attach(e.Document); _dirty = true; }
        private static void OnActivated(object sender, DocumentCollectionEventArgs e) { _dirty = true; }
        private static void OnClosing(object sender, DocumentCollectionEventArgs e) { Detach(e.Document); _dirty = true; }
        private static void OnChanged(object sender, EventArgs e) { _dirty = true; }

        private static void OnIdle(object sender, EventArgs e)
        {
            if (DateTime.UtcNow < _nextRefreshUtc) return;
            try { Refresh(); }
            catch (System.Exception ex)
            {
                _nextRefreshUtc = DateTime.UtcNow.AddSeconds(30);
                Infrastructure.PluginLog.Error("Selection indicator", ex);
            }
        }

        private static void Refresh()
        {
            if (!_dirty && !(_paneUnavailable && DateTime.UtcNow >= _retryPaneAt)) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null && !string.IsNullOrEmpty(doc.CommandInProgress)) return;
            _dirty = false;
            _nextRefreshUtc = DateTime.UtcNow.AddMilliseconds(100);
            double total = 0;
            try
            {
                if (doc != null)
                {
                    var selection = doc.Editor.SelectImplied();
                    if (selection.Status == PromptStatus.OK)
                    {
                        using (selection.Value)
                        using (doc.LockDocument())
                        using (var transaction = doc.Database.TransactionManager.StartOpenCloseTransaction())
                            total = Calculate(transaction, selection.Value.GetObjectIds());
                    }
                }
                Display(total);
            }
            catch (System.Exception ex) { Infrastructure.PluginLog.Error("Calculate selected length", ex); }
        }

        private static void Display(double total)
        {
            var text = total > 0 ? "Сумм. длина: " + total.ToString("F2", CultureInfo.CurrentCulture) : "";
            if (_pane == null && (!_paneUnavailable || DateTime.UtcNow >= _retryPaneAt))
            {
                try
                {
                    _pane = new Pane { Text = text, MinimumWidth = 160, ToolTipText = "Длина текущего выделения в единицах чертежа: линии, полилинии, дуги и сплайны.", Visible = total > 0 };
                    // Native Model/Paper switch is the first built-in pane.
                    AcadApp.StatusBar.Panes.Insert(0, _pane);
                    _paneUnavailable = false;
                    _displayedText = null;
                    if (_previousMacro != null && Convert.ToString(AcadApp.GetSystemVariable("MODEMACRO")) == _ownedMacro)
                        AcadApp.SetSystemVariable("MODEMACRO", _previousMacro);
                    _previousMacro = _ownedMacro = null;
                }
                catch (System.Exception) { _pane = null; _paneUnavailable = true; _retryPaneAt = DateTime.UtcNow.AddSeconds(10); }
            }
            if (_pane != null)
            {
                if (_displayedText == text) return;
                _displayedText = text;
                _pane.Text = text;
                _pane.Visible = total > 0;
                AcadApp.StatusBar.Update();
            }
            else
            {
                // Same mechanism as the supplied LISP when native panes are unavailable.
                if (_previousMacro == null) _previousMacro = Convert.ToString(AcadApp.GetSystemVariable("MODEMACRO"));
                var current = Convert.ToString(AcadApp.GetSystemVariable("MODEMACRO"));
                if (_ownedMacro == null || current == _ownedMacro)
                {
                    _ownedMacro = total > 0 ? text : _previousMacro;
                    AcadApp.SetSystemVariable("MODEMACRO", _ownedMacro);
                }
            }
        }

        public static void Stop()
        {
            if (!_started) return;
            _started = false;
            AcadApp.Idle -= OnIdle;
            AcadApp.DocumentManager.DocumentCreated -= OnCreated;
            AcadApp.DocumentManager.DocumentActivated -= OnActivated;
            AcadApp.DocumentManager.DocumentToBeDestroyed -= OnClosing;
            foreach (var doc in new List<Document>(Documents)) Detach(doc);
            try
            {
                if (_pane != null) { AcadApp.StatusBar.Panes.Remove(_pane); _pane.Dispose(); AcadApp.StatusBar.Update(); }
                if (_previousMacro != null && Convert.ToString(AcadApp.GetSystemVariable("MODEMACRO")) == _ownedMacro)
                    AcadApp.SetSystemVariable("MODEMACRO", _previousMacro);
            }
            catch (System.Exception) { /* Native UI may already be disposed during shutdown. */ }
            _pane = null;
            _displayedText = null;
            _previousMacro = _ownedMacro = null;
            _paneUnavailable = false;
            _nextRefreshUtc = DateTime.MinValue;
        }
    }
}
