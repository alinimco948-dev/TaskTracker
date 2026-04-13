using TaskTracker.Services.Interfaces;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services;

public class GradingService : IGradingService
{
    public double CompletionWeight => 0.7;
    public double OnTimeWeight => 0.3;
    
    public double CalculateWeightedScore(int totalTasks, int completedTasks, int onTimeTasks)
    {
        if (totalTasks == 0) return 0;
        
        var completionRate = Math.Round((double)completedTasks / totalTasks * 100, 1);
        var onTimeRate = completedTasks > 0 
            ? Math.Round((double)onTimeTasks / completedTasks * 100, 1) 
            : 0;
        
        double weightedScore;
        
        if (completedTasks == 0)
        {
            weightedScore = completionRate * CompletionWeight;
        }
        else if (onTimeTasks == completedTasks)
        {
            weightedScore = completionRate;
        }
        else
        {
            weightedScore = (completionRate * CompletionWeight) + (onTimeRate * OnTimeWeight);
        }
        
        return Math.Round(weightedScore, 1);
    }
    
    public (double completionRate, double onTimeRate, double weightedScore) CalculateScores(
        int totalTasks, int completedTasks, int onTimeTasks)
    {
        var completionRate = totalTasks > 0 
            ? Math.Round((double)completedTasks / totalTasks * 100, 1) 
            : 0;
        var onTimeRate = completedTasks > 0 
            ? Math.Round((double)onTimeTasks / completedTasks * 100, 1) 
            : 0;
        var weightedScore = CalculateWeightedScore(totalTasks, completedTasks, onTimeTasks);
        return (completionRate, onTimeRate, weightedScore);
    }
    
    public string GetPerformanceLevel(double weightedScore) => weightedScore switch
    {
        >= 90 => "Excellent",
        >= 75 => "Good",
        >= 60 => "Average",
        _ => "Needs Improvement"
    };
    
    public List<string> GenerateInsights(EmployeePerformanceViewModel report)
    {
        var insights = new List<string>();

        if (report.OverallScore >= 90)
            insights.Add("🌟 Excellent performance! Consistently completing tasks on time.");
        else if (report.OverallScore >= 75)
            insights.Add("👍 Good performance. Keep up the good work!");
        else if (report.OverallScore >= 60)
            insights.Add("📊 Average performance. There's room for improvement.");
        else
            insights.Add("⚠️ Needs improvement. Focus on completing tasks on time.");

        if (report.CompletionRate >= 95)
            insights.Add("✅ Outstanding task completion rate!");
        else if (report.CompletionRate < 70 && report.CompletionRate > 0)
            insights.Add("⚠️ Low task completion rate. Review pending tasks.");

        if (report.OnTimeRate >= 90)
            insights.Add("⏰ Excellent punctuality! Most tasks completed on time.");
        else if (report.OnTimeRate < 60 && report.OnTimeRate > 0)
            insights.Add("⌛ Time management needs improvement. Many tasks are late.");

        if (report.PendingTasks > 0)
            insights.Add($"📋 {report.PendingTasks} pending task(s) require attention.");
        
        if (report.LateTasks > 0)
            insights.Add($"⚠️ {report.LateTasks} task(s) were completed late.");

        return insights;
    }
    
    public List<string> GenerateRecommendations(EmployeePerformanceViewModel report)
    {
        var recommendations = new List<string>();

        if (report.CompletionRate < 80)
            recommendations.Add("🎯 Focus on completing pending tasks before starting new ones");
        
        if (report.OnTimeRate < 70)
            recommendations.Add("⏰ Start tasks earlier in the day to meet deadlines");

        if (report.OverallScore >= 80)
        {
            recommendations.Add("🌟 Consider mentoring other team members");
            recommendations.Add("📈 Continue maintaining high performance standards");
        }
        else if (report.OverallScore >= 60)
        {
            recommendations.Add("📊 Create a daily task schedule and prioritize urgent items");
            recommendations.Add("🎯 Set personal deadlines 15 minutes before actual deadlines");
        }
        else
        {
            recommendations.Add("🎯 Meet with supervisor to create an improvement plan");
            recommendations.Add("📋 Break down tasks into smaller, manageable steps");
            recommendations.Add("⏰ Use reminders and alarms for task deadlines");
        }

        if (report.PendingTasks > 0 && report.DailyBreakdown != null)
        {
            var pendingTasks = report.DailyBreakdown
                .Where(d => d.Status != "Completed")
                .Select(d => d.TaskName)
                .Distinct()
                .Take(3);
            
            if (pendingTasks.Any())
                recommendations.Add($"📋 Prioritize pending tasks: {string.Join(", ", pendingTasks)}");
        }

        if (report.LateTasks > 0 && report.OnTimeRate < 70)
        {
            recommendations.Add("⏰ Review deadlines and time management for tasks with late submissions");
        }

        return recommendations;
    }
}