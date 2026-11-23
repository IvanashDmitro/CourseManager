namespace CourseManager.Models;

public sealed class Assignment : CourseActivity
{
    private double _latePenaltyRate = 0.2;

    public Assignment()
    {
    }

    public Assignment(string title, double maxScore, DateOnly deadline, double latePenaltyRate = 0.2)
        : base(title, maxScore, deadline)
    {
        LatePenaltyRate = latePenaltyRate;
    }

    public double LatePenaltyRate
    {
        get => _latePenaltyRate;
        set
        {
            if (value < 0 || value > 0.5)
                throw new ArgumentOutOfRangeException(nameof(value), "Коефіцієнт штрафу має бути в межах [0; 0.5].");
            _latePenaltyRate = value;
        }
    }

    public override string TypeName => "Практична";

    public override double CalculateScore(double rawScore, bool isLate)
    {
        ValidateRawScore(rawScore);
        double result = isLate ? rawScore * (1.0 - LatePenaltyRate) : rawScore;
        return Math.Round(Math.Max(0, result), 2);
    }
}
