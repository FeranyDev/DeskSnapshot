using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskSnapshot.Services;

public static class LocalizationBindings
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(LocalizationBindings),
        new PropertyMetadata(string.Empty));

    public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

    public static void Apply(DependencyObject root)
    {
        // In system-language mode, XAML x:Uid already resolves the correct MRT
        // resources. The explicit traversal is only needed by the unpackaged
        // build when the user has selected a language override.
        if (!LocalizationService.HasExplicitLanguage)
        {
            return;
        }

        var visited = new HashSet<DependencyObject>();
        ApplyCore(root, visited);
    }

    private static void ApplyCore(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element))
        {
            return;
        }

        ApplyValue(element);

        // Walk the logical XAML tree as well as the visual tree. During a
        // window constructor, collapsed pages and control templates may not
        // have visual children yet, but their declared XAML children are
        // already available through these properties.
        if (element is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                ApplyCore(child, visited);
            }
        }

        if (element is Border border && border.Child is DependencyObject borderChild)
        {
            ApplyCore(borderChild, visited);
        }

        if (element is ContentControl contentControl && contentControl.Content is DependencyObject contentChild)
        {
            ApplyCore(contentChild, visited);
        }

        if (element is NavigationView navigationView)
        {
            if (navigationView.Content is DependencyObject navigationContent)
            {
                ApplyCore(navigationContent, visited);
            }

            foreach (var item in navigationView.MenuItems.Concat(navigationView.FooterMenuItems).OfType<DependencyObject>())
            {
                ApplyCore(item, visited);
            }
        }

        if (element is ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items.OfType<DependencyObject>())
            {
                ApplyCore(item, visited);
            }
        }

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
        {
            ApplyCore(VisualTreeHelper.GetChild(element, index), visited);
        }
    }

    private static void ApplyValue(DependencyObject element)
    {
        var key = GetKey(element);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        switch (element)
        {
            case TextBlock textBlock:
                if (LocalizationService.TryGetExplicit($"{key}.Text", out var text))
                {
                    textBlock.Text = text;
                }
                break;
            case ToggleSwitch toggleSwitch:
                if (LocalizationService.TryGetExplicit($"{key}.OnContent", out var onContent))
                {
                    toggleSwitch.OnContent = onContent;
                }

                if (LocalizationService.TryGetExplicit($"{key}.OffContent", out var offContent))
                {
                    toggleSwitch.OffContent = offContent;
                }
                break;
            case ContentControl contentControl when contentControl.Content is string:
                if (LocalizationService.TryGetExplicit($"{key}.Content", out var content))
                {
                    contentControl.Content = content;
                }
                break;
        }
    }
}
