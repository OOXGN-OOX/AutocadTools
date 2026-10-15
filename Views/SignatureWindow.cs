// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutocadTools.Models;
using AutocadTools.Services;
using Microsoft.Win32;

namespace AutocadTools.Views
{
    public sealed class SignatureWindow : Window
    {
        private readonly InkCanvas _ink;
        private readonly Image _imported;
        private readonly TextBox _name;
        private readonly ComboBox _library;
        private readonly ComboBox _slot;
        private StampSignature _source;
        public StampSignature Result { get; private set; }
        public string SlotKey => ((StampSignatureSlot)_slot.SelectedItem).Key;

        public SignatureWindow(StampSignatureSlot[] slots, string selectedKey)
        {
            Title = "Подпись"; Width = 760; Height = 470; MinWidth = 650; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/" + typeof(SignatureWindow).Assembly.GetName().Name + ";component/Views/Styles.xaml", UriKind.Relative) });
            SetResourceReference(StyleProperty, "ToolWindow"); UiTheme.Apply(this);
            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            _slot = new ComboBox { ItemsSource = slots, DisplayMemberPath = "Label", SelectedItem = slots.First(s => s.Key == selectedKey), MinWidth = 175 };
            _library = new ComboBox { DisplayMemberPath = "Name", MinWidth = 240, Margin = new Thickness(8, 0, 0, 0) };
            var top = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };
            top.Children.Add(_slot); top.Children.Add(_library); root.Children.Add(top);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            tools.Children.Add(Button("Загрузить PNG…", Import));
            tools.Children.Add(Button("Рисовать", () => _ink.EditingMode = InkCanvasEditingMode.Ink));
            tools.Children.Add(Button("Ластик", () => _ink.EditingMode = InkCanvasEditingMode.EraseByStroke));
            tools.Children.Add(Button("Отменить штрих", () => { if (_ink.Strokes.Count > 0) _ink.Strokes.RemoveAt(_ink.Strokes.Count - 1); }));
            tools.Children.Add(Button("Очистить", () => { _ink.Strokes.Clear(); _imported.Source = null; _source = null; }));
            Grid.SetRow(tools, 1); root.Children.Add(tools);
            var paper = new Grid { Background = Brushes.White, ClipToBounds = true };
            _imported = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(24), IsHitTestVisible = false };
            paper.Children.Add(_imported);
            _ink = new InkCanvas { Background = Brushes.Transparent,
                DefaultDrawingAttributes = new DrawingAttributes { Color = SignatureLibrary.InkColor, Width = 2, Height = 2, FitToCurve = true } };
            paper.Children.Add(_ink); Grid.SetRow(paper, 2); root.Children.Add(paper);
            _name = new TextBox { Margin = new Thickness(0, 10, 0, 10), Text = "Новая подпись" };
            _name.SetResourceReference(StyleProperty, "ToolTextBox");
            System.Windows.Automation.AutomationProperties.SetName(_name, "Название подписи");
            Grid.SetRow(_name, 3); root.Children.Add(_name);
            var actions = new DockPanel();
            var remove = Button("Убрать из ячейки", () => { Result = null; DialogResult = true; }); actions.Children.Add(remove);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = Button("Отмена", () => DialogResult = false); cancel.IsCancel = true; right.Children.Add(cancel);
            var save = Button("Сохранить и выбрать", Save); save.SetResourceReference(StyleProperty, "PrimaryButton"); right.Children.Add(save);
            actions.Children.Add(right); Grid.SetRow(actions, 4); root.Children.Add(actions);
            try { _library.ItemsSource = SignatureLibrary.Load(); }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось прочитать библиотеку подписей: " + ex.Message, Title); }
            _library.SelectionChanged += (_, _) => { if (_library.SelectedItem is StampSignature signature) Select(signature); };
            _slot.SelectionChanged += (_, _) => { if (_slot.SelectedItem is StampSignatureSlot slot) Select(slot.Signature); };
            Select(slots.First(s => s.Key == selectedKey).Signature);
        }

        private static Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(9, 6, 9, 6) };
            button.Click += (_, _) => action(); return button;
        }
        private void Select(StampSignature signature)
        {
            _source = signature; _ink.Strokes.Clear(); _imported.Source = null;
            _name.Text = signature?.Name ?? "Новая подпись";
            if (signature?.Strokes?.Length > 0)
                using (var stream = new MemoryStream(signature.Strokes)) _ink.Strokes = new StrokeCollection(stream);
            else _imported.Source = signature?.Image;
        }
        private void Import()
        {
            var dialog = new OpenFileDialog { Filter = "Подпись PNG (*.png)|*.png", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try { Select(SignatureLibrary.Import(dialog.FileName, Path.GetFileNameWithoutExtension(dialog.FileName))); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        private void Save()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_name.Text)) throw new ArgumentException("Укажите название подписи.");
                if (_ink.Strokes.Count == 0 && _imported.Source == null) throw new ArgumentException("Нарисуйте подпись или загрузите PNG.");
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    context.DrawRectangle(Brushes.White, null, new Rect(0, 0, _ink.ActualWidth, _ink.ActualHeight));
                    if (_imported.Source != null)
                    {
                        var image = (BitmapSource)_imported.Source;
                        double scale = Math.Min((_ink.ActualWidth - 48) / image.PixelWidth, (_ink.ActualHeight - 48) / image.PixelHeight);
                        double width = image.PixelWidth * scale, height = image.PixelHeight * scale;
                        context.DrawImage(image, new Rect((_ink.ActualWidth - width) / 2, (_ink.ActualHeight - height) / 2, width, height));
                    }
                    _ink.Strokes.Draw(context);
                }
                var bitmap = new RenderTargetBitmap(Math.Max(1, (int)_ink.ActualWidth), Math.Max(1, (int)_ink.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var result = new StampSignature { Name = _name.Text.Trim(), Png = SignatureLibrary.Normalize(bitmap) };
                // Preserve editable strokes only when the signature consists entirely of ink.
                if (_imported.Source == null) using (var stream = new MemoryStream()) { _ink.Strokes.Save(stream); result.Strokes = stream.ToArray(); }
                if (_source != null && _source.Name == result.Name) result.Id = _source.Id;
                SignatureLibrary.Save(result);
                Result = result; DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить подпись: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
}
