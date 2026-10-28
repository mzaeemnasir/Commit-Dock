using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CommitDock.Views
{
    /// <summary>
    ///     Placeholder shown on top of a list or pane that has nothing to display: an icon, a short title and an
    ///     optional hint. It does not take pointer input, so the control underneath keeps its context menu.
    /// </summary>
    public partial class EmptyState : UserControl
    {
        public static readonly StyledProperty<Geometry> IconProperty =
            AvaloniaProperty.Register<EmptyState, Geometry>(nameof(Icon));

        public static readonly StyledProperty<string> TitleProperty =
            AvaloniaProperty.Register<EmptyState, string>(nameof(Title), string.Empty);

        public static readonly StyledProperty<string> DescriptionProperty =
            AvaloniaProperty.Register<EmptyState, string>(nameof(Description), string.Empty);

        public Geometry Icon
        {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Description
        {
            get => GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public EmptyState()
        {
            InitializeComponent();
        }
    }
}
