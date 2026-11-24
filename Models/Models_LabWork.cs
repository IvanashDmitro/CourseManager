namespace CourseManager.Models;

public sealed class LabWork : CourseActivity
{
    private int _defenseBonus;

    public LabWork()
    {
    }

    public LabWork(string title, double maxScore, DateOnly deadline, int defenseBonus = 0)
        : base(title, maxScore, deadline)
    {
        DefenseBonus = defenseBonus;
    }

    public int DefenseBonus
    {
        get => _defenseBonus;
        set
        {
            if (value < 0 || value > 10)
                throw new ArgumentOutOfRangeException(nameof(value), "Бонус за захист має бути від 0 до 10.");
            _defenseBonus = value;
        }
    }

    public override string TypeName => "Лабораторна";

    public override double CalculateScore(double rawScore, bool isLate)
    {
        ValidateRawScore(rawScore);
        double adjusted = isLate ? rawScore * 0.85
            : rawScore > 0 ? rawScore + DefenseBonus
            : 0;
        return Math.Round(Math.Min(adjusted, MaxScore), 2);
    }
}
