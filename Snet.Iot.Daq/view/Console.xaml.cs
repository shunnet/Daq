using ICSharpCode.AvalonEdit;
using Snet.Utility;
using Snet.Windows.Controls.handler;
using System.Windows.Controls;
using System.Windows.Input;

namespace Snet.Iot.Daq.view
{
    /// <summary>
    /// Console.xaml 的交互逻辑
    /// </summary>
    public partial class Console : UserControl
    {
        public Console()
        {
            InitializeComponent();
            new EditHandler(edit, App.EditModels, color: ("#414141", "#FFFFFF"));

        }
        private void TextEditor_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;
        }
        private void TextEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || e.Key == Key.Delete || e.Key == Key.Back)
            {
                e.Handled = true;
            }
        }
        private void TextEditor_TextChanged(object sender, EventArgs e)
        {
            TextEditor text = sender.GetSource<TextEditor>();
            text.SelectionStart = text.Text.Length;
            text.SelectionLength = 0;
            text.ScrollToEnd();
        }
    }
}
