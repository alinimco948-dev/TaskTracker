namespace TaskTracker.Models.ViewModels;

public static class ViewHelpers
{
    /// <summary>
    /// Get initials from a name
    /// </summary>
    public static string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpper();
        return (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpper();
    }

    /// <summary>
    /// Get performance score color class based on score
    /// </summary>
    public static string GetScoreColorClass(int score)
    {
        return score >= 80 ? "text-green-600" : score >= 60 ? "text-yellow-600" : "text-red-600";
    }

    /// <summary>
    /// Get performance score background color for progress bar
    /// </summary>
    public static string GetScoreBgColor(int score)
    {
        return score >= 80 ? "green" : score >= 60 ? "yellow" : "red";
    }

    /// <summary>
    /// Get performance icon based on score
    /// </summary>
    public static string GetScoreIcon(int score)
    {
        return score >= 80 ? "fa-trophy" : score >= 60 ? "fa-chart-line" : "fa-exclamation-triangle";
    }

    /// <summary>
    /// Get status badge HTML
    /// </summary>
    public static string GetStatusBadge(bool isActive)
    {
        if (isActive)
        {
            return "<span class='px-2 py-1 bg-green-100 text-green-700 rounded-full text-xs font-medium'><i class='fas fa-circle text-green-500 mr-1 text-xs'></i>Active</span>";
        }
        return "<span class='px-2 py-1 bg-red-100 text-red-700 rounded-full text-xs font-medium'><i class='fas fa-circle text-red-500 mr-1 text-xs'></i>Inactive</span>";
    }

    /// <summary>
    /// Format date to short format (MMM dd, yyyy)
    /// </summary>
    public static string FormatShortDate(DateTime? date)
    {
        return date?.ToString("MMM dd, yyyy") ?? "Not set";
    }

    /// <summary>
    /// Format date to medium format (MMM dd)
    /// </summary>
    public static string FormatMediumDate(DateTime date)
    {
        return date.ToString("MMM dd");
    }

    /// <summary>
    /// Get rank medal class for top 3 performers
    /// </summary>
    public static string GetRankMedalClass(int rank)
    {
        return rank switch
        {
            1 => "bg-amber-100 text-amber-700",
            2 => "bg-slate-100 text-slate-700",
            3 => "bg-orange-100 text-orange-700",
            _ => "bg-gray-100 text-gray-600"
        };
    }

    /// <summary>
    /// Get rank icon for top 3 performers
    /// </summary>
    public static string GetRankIcon(int rank)
    {
        return rank switch
        {
            1 => "fa-trophy",
            2 => "fa-medal",
            3 => "fa-medal",
            _ => ""
        };
    }

    /// <summary>
    /// Get rank gradient color for top 3 cards
    /// </summary>
    public static string GetRankGradient(int rank)
    {
        return rank switch
        {
            1 => "from-amber-500 to-amber-700",
            2 => "from-slate-500 to-slate-700",
            3 => "from-orange-500 to-orange-700",
            _ => "from-gray-500 to-gray-700"
        };
    }
}