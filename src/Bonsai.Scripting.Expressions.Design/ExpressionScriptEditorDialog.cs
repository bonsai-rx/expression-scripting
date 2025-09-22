using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Linq.Dynamic.Core.Exceptions;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Bonsai.Scripting.Expressions.Design.Properties;
using ScintillaNET;

namespace Bonsai.Scripting.Expressions.Design
{
    internal partial class ExpressionScriptEditorDialog : Form
    {
        readonly StringBuilder autoCompleteList = new();

        public ExpressionScriptEditorDialog()
        {
            InitializeComponent();
            scintilla.StyleResetDefault();
            scintilla.Styles[Style.Default].Font = "Consolas";
            scintilla.Styles[Style.Default].Size = 10;
            scintilla.StyleClearAll();

            scintilla.CaretLineBackColor = ColorTranslator.FromHtml("#feefff");
            scintilla.Styles[Style.Cpp.Default].ForeColor = Color.Black;
            scintilla.Styles[Style.Cpp.Number].ForeColor = Color.Black;
            scintilla.Styles[Style.Cpp.Character].ForeColor = ColorTranslator.FromHtml("#a31515");
            scintilla.Styles[Style.Cpp.String].ForeColor = ColorTranslator.FromHtml("#a31515");
            scintilla.Styles[Style.Cpp.StringEol].ForeColor = ColorTranslator.FromHtml("#a31515");
            scintilla.Styles[Style.Cpp.Word].ForeColor = ColorTranslator.FromHtml("#0000ff");
            scintilla.Styles[Style.Cpp.Word2].ForeColor = ColorTranslator.FromHtml("#2b91af");
            scintilla.Lexer = Lexer.Cpp;

            var types = "Object Boolean Char String SByte Byte Int16 UInt16 Int32 UInt32 Int64 UInt64 Single Double Decimal DateTime DateTimeOffset TimeSpan Guid Math Convert";
            scintilla.SetKeywords(0, "it iif new outerIt as true false null");
            scintilla.SetKeywords(1, string.Join(" ", types, types.ToLowerInvariant()));

            scintilla.AutoCSeparator = ';';
            scintilla.AutoCTypeSeparator = '?';
            scintilla.AutoCDropRestOfWord = true;
            scintilla.RegisterRgbaImage(0, Resources.FieldIcon);
            scintilla.RegisterRgbaImage(1, Resources.PropertyIcon);
            scintilla.RegisterRgbaImage(2, Resources.MethodIcon);
        }

        public Type ItType { get; set; }

        public string Script { get; set; }

        protected override void OnLoad(EventArgs e)
        {
            scintilla.Text = Script;
            scintilla.EmptyUndoBuffer();
            if (Owner != null)
            {
                Icon = Owner.Icon;
                ShowIcon = true;
            }

            base.OnLoad(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyData == Keys.Escape && !e.Handled)
            {
                Close();
                e.Handled = true;
            }

            if (e.Modifiers == Keys.Control && e.KeyCode == Keys.Enter)
            {
                okButton.PerformClick();
            }

            if (e.Modifiers == Keys.Control && e.KeyCode == Keys.Space &&
                TryGetAutoCompleteList(ItType, out string list, out int lenEntered))
            {
                scintilla.AutoCShow(lenEntered, list);
                e.SuppressKeyPress = true;
            }

            base.OnKeyDown(e);
        }

        private void scintilla_CharAdded(object sender, CharAddedEventArgs e)
        {
            autoCompleteList.Clear();
            if (e.Char == '.' && TryGetAutoCompleteList(ItType, out string list, out int lenEntered))
            {
                scintilla.AutoCShow(lenEntered, list);
            }
        }

        private void scintilla_TextChanged(object sender, EventArgs e)
        {
            Script = scintilla.Text;
        }

        private bool TryGetAutoCompleteList(Type itType, out string list, out int lenEntered)
        {
            if (itType is not null)
            {
                try
                {
                    var currentPos = scintilla.CurrentPosition;
                    var config = ParsingConfigHelper.CreateParsingConfig(itType);
                    var wordStartPos = scintilla.WordStartPosition(currentPos, true);
                    scintilla.CurrentPosition = wordStartPos;
                    lenEntered = currentPos - wordStartPos;
                    var analyzer = new CaretExpressionAnalyzer(config, scintilla.Text, wordStartPos - 1);

                    autoCompleteList.Clear();
                    var bindingFlags = BindingFlags.Public | BindingFlags.Instance;
                    if (selectedType is null)
                    {
                        AppendMember("it", -1, autoCompleteList);
                        selectedType = itType;
                    }

                    if (!selectedType.IsEnum)
                        AppendFields(selectedType, bindingFlags, autoCompleteList);
                    AppendProperties(selectedType, bindingFlags, autoCompleteList);
                    AppendMethods(selectedType, bindingFlags, autoCompleteList);
                    list = autoCompleteList.ToString();
                    return true;
                }
                catch (ParseException) { }
            }

            lenEntered = default;
            list = default;
            return false;
        }

        private void AppendFields(Type type, BindingFlags bindingFlags, StringBuilder sb)
        {
            foreach (var field in type.GetFields(bindingFlags)
                                      .OrderBy(f => f.Name))
            {
                AppendMember(field.Name, 0, sb);
            }
        }

        private void AppendProperties(Type type, BindingFlags bindingFlags, StringBuilder sb)
        {
            foreach (var property in type.GetProperties(bindingFlags)
                                         .Except(type.GetDefaultMembers())
                                         .OrderBy(p => p.Name))
            {
                AppendMember(property.Name, 1, sb);
            }
        }

        private void AppendMethods(Type type, BindingFlags bindingFlags, StringBuilder sb)
        {
            var nameSet = new HashSet<string>();
            foreach (var method in type.GetMethods(bindingFlags)
                                       .OrderBy(m => m.Name))
            {
                if (!method.IsSpecialName && nameSet.Add(method.Name))
                    AppendMember(method.Name, 2, sb);
            }
        }

        private void AppendMember(string name, int type, StringBuilder sb)
        {
            if (sb.Length > 0)
                sb.Append(scintilla.AutoCSeparator);

            sb.Append(name);
            sb.Append(scintilla.AutoCTypeSeparator);
            sb.Append(type);
        }
    }
}
