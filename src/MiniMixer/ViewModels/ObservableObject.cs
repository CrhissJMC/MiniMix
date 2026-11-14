using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MiniMixer.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>Botón de ruteo A1/A2/A3 de una tira.</summary>
public sealed class RouteViewModel : ObservableObject
{
    private readonly Action<int, bool> _changed;
    private bool _isOn;

    public RouteViewModel(string busName, int index, bool initial, Action<int, bool> changed)
    {
        Label = "▸" + busName;
        Index = index;
        _changed = changed;
        _isOn = initial;
        if (initial) changed(index, true);
    }

    public string Label { get; }
    public int Index { get; }

    public bool IsOn
    {
        get => _isOn;
        set { if (SetProperty(ref _isOn, value)) _changed(Index, value); }
    }
}
