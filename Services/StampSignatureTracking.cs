// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Services
{
    // Dynamic blocks regenerate anonymous definitions when the sheet format changes.
    internal static class StampSignatureTracking
    {
        private const string OwnerKey = "AutocadTools.SignatureTracking.Stop";
        private static readonly HashSet<Database> Databases = new HashSet<Database>();
        private static readonly HashSet<Database> Dirty = new HashSet<Database>();
        private static readonly Dictionary<Database, string> SourceStates = new Dictionary<Database, string>();
        private static DateTime _nextCheck;
        private static bool _started;
        private static bool _updating;
        public static void Start()
        {
            if (_started) return;
            (AppDomain.CurrentDomain.GetData(OwnerKey) as Action)?.Invoke();
            AppDomain.CurrentDomain.SetData(OwnerKey, (Action)Stop);
            foreach (Document document in AcadApp.DocumentManager) Attach(document.Database);
            AcadApp.DocumentManager.DocumentCreated += OnCreated;
            AcadApp.DocumentManager.DocumentToBeDestroyed += OnClosing;
            AcadApp.Idle += OnIdle;
            _started = true;
        }
        public static void Stop()
        {
            if (!_started) return;
            AcadApp.DocumentManager.DocumentCreated -= OnCreated;
            AcadApp.DocumentManager.DocumentToBeDestroyed -= OnClosing;
            AcadApp.Idle -= OnIdle;
            foreach (var database in Databases) { database.ObjectModified -= OnChanged; database.ObjectAppended -= OnChanged; }
            Databases.Clear(); Dirty.Clear(); SourceStates.Clear(); _started = false;
        }
        private static void Attach(Database database)
        {
            if (!Databases.Add(database)) return;
            database.ObjectModified += OnChanged; database.ObjectAppended += OnChanged;
            Dirty.Add(database);
        }
        private static void OnCreated(object sender, DocumentCollectionEventArgs args) => Attach(args.Document.Database);
        private static void OnClosing(object sender, DocumentCollectionEventArgs args)
        {
            var database = args.Document.Database;
            database.ObjectModified -= OnChanged; database.ObjectAppended -= OnChanged;
            Databases.Remove(database); Dirty.Remove(database); SourceStates.Remove(database);
        }
        private static void OnChanged(object sender, ObjectEventArgs args)
        {
            if (!_updating && args.DBObject is BlockReference) Dirty.Add((Database)sender);
        }
        private static void OnIdle(object sender, EventArgs args)
        {
            var document = AcadApp.DocumentManager.MdiActiveDocument;
            if (document == null || !document.Editor.IsQuiescent || DateTime.UtcNow < _nextCheck) return;
            _nextCheck = DateTime.UtcNow.AddSeconds(1);
            var database = document.Database;
            var state = StampTextLayoutService.SourceState(database);
            bool sourceChanged = !SourceStates.TryGetValue(database, out var previous) || previous != state;
            if (!Dirty.Remove(database) && !sourceChanged) return;
            _updating = true;
            try
            {
                using (document.LockDocument())
                {
                    StampSignatureDwgService.Synchronize(document.Database);
                    StampTextLayoutService.Apply(document.Database);
                    if (sourceChanged) document.Editor.Regen();
                }
            }
            catch (Exception ex) { document.Editor.WriteMessage("\n[Autocad Tools] Не удалось обновить штамп: " + ex.Message + "\n"); }
            finally { SourceStates[database] = StampTextLayoutService.SourceState(database); _updating = false; }
        }
    }
}
