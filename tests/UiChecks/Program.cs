// Copyright (c) 2026 OOXGN. All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using AutocadTools.Views;

namespace UiChecks
{
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b && !b;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Convert(value, targetType, parameter, culture);
    }
    public class InverseBoolConverter : InverseBooleanConverter { }
    public class Field { public string Value { get; set; } = ""; }
    public class PlotRow { public string Name { get; set; } public string SheetNumber { get; set; } public string Format { get; set; } public string Status { get; set; } public bool IsSelected { get; set; } }
    public class Fixture
    {
        public object Options { get; set; }
        public string Prefix { get; set; } = "П";
        public string Suffix { get; set; } = "";
        public string StartNumberText { get; set; } = "1";
        public string StepText { get; set; } = "1";
        public string DigitsText { get; set; } = "2";
        public int OrderIndex { get; set; } = 1;
        public int SortingIndex { get; set; } = 0;
        public int ModeIndex { get; set; } = 2;
        public Visibility IsSortingModeVisibility { get; set; } = Visibility.Visible;
        public Visibility IsAttributeModeVisibility { get; set; } = Visibility.Visible;
        public string[] AvailableAttributes { get; set; } = new[] { "ПОЗИЦИЯ", "МАРКА" };
        public string AttributeTag { get; set; } = "ПОЗИЦИЯ";
        public string[] PreviewItems { get; set; } = new[] { "П01", "П02", "П03", "П04", "П05" };
        public bool IsBusy { get; set; }
        public bool? IsAllSelected { get; set; } = null;
        public bool PlotInBackground { get; set; } = true;
        public string StatusMessage { get; set; } = "Найдено 3 листа. Выберите рамки и проверьте параметры печати.";
        public string[] AllFormatNames { get; set; } = new[] { "А4", "А3", "А2", "Нестандартный" };
        public object[] Items => new object[] {
            new PlotRow { Name = "Отопление и вентиляция. План этажа на отметке +3,600", SheetNumber = "01", Format = "А3", Status = "Готов", IsSelected = true },
            new PlotRow { Name = "Схемы систем приточной вентиляции П1, П2", SheetNumber = "02", Format = "А2", Status = "Ошибка валидации", IsSelected = true },
            new PlotRow { Name = "Спецификация оборудования и материалов", SheetNumber = "03", Format = "Нестандартный", Status = "Готов", IsSelected = false }
        };
        public string ОбозначениеДокумента { get; set; } = "024-ОВ-Р";
        public string НаименованиеОбъекта { get; set; } = "Жилой комплекс с подземной автостоянкой";
        public string НаименованиеУчастка { get; set; } = "Корпус 2. Отопление, вентиляция и кондиционирование";
        public Dictionary<string, Field> FieldsMap { get; } = new Dictionary<string, Field>();
        public string TotalLengthFormatted { get; set; } = "12 458,36";
        public string TotalAreaFormatted { get; set; } = "186,42";
        public bool HasTotalArea { get; set; } = true;
        public string[] LengthUnits { get; set; } = new[] { "Millimeters", "Meters", "Kilometers" };
        public string SelectedLengthUnit { get; set; } = "Meters";
        public string[] AreaUnits { get; set; } = new[] { "SquareMillimeters", "SquareMeters", "SquareKilometers" };
        public string SelectedAreaUnit { get; set; } = "SquareMeters";
        public string ScaleNumerator { get; set; } = "1";
        public string ScaleDenominator { get; set; } = "100";
        public string ScaleInfo { get; set; } = "Масштаб 1:100";
        public int TotalCount { get; set; } = 8;
        public int GroupCount { get; set; } = 2;
        public object[] Groups => new object[] {
            new { ObjectType = "Полилинии", Count = 5, TotalLengthFormatted = "12 300,00 м", TotalAreaFormatted = "186,42 м²", HasArea = true, Items = Array.Empty<object>() },
            new { ObjectType = "Отрезки", Count = 3, TotalLengthFormatted = "158,36 м", TotalAreaFormatted = "", HasArea = false, Items = Array.Empty<object>() }
        };
    }
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                string root = Path.GetFullPath(args[0]);
                string output = Path.Combine(root, "artifacts", "validation", "ui");
                Directory.CreateDirectory(output);
                foreach (var dark in new[] { false, true })
                {
                    var panel = new WrapPanel { Width = 9 * 56, Background = dark ? new SolidColorBrush(Color.FromRgb(43, 43, 43)) : Brushes.White };
                    foreach (var command in ToolIcons.Commands)
                        panel.Children.Add(new Image { Source = ToolIcons.Create(command, dark), Width = 32, Height = 32, Margin = new Thickness(12) });
                    panel.Measure(new Size(panel.Width, double.PositiveInfinity));
                    panel.Arrange(new Rect(panel.DesiredSize)); panel.UpdateLayout();
                    var icons = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    icons.Render(panel);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(icons));
                    using var iconStream = File.Create(Path.Combine(output, dark ? "Ribbon-dark.png" : "Ribbon-light.png"));
                    png.Save(iconStream);
                }
                foreach (var match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(root, "App.cs")), "AddButton\\([^;]+?, \\\"([^\\\"]+)\\\"\\);").Cast<System.Text.RegularExpressions.Match>())
                    if (!ToolIcons.Commands.Contains(match.Groups[1].Value, StringComparer.OrdinalIgnoreCase)) throw new Exception("Ribbon command has no icon: " + match.Groups[1].Value);
                int count = 0;
                foreach (string path in Directory.GetFiles(Path.Combine(root, "Views"), "*.xaml").Where(p => !p.EndsWith("Styles.xaml")))
                foreach (double scale in new[] { 1.0, 1.5 })
                foreach (bool compact in new[] { false, true })
                {
                    XDocument xml = XDocument.Load(path);
                    var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
                    xml.Root.Attribute(x + "Class")?.Remove();
                    foreach (var element in xml.Descendants().Where(e => e.Name.NamespaceName.StartsWith("clr-namespace:AutocadTools.")))
                        element.Name = XNamespace.Get("clr-namespace:UiChecks;assembly=AutocadTools") + element.Name.LocalName;
                    foreach (var attribute in xml.Descendants().Attributes().ToList())
                    {
                        if (new[] { "Click", "Checked", "Unchecked", "Closing", "RequestNavigate", "DragStarted", "DragDelta", "DragCompleted" }.Contains(attribute.Name.LocalName)) attribute.Remove();
                        else if (attribute.IsNamespaceDeclaration && attribute.Value.StartsWith("clr-namespace:AutocadTools.")) attribute.Value = "clr-namespace:UiChecks;assembly=AutocadTools";
                        else if (attribute.Name.LocalName == "Source" && attribute.Value == "Styles.xaml") attribute.Value = Path.Combine(root, "Views", "Styles.xaml");
                    }
                    var window = (Window)XamlReader.Parse(xml.ToString());
                    UiTheme.Apply(window);
                    var fixture = new Fixture();
                    fixture.Options = Path.GetFileNameWithoutExtension(path) switch
                    {
                        "CleanDwgWindow" => new AutocadTools.Models.CleanDwgOptions(),
                        "PlanPrepWindow" => new AutocadTools.Models.PlanPrepOptions(),
                        "SelectSimilarWindow" => new AutocadTools.Models.SelectSimilarOptions(),
                        "StripTextWindow" => new AutocadTools.Models.StripTextOptions(),
                        _ => null
                    };
                    foreach (var match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path), @"FieldsMap\[([^\]]+)\]").Cast<System.Text.RegularExpressions.Match>())
                        fixture.FieldsMap[match.Groups[1].Value] = new Field { Value = match.Groups[1].Value.Contains("Фам.") ? "Иванов И. И." : "Р" };
                    window.DataContext = fixture;
                    if (window.FindName("VersionText") is TextBlock version) version.Text = "Версия 0.1.0";
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = -32000;
                    window.Top = -32000;
                    window.ShowActivated = false;
                    window.ShowInTaskbar = false;
                    if (compact && window.ResizeMode != ResizeMode.NoResize)
                    {
                        if (!double.IsNaN(window.Width)) window.Width = Math.Max(window.MinWidth, window.Width - 120);
                        if (!double.IsNaN(window.Height)) window.Height = Math.Max(window.MinHeight, window.Height - 120);
                    }
                    window.Show();
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    window.UpdateLayout();
                    var content = (FrameworkElement)window.Content;
                    foreach (var combo in Descendants(content).OfType<ComboBox>().ToArray())
                    {
                        combo.ApplyTemplate();
                        if (combo.IsEditable && combo.Template.FindName("PART_EditableTextBox", combo) is not TextBox)
                            throw new Exception("Editable combo box has no text editor.");
                        combo.IsDropDownOpen = true;
                        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        if (combo.Template.FindName("PART_Popup", combo) is not System.Windows.Controls.Primitives.Popup popup || !popup.IsOpen)
                            throw new Exception("Combo box popup did not open.");
                        combo.IsDropDownOpen = false;
                    }
                    var surface = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
                    double width = surface.ActualWidth, height = surface.ActualHeight;
                    // Localized command labels must remain fully inside their hit targets.
                    foreach (var button in Descendants(content).OfType<Button>().Where(b => b.IsVisible && b.Content is string))
                    {
                        var text = new FormattedText((string)button.Content, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight,
                            new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize, Brushes.Black, 1);
                        if (text.Width > button.ActualWidth - button.Padding.Left - button.Padding.Right + 1)
                            throw new Exception(Path.GetFileName(path) + ": clipped button: " + button.Content);
                        var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
                        if (bounds.Bottom > content.ActualHeight + 1 || bounds.Right > content.ActualWidth + 1)
                            throw new Exception(Path.GetFileName(path) + ": action outside window: " + button.Content);
                    }
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bitmap.Render(surface);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + $"-{scale * 100:0}" + (compact ? "-compact" : "") + ".png"))) encoder.Save(stream);
                    window.Close();
                    count++;
                }
                Console.WriteLine($"PASS: {count} WPF renders, 100% / 150%, default / minimum dimensions; combo boxes, command label fit; 17 vector icons.");
                app.Shutdown();
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}
