using ICSharpCode.AvalonEdit;
using Snet.Utility;
using Snet.Windows.Controls.handler;
using System.Windows.Controls;
using System.Windows.Input;

namespace Snet.Iot.Daq.view
{
    /// <summary>
    /// Console.xaml 的交互逻辑，负责日志编辑器初始化和输入保护
    /// </summary>
    public partial class Console : UserControl
    {
        /// <summary>
        /// 构造函数，初始化控制台组件并配置日志编辑器高亮规则
        /// </summary>
        public Console()
        {
            InitializeComponent();
            new EditHandler(edit, App.EditModels, color: ("#414141", "#FFFFFF"));
        }

        /// <summary>
        /// 拦截文本输入，防止用户手动编辑日志内容
        /// </summary>
        private void TextEditor_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// 拦截键盘按键，阻止粘贴（Ctrl+V）、删除和退格操作
        /// </summary>
        private void TextEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) || e.Key == Key.Delete || e.Key == Key.Back)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 文本内容变化时自动滚动到末尾，保持最新日志可见
        /// </summary>
        private void TextEditor_TextChanged(object sender, EventArgs e)
        {
            TextEditor text = sender.GetSource<TextEditor>();
            text.SelectionStart = text.Text.Length;
            text.SelectionLength = 0;
            text.ScrollToEnd();
        }
    }
}
