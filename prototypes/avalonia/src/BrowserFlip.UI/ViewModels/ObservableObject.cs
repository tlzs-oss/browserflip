using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BrowserFlip.UI.ViewModels;

/// <summary>
/// Hand-rolled <see cref="INotifyPropertyChanged"/> base, matching the product's existing MVVM
/// style. BrowserFlip deliberately takes no MVVM framework dependency.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Assigns <paramref name="field"/> and raises the change notification when it differs.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }
}
