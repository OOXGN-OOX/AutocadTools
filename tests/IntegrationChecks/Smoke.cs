using System;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
public class IntegrationSmoke
{
    [CommandMethod("AUTOCADTOOLS_INTEGRATION")]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        try { DomainChecks.Run(doc); FeatureChecks.Run(doc.Editor); TableSafetyChecks.Run(doc.Editor); WorkflowChecks.Run(); FormatChecks.Run(); doc.Editor.WriteMessage("\nINTEGRATION_PASS\n"); }
        catch (System.Exception ex) { doc.Editor.WriteMessage("\nINTEGRATION_FAIL: " + ex + "\n"); }
    }
}
