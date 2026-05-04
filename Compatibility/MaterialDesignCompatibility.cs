using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

[assembly: XmlnsDefinition(
    "http://materialdesigninxaml.net/winfx/xaml/themes",
    "MaterialDesignThemes.Wpf"
)]

namespace MaterialDesignThemes.Wpf
{
    public enum BaseTheme
    {
        Inherit,
        Light,
        Dark,
    }

    public enum PackIconKind
    {
        AlertOutline,
        AttachmentAdd,
        AutoFix,
        Binoculars,
        BookmarkAdd,
        Camera,
        Check,
        Close,
        Cogs,
        ContentCopy,
        ContentPaste,
        Create,
        CreateOutline,
        Delete,
        DeleteForever,
        DeleteRestore,
        Edit,
        EventQuestion,
        ExclamationBold,
        ExitToApp,
        FastForward,
        File,
        FileDocumentOutline,
        FolderAdd,
        FolderOpen,
        Image,
        ImageFilterBlackWhite,
        ImageOutline,
        ImageRefresh,
        ImageRemove,
        InfoBox,
        Manufacturing,
        Minus,
        MoveToInbox,
        MovieOpenPlay,
        MoviePlay,
        PaletteSwatch,
        PauseBox,
        Play,
        Plus,
        PropertyTag,
        QuestionBoxOutline,
        Reload,
        RemoveBox,
        RemoveCircle,
        Rename,
        Rewind,
        Settings,
        SettingsApplications,
        Toolbox,
        TrashCanOutline,
        ViewAgendaOutline,
        ViewGridOutline,
    }

    public sealed class BundledTheme : ResourceDictionary
    {
        public BaseTheme BaseTheme { get; set; }
        public object PrimaryColor { get; set; }
        public object SecondaryColor { get; set; }
    }

    [MarkupExtensionReturnType(typeof(PackIcon))]
    public sealed class PackIconExtension : MarkupExtension
    {
        public PackIconKind Kind { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return new PackIcon { Kind = Kind };
        }
    }

