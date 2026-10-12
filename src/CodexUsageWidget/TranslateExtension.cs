using System;
using System.Windows.Data;
using System.Windows.Markup;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Localization.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
