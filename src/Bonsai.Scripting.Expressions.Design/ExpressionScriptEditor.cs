using System;
using System.Drawing.Design;
using System.ComponentModel;
using System.Windows.Forms.Design;
using System.Windows.Forms;
using Bonsai.Design;

namespace Bonsai.Scripting.Expressions.Design
{
    /// <summary>
    /// Provides a user interface editor that displays a dialog box for editing
    /// the expression script.
    /// </summary>
    public class ExpressionScriptEditor : RichTextEditor
    {
        static readonly bool IsRunningOnMono = Type.GetType("Mono.Runtime") != null;

        /// <inheritdoc/>
        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            if (provider != null && !IsRunningOnMono)
            {
                var scintillaEditor = new ScintillaExpressionScriptEditor(this);
                return scintillaEditor.EditValue(context, provider, value);
            }

            return base.EditValue(context, provider, value);
        }
    }

    internal class ScintillaExpressionScriptEditor : DataSourceTypeEditor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScintillaExpressionScriptEditor"/> class.
        /// </summary>
        public ScintillaExpressionScriptEditor(UITypeEditor baseEditor)
            : base(DataSource.Input)
        {
            BaseEditor = baseEditor;
        }

        private UITypeEditor BaseEditor { get; }

        /// <inheritdoc/>
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            return UITypeEditorEditStyle.Modal;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            var workflowBuilder = (WorkflowBuilder)provider.GetService(typeof(WorkflowBuilder));
            var editorService = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
            if (editorService != null)
            {
                using var editorDialog = new ExpressionScriptEditorDialog();
                editorDialog.Script = (string)value;
                editorDialog.ItType = GetDataSource(context, provider)?.ObservableType;
                return editorService.ShowDialog(editorDialog) == DialogResult.OK
                    ? editorDialog.Script
                    : value;
            }

            return BaseEditor.EditValue(context, provider, value);
        }
    }
}
