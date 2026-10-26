using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.TextMate;

namespace CommitDock.Views
{
    public class UpdateInfoView : TextEditor
    {
        protected override Type StyleKeyOverride => typeof(TextEditor);

        public UpdateInfoView() : base(new TextArea(), new TextDocument())
        {
            IsReadOnly = true;
            ShowLineNumbers = false;
            WordWrap = true;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            TextArea.TextView.Margin = new Thickness(4, 0);
            TextArea.TextView.Options.EnableHyperlinks = false;
            TextArea.TextView.Options.EnableEmailHyperlinks = false;
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (_textMate == null)
            {
                _textMate = Models.TextMateHelper.CreateForEditor(this);
                Models.TextMateHelper.SetGrammarByFileName(_textMate, "README.md");
            }
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            if (_textMate != null)
            {
                _textMate.Dispose();
                _textMate = null;
            }

            GC.Collect();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is Models.Version ver)
                Text = ver.Body ?? string.Empty;
        }

        private TextMate.Installation _textMate = null;
    }

    public partial class SelfUpdate : ChromelessWindow
    {
        public SelfUpdate()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (DataContext is not ViewModels.SelfUpdate vm)
                return;

            // The files are being swapped right now, so the window stays until the new version takes over.
            if (vm.IsInstalling)
            {
                e.Cancel = true;
                return;
            }

            vm.Cancel();
        }

        private void CloseWindow(object _1, RoutedEventArgs _2)
        {
            Close();
        }

        private void GotoDownload(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SelfUpdate vm)
                vm.OpenReleasePage();

            e.Handled = true;
        }

        private void IgnoreThisVersion(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SelfUpdate vm)
                vm.SkipThisVersion();

            Close();
            e.Handled = true;
        }

        private async void StartUpdate(object _, RoutedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is ViewModels.SelfUpdate vm)
                await vm.InstallAsync();
        }

        private void CancelUpdate(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SelfUpdate vm)
                vm.Cancel();

            e.Handled = true;
        }

        private void RevealDownload(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SelfUpdate vm)
                vm.RevealDownloadedFile();

            e.Handled = true;
        }
    }
}