    public class PackIcon : TextBlock
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind),
            typeof(PackIconKind),
            typeof(PackIcon),
            new FrameworkPropertyMetadata(PackIconKind.File, (dependencyObject, args) =>
            {
                if (args.NewValue is PackIconKind kind && !Equals(args.NewValue, args.OldValue))
                {
                    ((PackIcon)dependencyObject).UpdateGlyph(kind);
                }
            })
        );

        public PackIcon()
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets");
            TextAlignment = TextAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            HorizontalAlignment = HorizontalAlignment.Center;
            UpdateGlyph(Kind);
        }

        public PackIconKind Kind
        {
            get => (PackIconKind)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        private void UpdateGlyph(PackIconKind kind)
        {
            Text = ResolveGlyph(kind);
        }

        private static string ResolveGlyph(PackIconKind kind)
        {
            return kind switch
            {
                PackIconKind.AlertOutline => "\uE7BA",
                PackIconKind.AttachmentAdd => "\uE723",
                PackIconKind.AutoFix => "\uE721",
                PackIconKind.Binoculars => "\uE8B6",
                PackIconKind.BookmarkAdd => "\uE734",
                PackIconKind.Camera => "\uE722",
                PackIconKind.Check => "\uE73E",
                PackIconKind.Close => "\uE711",
                PackIconKind.Cogs => "\uE713",
                PackIconKind.ContentCopy => "\uE8C8",
                PackIconKind.ContentPaste => "\uE77F",
                PackIconKind.Create => "\uE70F",
                PackIconKind.CreateOutline => "\uE70F",
                PackIconKind.Delete => "\uE74D",
                PackIconKind.DeleteForever => "\uE74D",
                PackIconKind.DeleteRestore => "\uE777",
                PackIconKind.Edit => "\uE70F",
                PackIconKind.EventQuestion => "\uE897",
                PackIconKind.ExclamationBold => "\uE783",
                PackIconKind.ExitToApp => "\uE8BB",
                PackIconKind.FastForward => "\uE893",
                PackIconKind.File => "\uE7C3",
                PackIconKind.FileDocumentOutline => "\uE8A5",
                PackIconKind.FolderAdd => "\uE8F4",
                PackIconKind.FolderOpen => "\uE838",
                PackIconKind.Image => "\uE91B",
                PackIconKind.ImageFilterBlackWhite => "\uE91B",
                PackIconKind.ImageOutline => "\uE91B",
                PackIconKind.ImageRefresh => "\uE895",
                PackIconKind.ImageRemove => "\uE711",
                PackIconKind.InfoBox => "\uE946",
                PackIconKind.Manufacturing => "\uE9F5",
                PackIconKind.Minus => "\uE738",
                PackIconKind.MoveToInbox => "\uE8B5",
                PackIconKind.MovieOpenPlay => "\uE714",
                PackIconKind.MoviePlay => "\uE714",
                PackIconKind.PaletteSwatch => "\uE790",
                PackIconKind.PauseBox => "\uE769",
                PackIconKind.Play => "\uE768",
                PackIconKind.Plus => "\uE710",
                PackIconKind.PropertyTag => "\uE8EC",
                PackIconKind.QuestionBoxOutline => "\uE897",
                PackIconKind.Reload => "\uE72C",
                PackIconKind.RemoveBox => "\uE738",
                PackIconKind.RemoveCircle => "\uE711",
                PackIconKind.Rename => "\uE8AC",
                PackIconKind.Rewind => "\uE892",
                PackIconKind.Settings => "\uE713",
                PackIconKind.SettingsApplications => "\uE713",
                PackIconKind.Toolbox => "\uE90F",
                PackIconKind.TrashCanOutline => "\uE74D",
                PackIconKind.ViewAgendaOutline => "\uE8A0",
                PackIconKind.ViewGridOutline => "\uE80A",
                _ => "\uE7C3",
            };
        }
    }

    public class ColorZone : ContentControl
    {
        public string Mode { get; set; }
    }

    public class DrawerHost : ContentControl
    {
        public static readonly DependencyProperty LeftDrawerContentProperty =
            DependencyProperty.Register(
                nameof(LeftDrawerContent),
                typeof(object),
                typeof(DrawerHost),
                new PropertyMetadata(
                    null,
                    (dependencyObject, _) => ((DrawerHost)dependencyObject).RebuildVisual()
                )
            );

        public static readonly DependencyProperty IsLeftDrawerOpenProperty =
            DependencyProperty.Register(
                nameof(IsLeftDrawerOpen),
                typeof(bool),
                typeof(DrawerHost),
                new PropertyMetadata(
                    false,
                    (dependencyObject, _) => ((DrawerHost)dependencyObject).UpdateDrawerVisibility()
                )
            );

        private bool isRebuilding;
        private object mainContent;
        private Border drawerContainer;

        public object LeftDrawerContent
        {
            get => GetValue(LeftDrawerContentProperty);
            set => SetValue(LeftDrawerContentProperty, value);
        }

        public bool IsLeftDrawerOpen
        {
            get => (bool)GetValue(IsLeftDrawerOpenProperty);
            set => SetValue(IsLeftDrawerOpenProperty, value);
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            if (!isRebuilding)
            {
                mainContent = newContent;
                RebuildVisual();
            }

            base.OnContentChanged(oldContent, newContent);
        }

        private void RebuildVisual()
        {
            if (isRebuilding)
            {
                return;
            }

            isRebuilding = true;
            try
            {
                var root = new Grid();
                root.Children.Add(new ContentPresenter { Content = mainContent });

                drawerContainer = new Border
                {
                    Background = SystemColors.ControlBrush,
                    Child = new ContentPresenter { Content = LeftDrawerContent },
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                Panel.SetZIndex(drawerContainer, 10);
                root.Children.Add(drawerContainer);

                base.Content = root;
                UpdateDrawerVisibility();
            }
            finally
            {
                isRebuilding = false;
            }
        }

        private void UpdateDrawerVisibility()
        {
            if (drawerContainer == null)
            {
                return;
            }

            drawerContainer.Visibility = IsLeftDrawerOpen ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public class PopupBox : ContentControl
    {
        public static readonly DependencyProperty ToggleContentProperty = DependencyProperty.Register(
            nameof(ToggleContent),
            typeof(object),
            typeof(PopupBox),
            new PropertyMetadata(
                null,
                (dependencyObject, _) => ((PopupBox)dependencyObject).BuildContent()
            )
        );

        public static readonly DependencyProperty PopupContentProperty = DependencyProperty.Register(
            nameof(PopupContent),
            typeof(object),
            typeof(PopupBox),
            new PropertyMetadata(
                null,
                (dependencyObject, _) => ((PopupBox)dependencyObject).BuildContent()
            )
        );

        public static readonly DependencyProperty StaysOpenProperty = DependencyProperty.Register(
            nameof(StaysOpen),
            typeof(bool),
            typeof(PopupBox),
            new PropertyMetadata(
                true,
                (dependencyObject, _) => ((PopupBox)dependencyObject).BuildContent()
            )
        );

        public static readonly DependencyProperty IsPopupOpenProperty =
            DependencyProperty.Register(
                nameof(IsPopupOpen),
                typeof(bool),
                typeof(PopupBox),
                new PropertyMetadata(false, (dependencyObject, args) =>
                {
                    var popupBox = (PopupBox)dependencyObject;
                    if (popupBox.popup != null)
                    {
                        popupBox.popup.IsOpen = (bool)args.NewValue;
                    }
                })
            );

        private bool isBuilding;
        private Popup popup;

        public object ToggleContent
        {
            get => GetValue(ToggleContentProperty);
            set => SetValue(ToggleContentProperty, value);
        }

        public object PopupContent
        {
            get => GetValue(PopupContentProperty);
            set => SetValue(PopupContentProperty, value);
        }

        public bool StaysOpen
        {
            get => (bool)GetValue(StaysOpenProperty);
            set => SetValue(StaysOpenProperty, value);
        }

        public bool IsPopupOpen
        {
            get => (bool)GetValue(IsPopupOpenProperty);
            set => SetValue(IsPopupOpenProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            BuildContent();
        }

        private void BuildContent()
        {
            if (isBuilding)
            {
                return;
            }

            isBuilding = true;
            try
            {
                var button = new Button
                {
                    Content = ToggleContent,
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                };

                popup = new Popup
                {
                    PlacementTarget = button,
                    Placement = PlacementMode.Bottom,
                    StaysOpen = StaysOpen,
                    Child = new ContentPresenter { Content = PopupContent },
                };

                popup.Opened += (_, _) => SetCurrentValue(IsPopupOpenProperty, true);
                popup.Closed += (_, _) => SetCurrentValue(IsPopupOpenProperty, false);
                button.Click += (_, _) => IsPopupOpen = !IsPopupOpen;

                var root = new Grid();
                root.Children.Add(button);
                root.Children.Add(popup);
                Content = root;
            }
            finally
            {
                isBuilding = false;
            }
        }
    }

    public static class HintAssist
    {
        public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached(
            "Hint",
            typeof(object),
            typeof(HintAssist),
            new PropertyMetadata(null)
        );

        public static readonly DependencyProperty IsFloatingProperty =
            DependencyProperty.RegisterAttached(
                "IsFloating",
                typeof(bool),
                typeof(HintAssist),
                new PropertyMetadata(false)
            );

        public static object GetHint(DependencyObject obj) => obj.GetValue(HintProperty);
        public static void SetHint(DependencyObject obj, object value) => obj.SetValue(HintProperty, value);
        public static bool GetIsFloating(DependencyObject obj) => (bool)obj.GetValue(IsFloatingProperty);
        public static void SetIsFloating(DependencyObject obj, bool value) => obj.SetValue(IsFloatingProperty, value);
    }

    public static class TextFieldAssist
    {
        public static readonly DependencyProperty DecorationVisibilityProperty =
            DependencyProperty.RegisterAttached(
                "DecorationVisibility",
                typeof(Visibility),
                typeof(TextFieldAssist),
                new PropertyMetadata(Visibility.Visible)
            );

        public static readonly DependencyProperty HasClearButtonProperty =
            DependencyProperty.RegisterAttached(
                "HasClearButton",
                typeof(bool),
                typeof(TextFieldAssist),
                new PropertyMetadata(false)
            );

        public static readonly DependencyProperty HasOutlinedTextFieldProperty =
            DependencyProperty.RegisterAttached(
                "HasOutlinedTextField",
                typeof(bool),
                typeof(TextFieldAssist),
                new PropertyMetadata(false)
            );

        public static readonly DependencyProperty TextFieldCornerRadiusProperty =
            DependencyProperty.RegisterAttached(
                "TextFieldCornerRadius",
                typeof(CornerRadius),
                typeof(TextFieldAssist),
                new PropertyMetadata(new CornerRadius(0))
            );

        public static readonly DependencyProperty UnderlineBrushProperty =
            DependencyProperty.RegisterAttached(
                "UnderlineBrush",
                typeof(Brush),
                typeof(TextFieldAssist),
                new PropertyMetadata(null)
            );

        public static Visibility GetDecorationVisibility(DependencyObject obj) =>
            (Visibility)obj.GetValue(DecorationVisibilityProperty);

        public static void SetDecorationVisibility(DependencyObject obj, Visibility value) =>
            obj.SetValue(DecorationVisibilityProperty, value);

        public static bool GetHasClearButton(DependencyObject obj) =>
            (bool)obj.GetValue(HasClearButtonProperty);

        public static void SetHasClearButton(DependencyObject obj, bool value) =>
            obj.SetValue(HasClearButtonProperty, value);

        public static bool GetHasOutlinedTextField(DependencyObject obj) =>
            (bool)obj.GetValue(HasOutlinedTextFieldProperty);

        public static void SetHasOutlinedTextField(DependencyObject obj, bool value) =>
            obj.SetValue(HasOutlinedTextFieldProperty, value);

        public static CornerRadius GetTextFieldCornerRadius(DependencyObject obj) =>
            (CornerRadius)obj.GetValue(TextFieldCornerRadiusProperty);

        public static void SetTextFieldCornerRadius(DependencyObject obj, CornerRadius value) =>
            obj.SetValue(TextFieldCornerRadiusProperty, value);

        public static Brush GetUnderlineBrush(DependencyObject obj) =>
            (Brush)obj.GetValue(UnderlineBrushProperty);

        public static void SetUnderlineBrush(DependencyObject obj, Brush value) =>
            obj.SetValue(UnderlineBrushProperty, value);
    }

    public static class GroupBoxAssist
    {
        public static readonly DependencyProperty HeaderPaddingProperty =
            DependencyProperty.RegisterAttached(
                "HeaderPadding",
                typeof(Thickness),
                typeof(GroupBoxAssist),
                new PropertyMetadata(new Thickness(0))
            );

        public static Thickness GetHeaderPadding(DependencyObject obj) =>
            (Thickness)obj.GetValue(HeaderPaddingProperty);

        public static void SetHeaderPadding(DependencyObject obj, Thickness value) =>
            obj.SetValue(HeaderPaddingProperty, value);
    }
}
