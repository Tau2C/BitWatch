using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;

namespace BitWatch.Behaviors
{
    public static class ListBoxBehaviors
    {
        public static readonly AttachedProperty<bool> AutoScrollProperty =
            AvaloniaProperty.RegisterAttached<ListBox, bool>(
                "AutoScroll",
                typeof(ListBoxBehaviors),
                defaultValue: false);

        public static bool GetAutoScroll(ListBox listBox)
        {
            return listBox.GetValue(AutoScrollProperty);
        }

        public static void SetAutoScroll(ListBox listBox, bool value)
        {
            listBox.SetValue(AutoScrollProperty, value);
        }

        static ListBoxBehaviors()
        {
            AutoScrollProperty.Changed.Subscribe(OnAutoScrollChanged);
        }

        private static void OnAutoScrollChanged(AvaloniaPropertyChangedEventArgs<bool> e)
        {
            if (e.Sender is ListBox listBox && e.NewValue.GetValueOrDefault())
            {
                if (listBox.Items is INotifyCollectionChanged collection)
                {
                    collection.CollectionChanged += (s, args) =>
                    {
                        if (listBox.ItemCount > 0)
                        {
                            listBox.ScrollIntoView(listBox.ItemCount - 1);
                        }
                    };
                }
            }
        }
    }
}
