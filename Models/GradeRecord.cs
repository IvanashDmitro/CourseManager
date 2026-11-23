namespace CourseManager.Models;

public sealed class GradeRecord
{
    private CourseActivity _activity = null!;
    private double _rawScore;

    public GradeRecord()
    {
    }

    public GradeRecord(CourseActivity activity, double rawScore, bool isLate)
    {
        Activity = activity;
        IsLate = isLate;
        RawScore = rawScore;
    }

    public CourseActivity Activity
    {
        get => _activity;
        set => _activity = value ?? throw new ArgumentNullException(nameof(value));
    }

    public double RawScore
    {
        get => _rawScore;
        set
        {
            if (_activity != null && (value < 0 || value > _activity.MaxScore))
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Бал {value} має бути в межах [0; {_activity.MaxScore}].");
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Бал не може бути від'ємним.");
            _rawScore = value;
        }
    }

    public bool IsLate { get; set; }

    public double FinalScore => Activity.CalculateScore(RawScore, IsLate);
}
