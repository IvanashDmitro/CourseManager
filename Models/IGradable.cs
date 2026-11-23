namespace CourseManager.Models;

public interface IGradable
{
    double MaxScore { get; }
    double CalculateScore(double rawScore, bool isLate);
}
