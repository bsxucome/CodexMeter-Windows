using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal static class L
    {
        public const string ProductName = "CodexMeter for Windows";
        public static string Language = "zh-CN";
        public static string T(string value) { return TextCatalog.Transform(value, Language); }
        // Used on newly built control trees. Keep user-entered TextBox content untouched.
        public static void Apply(DependencyObject root)
        {
            var window = root as Window; if (window != null) window.Title = T(window.Title);
            var text = root as TextBlock; if (text != null) text.Text = T(text.Text);
            var content = root as ContentControl;
            if (content != null && content.Content is string) content.Content = T((string)content.Content);
            var header = root as HeaderedItemsControl;
            if (header != null && header.Header is string) header.Header = T((string)header.Header);
            var element = root as FrameworkElement;
            if (element != null && element.ToolTip is string) element.ToolTip = T((string)element.ToolTip);
            string name = AutomationProperties.GetName(root);
            if (!string.IsNullOrEmpty(name)) AutomationProperties.SetName(root, T(name));
            var items = root as ItemsControl;
            if (items != null && items.ItemsSource == null)
            {
                int selected = items is System.Windows.Controls.Primitives.Selector ? ((System.Windows.Controls.Primitives.Selector)items).SelectedIndex : -1;
                for (int i = 0; i < items.Items.Count; i++)
                    if (items.Items[i] is string && T((string)items.Items[i]) != (string)items.Items[i]) items.Items[i] = T((string)items.Items[i]);
                if (selected >= 0 && items is System.Windows.Controls.Primitives.Selector) ((System.Windows.Controls.Primitives.Selector)items).SelectedIndex = selected;
            }
            var children = LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().ToArray();
            foreach (var child in children) Apply(child);
        }
        public static MessageBoxResult Confirm(Window owner, string message, string title)
        { return MessageBox.Show(owner, T(message), T(title), MessageBoxButton.YesNo, MessageBoxImage.Question); }
    }
}
