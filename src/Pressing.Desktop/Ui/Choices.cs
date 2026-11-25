using Pressing.Core;

namespace Pressing.Desktop.Ui;

/// <summary>Valeur + libellé pour alimenter un <see cref="SelectBox"/>.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public static class ChoiceExtensions
{
    public static void SetChoices<T>(this SelectBox box, IEnumerable<Choice<T>> choices, T? selected = default)
    {
        List<Choice<T>> list = choices.ToList();
        box.Format = o => ((Choice<T>)o).Label;
        int index = selected is null ? 0 : Math.Max(0, list.FindIndex(c => EqualityComparer<T>.Default.Equals(c.Value, selected)));
        box.SetItems(list.Cast<object>(), index);
    }

    public static T? SelectedValue<T>(this SelectBox box) =>
        box.SelectedItem is Choice<T> c ? c.Value : default;
}

public static class Choices
{
    public static IEnumerable<Choice<PaymentMethod>> PaymentMethods() =>
        Enum.GetValues<PaymentMethod>().Select(m => new Choice<PaymentMethod>(m, m.Label()));

    public static IEnumerable<Choice<ServiceType>> Services() =>
        Enum.GetValues<ServiceType>().Select(m => new Choice<ServiceType>(m, m.Label()));
}
