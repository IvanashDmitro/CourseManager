using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CourseManager.Models;

// ШІ: Claude Code (Claude Opus 5.5) – INotifyPropertyChanged і DisplayName для оновлення ПІБ і групи в інтерфейсі.
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
// INotifyPropertyChanged: після «Оновити» таблиця і списки одразу показують нові ПІБ і групу.
public sealed class Student : INotifyPropertyChanged
{
    private string _fullName = string.Empty;
    private string _group = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Student()
    {
    }

    public Student(string fullName, string group)
    {
        FullName = fullName;
        Group = group;
    }

    public string FullName
    {
        get => _fullName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("ПІБ здобувача не може бути порожнім.", nameof(value));
            _fullName = value.Trim();
            OnChanged(nameof(FullName));
        }
    }

    public string Group
    {
        get => _group;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Група не може бути порожньою.", nameof(value));
            _group = value.Trim().ToUpperInvariant();
            OnChanged(nameof(Group));
        }
    }

    // DisplayName використовується у випадному списку здобувачів.
    [JsonIgnore]
    public string DisplayName => $"{FullName} ({Group})";

    private void OnChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }

    public override string ToString() => DisplayName;
}
