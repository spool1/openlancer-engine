// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using LibreLancer.ContentEdit;
using LibreLancer.Dialogs;
using LibreLancer.ImUI;

namespace LancerEdit.ModelEditor
{
    internal class ModelSaveStrategy : ISaveStrategy
    {
        private readonly MainWindow window;
        private readonly ModelEditorTab modelTab;

        public ModelSaveStrategy(MainWindow window, ModelEditorTab modelTab)
        {
            this.window = window;
            this.modelTab = modelTab;
        }

        public bool ShouldSave => modelTab.Dirty;

        public void DrawMenuOptions()
        {
            if (Theme.IconMenuItem(Icons.Save, string.Format("Save '{0}'", modelTab.DocumentName), true))
            {
                Save(false);
            }
            if (Theme.IconMenuItem(Icons.Save, "Save As", true))
            {
                Save(true);
            }
        }

        public void Save()
        {
            Save(false);
        }

        EditResult<UtfStatistics> WriteTo(string path)
        {
            modelTab.ApplyToUtf();
            return modelTab.Utf.Save(path);
        }


        internal void Save(bool forceSaveAs)
        {
            if (!forceSaveAs && !string.IsNullOrEmpty(modelTab.FilePath))
            {
                var res = WriteTo(modelTab.FilePath);
                window.ResultMessages(res);
                if(res.IsSuccess)
                    window.OnSaved();
            }
            else
                RunSaveDialog();
        }

        internal void RunSaveDialog()
        {
            FileDialog.Save(f =>
            {
                var result = WriteTo(f);
                window.ResultMessages(result);
                if (result.IsSuccess)
                {
                    window.OnSaved();
                    modelTab.DocumentName = System.IO.Path.GetFileName(f);
                    modelTab.UpdateTitle();
                    modelTab.FilePath = f;
                }
            }, AppFilters.AleFilters);
        }
    }
}
