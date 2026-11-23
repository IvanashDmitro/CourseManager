using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CourseManager.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "activityType")]
[JsonDerivedType(typeof(LabWork), "LabWork")]
[JsonDerivedType(typeof(Assignment), "Assignment")]
[JsonDerivedType(typeof(Quiz), "Quiz")]
public abstract class CourseActivity : IGradable, INotifyPropertyChanged
{
    private string _title = string.Empty;
    private double _maxScore;
    private DateOnly _deadline;

    // ШІ: Claude Code (Claude Opus 5.5) – сповіщення про зміну і DisplayName, щоб після редагування роботи
    // нові назва, максимум і дедлайн одразу показувалися у списках.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }

    // Так робота виглядає у списках: «Лабораторна 2 (макс. 15, до 15.11.2026)».
    [JsonIgnore]
    public string DisplayName => $"{Title} (макс. {MaxScore}, до {Deadline:dd.MM.yyyy})";

    protected CourseActivity()
    {
    }

    protected CourseActivity(string title, double maxScore, DateOnly deadline)
    {
        Title = title;
        MaxScore = maxScore;
        Deadline = deadline;
    }

    public string Title
    {
        get => _title;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Назва активності не може бути порожньою.", nameof(value));
            _title = value.Trim();
            OnChanged(nameof(Title));
        }
    }

    public double MaxScore
    {
        get => _maxScore;
        set
        {
            if (value <= 0 || value > 100)
                throw new ArgumentOutOfRangeException(nameof(value), "Максимальний бал має бути більшим за 0 і не більшим за 100.");
            _maxScore = value;
            OnChanged(nameof(MaxScore));
        }
    }

    public DateOnly Deadline
    {
        get => _deadline;
        set
        {
            if (value.Year < 2020 || value.Year > 2035)
                throw new ArgumentOutOfRangeException(nameof(value), "Некоректна дата дедлайну.");
            _deadline = value;
            OnChanged(nameof(Deadline));
        }
    }

    public abstract string TypeName { get; }

    public abstract double CalculateScore(double rawScore, bool isLate);

    public override string ToString() => DisplayName;

    protected void ValidateRawScore(double rawScore)
    {
        if (rawScore < 0 || rawScore > MaxScore)
            throw new ArgumentOutOfRangeException(nameof(rawScore),
                $"Оцінка {rawScore} виходить за межі [0; {MaxScore}].");
    }
}
