﻿namespace mpTxtCopyPaste
{
    using System;
    using System.Globalization;
    using Autodesk.AutoCAD.ApplicationServices;
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.EditorInput;
    using Autodesk.AutoCAD.Geometry;
    using Autodesk.AutoCAD.Runtime;
    using ModPlusAPI;
    using ModPlusAPI.Windows;
    using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

    /// <summary>
    /// Main class of plugin
    /// </summary>
    public class MpTxtCopyPaste
    {
        private const string LangItem = "mpTxtCopyPaste";

        /// <summary>
        /// Command start
        /// </summary>
        [CommandMethod("ModPlus", "mpTxtCopyPaste", CommandFlags.UsePickSet)]
        public static void MainFunction()
        {
            Statistic.SendCommandStarting(new ModPlusConnector());

            try
            {
                var deleteSource = false; // Удаление исходника

                // Значение по-умолчанию из расширенных данных чертеже
                var defVal = ModPlus.Helpers.XDataHelpers.GetStringXData("mpTxtCopyPaste");
                if (!string.IsNullOrEmpty(defVal))
                {
                    if (bool.TryParse(defVal, out var tmp))
                        deleteSource = tmp;
                }

                var doc = AcApp.DocumentManager.MdiActiveDocument;
                var db = doc.Database;
                var ed = doc.Editor;

                var per = PromptSourceEntity(doc, ref deleteSource);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var str = GetString(per, deleteSource, ed);

                    if (!string.IsNullOrEmpty(str))
                    {
                        while (true)
                        {
                            per = ed.GetEntity(GetDestinationEntityOptions());
                            if (per.Status == PromptStatus.Cancel)
                                break;
                            if (per.Status != PromptStatus.OK)
                                continue;

                            var destinationEntity = tr.GetObject(per.ObjectId, OpenMode.ForWrite) as Entity;
                            SetString(destinationEntity, str, ed);
                            db.TransactionManager.QueueForGraphicsFlush();
                        }
                    }

                    tr.Commit();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (System.Exception exception)
            {
                ExceptionBox.Show(exception);
            }
        }

        private static PromptEntityOptions GetDestinationEntityOptions()
        {
            var peo = new PromptEntityOptions("\n" + Language.GetItem(LangItem, "msg5"));
            peo.SetRejectMessage("\n" + Language.GetItem(LangItem, "msg3"));
            peo.AddAllowedClass(typeof(DBText), false);
            peo.AddAllowedClass(typeof(MText), false);
            peo.AddAllowedClass(typeof(MLeader), false);
            peo.AddAllowedClass(typeof(Table), false);
            peo.AllowNone = false;
            return peo;
        }

        private static PromptEntityResult PromptSourceEntity(Document doc, ref bool deleteSource)
        {
            var peo = new PromptEntityOptions("\n" + Language.GetItem(LangItem, "msg1"));
            peo.SetMessageAndKeywords("\n" + Language.GetItem(LangItem, "msg2"), "Delete");
            peo.AppendKeywordsToMessage = true;
            peo.SetRejectMessage("\n" + Language.GetItem(LangItem, "msg3"));
            peo.AddAllowedClass(typeof(DBText), false);
            peo.AddAllowedClass(typeof(MText), false);
            peo.AddAllowedClass(typeof(MLeader), false);
            peo.AddAllowedClass(typeof(Table), false);
            peo.AddAllowedClass(typeof(Dimension), false);
            peo.AllowNone = true;
            var per = doc.Editor.GetEntity(peo);

            if (per.Status == PromptStatus.Keyword)
            {
                deleteSource = MessageBox.ShowYesNo(Language.GetItem(LangItem, "msg4"), MessageBoxIcon.Question);

                // Сохраняем текущее значение как значение по умолчанию
                ModPlus.Helpers.XDataHelpers.SetStringXData("mpTxtCopyPaste", deleteSource.ToString());
                return PromptSourceEntity(doc, ref deleteSource);
            }

            if (per.Status == PromptStatus.OK)
            {
                return per;
            }

            throw new OperationCanceledException();
        }

        private static string GetString(PromptEntityResult prompt, bool deleteSource, Editor ed)
        {
            var mode = deleteSource ? OpenMode.ForWrite : OpenMode.ForRead;
            var ent = prompt.ObjectId.GetObject(mode);

            // Delete source
            if (deleteSource && !(ent is Table))
                ent.Erase(true);

            switch (ent)
            {
                case DBText dbText:
                    return dbText.TextString;
                case MText mText:
                    return mText.Contents;
                case MLeader mLeader:
                    return mLeader.MText.Contents;
                case Dimension dimension:
                    return !string.IsNullOrEmpty(dimension.DimensionText)
                        ? dimension.DimensionText
                        : Math.Round(dimension.Measurement, dimension.Dimdec)
                            .ToString(CultureInfo.InvariantCulture)
                            .Replace(".", dimension.Dimdsep.ToString());
                case Table table:
                    var cell = GetCell(table, ed);
                    if (cell != null)
                    {
                        var str = cell.TextString;
                        if (deleteSource)
                            cell.TextString = string.Empty;
                        return str;
                    }

                    break;
            }

            return null;
        }

        private static void SetString(Entity ent, string str, Editor ed)
        {
            switch (ent)
            {
                case DBText dbText:
                    dbText.TextString = str;
                    break;
                case MText mText:
                    mText.Contents = str;
                    break;
                case MLeader mLeader:
                    var text = mLeader.MText;
                    if (text != null)
                    {
                        text.Contents = str;
                        mLeader.MText = text;
                    }

                    break;
                case Table table:
                    var cell = GetCell(table, ed);
                    if (cell != null)
                        cell.TextString = str;
                    table.RecomputeTableBlock(true);
                    break;
            }
        }

        private static Cell GetCell(Table table, Editor ed)
        {
            var r = ed.GetPoint($"\nPick cell");
            if (r.Status == PromptStatus.OK)
            {
                var hit = table.HitTest(r.Value, Vector3d.ZAxis);
                if (hit.Type == TableHitTestType.Cell)
                    return table.Cells[hit.Row, hit.Column];
            }

            return null;
        }
    }
}