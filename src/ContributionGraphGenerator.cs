using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public class ContributionGraphGenerator
{
    private const int CellSize = 11;
    private const int CellSpacing = 3;
    private const int MarginTop = 60;
    private const int MarginBottom = 40;
    private const int MarginLeft = 40;
    private const int MarginRight = 150;
    private const int MonthLabelHeight = 20;
    private const int DayLabelWidth = 30;
    private const int LegendHeight = 20;
    
    private readonly Rgba32 BackgroundColor = new Rgba32(13, 17, 23);
    private readonly Rgba32 TextColor = new Rgba32(201, 209, 217);
    private readonly Rgba32 GridColor = new Rgba32(22, 27, 34);
    
    private readonly Rgba32[] ContributionColors = new[]
    {
        new Rgba32(22, 27, 34),      // No contributions
        new Rgba32(14, 68, 41),      // 1-2 contributions
        new Rgba32(0, 109, 50),      // 3-5 contributions
        new Rgba32(38, 166, 65),     // 6-10 contributions
        new Rgba32(57, 211, 83),     // 11+ contributions
    };

    public Image<Rgba32> Generate(Dictionary<DateTime, int> commitsByDate, DateTime startDate, DateTime endDate)
    {
        var allDays = GetDaysInRange(startDate, endDate);
        
        var weeks = GroupDaysByWeek(allDays);
        
        int gridWidth = weeks.Count * (CellSize + CellSpacing);
        int gridHeight = 7 * (CellSize + CellSpacing);
        int imageWidth = MarginLeft + DayLabelWidth + gridWidth + MarginRight;
        int imageHeight = MarginTop + MonthLabelHeight + gridHeight + MarginBottom + LegendHeight;
        
        var image = new Image<Rgba32>(imageWidth, imageHeight, BackgroundColor);
        
        int maxCommits = commitsByDate.Values.DefaultIfEmpty(0).Max();
        
        DrawMonthLabels(image, weeks, startDate);
        
        DrawDayLabels(image, weeks);
        
        DrawContributionGrid(image, weeks, commitsByDate, maxCommits);
        
        DrawLegend(image, imageWidth, imageHeight, maxCommits);
        
        DrawTitle(image, imageWidth, commitsByDate.Values.Sum());
        
        return image;
    }

    private List<DateTime> GetDaysInRange(DateTime startDate, DateTime endDate)
    {
        var days = new List<DateTime>();
        var current = startDate;
        while (current < endDate)
        {
            days.Add(current);
            current = current.AddDays(1);
        }
        return days;
    }

    private List<List<DateTime>> GroupDaysByWeek(List<DateTime> days)
    {
        var weeks = new List<List<DateTime>>();
        var currentWeek = new List<DateTime>();
        
        // GitHub style starts week on Sunday (DayOfWeek.Sunday = 0)
        // Find the first Sunday before or equal to the first day
        var firstDay = days[0];
        int dayOfWeek = (int)firstDay.DayOfWeek; // 0 = Sunday, 1 = Monday, ..., 6 = Saturday
        
        // Add empty days for the first week if needed (days before Sunday)
        // If firstDay is Sunday (0), no empty days needed
        // If firstDay is Monday (1), add 1 empty day (Sunday)
        // etc.
        for (int i = 0; i < dayOfWeek; i++)
        {
            currentWeek.Add(DateTime.MinValue); // Placeholder for empty cells
        }
        
        foreach (var day in days)
        {
            currentWeek.Add(day);
            
            if (currentWeek.Count == 7)
            {
                weeks.Add(currentWeek);
                currentWeek = new List<DateTime>();
            }
        }
        
        // Add remaining days as the last week
        if (currentWeek.Count > 0)
        {
            while (currentWeek.Count < 7)
            {
                currentWeek.Add(DateTime.MinValue);
            }
            weeks.Add(currentWeek);
        }
        
        return weeks;
    }

    private void DrawMonthLabels(Image<Rgba32> image, List<List<DateTime>> weeks, DateTime startDate)
    {
        var font = SystemFonts.CreateFont("Segoe UI", 12, FontStyle.Regular);
        var brush = new SolidBrush(TextColor);
        
        string currentMonth = "";
        int currentMonthStartWeek = -1;
        
        for (int weekIndex = 0; weekIndex < weeks.Count; weekIndex++)
        {
            var week = weeks[weekIndex];
            var firstDayOfWeek = week.FirstOrDefault(d => d != DateTime.MinValue);
            
            if (firstDayOfWeek != DateTime.MinValue)
            {
                string month = firstDayOfWeek.ToString("MMM");
                
                if (month != currentMonth)
                {
                    if (currentMonthStartWeek >= 0 && currentMonthStartWeek < weekIndex)
                    {
                        int centerWeek = (currentMonthStartWeek + weekIndex) / 2;
                        int x = MarginLeft + DayLabelWidth + centerWeek * (CellSize + CellSpacing) + (CellSize + CellSpacing) / 2;
                        int y = MarginTop / 2 + 5;
        
                        var options = new RichTextOptions(font)
                        {
                            Origin = new PointF(x, y),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        
                        image.Mutate(ctx => ctx.DrawText(options, currentMonth, brush));
                    }
                    
                    currentMonth = month;
                    currentMonthStartWeek = weekIndex;
                }
            }
        }
        
        if (currentMonthStartWeek >= 0)
        {
            int centerWeek = (currentMonthStartWeek + weeks.Count) / 2;
            int x = MarginLeft + DayLabelWidth + centerWeek * (CellSize + CellSpacing) + (CellSize + CellSpacing) / 2;
            int y = MarginTop / 2 + 5;
        
            var options = new RichTextOptions(font)
            {
                Origin = new PointF(x, y),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            image.Mutate(ctx => ctx.DrawText(options, currentMonth, brush));
        }
    }

    private void DrawDayLabels(Image<Rgba32> image, List<List<DateTime>> weeks)
    {
        var font = SystemFonts.CreateFont("Segoe UI", 12, FontStyle.Regular);
        var brush = new SolidBrush(TextColor);
        
        string[] dayLabels = { "Mon", "Wed", "Fri" };
        int[] dayIndices = { 1, 3, 5 }; // Monday, Wednesday, Friday
        
        foreach (int dayIndex in dayIndices)
        {
            int y = MarginTop + MonthLabelHeight + dayIndex * (CellSize + CellSpacing) + CellSize / 2;
            int x = MarginLeft + DayLabelWidth - 5;
            
            var options = new RichTextOptions(font)
            {
                Origin = new PointF(x, y),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            image.Mutate(ctx => ctx.DrawText(options, dayLabels[dayIndex / 2], brush));
        }
    }

    private void DrawContributionGrid(Image<Rgba32> image, List<List<DateTime>> weeks, Dictionary<DateTime, int> commitsByDate, int maxCommits)
    {
        for (int weekIndex = 0; weekIndex < weeks.Count; weekIndex++)
        {
            var week = weeks[weekIndex];
            
            for (int dayIndex = 0; dayIndex < week.Count; dayIndex++)
            {
                var day = week[dayIndex];
                
                int x = MarginLeft + DayLabelWidth + weekIndex * (CellSize + CellSpacing);
                int y = MarginTop + MonthLabelHeight + dayIndex * (CellSize + CellSpacing);
                
                Rgba32 color;
                
                if (day == DateTime.MinValue)
                {
                    color = BackgroundColor; // Empty cell
                }
                else
                {
                    commitsByDate.TryGetValue(day, out int commitCount);
                    color = GetColorForCommitCount(commitCount, maxCommits);
                }
                
                var rect = new Rectangle(x, y, CellSize, CellSize);
                image.Mutate(ctx => ctx.Fill(color, rect));
            }
        }
    }

    private Rgba32 GetColorForCommitCount(int commitCount, int maxCommits)
    {
        if (commitCount == 0)
            return ContributionColors[0];
        
        if (maxCommits == 0)
            return ContributionColors[0];
        
        // Scale commit count to color index (1-4)
        float ratio = (float)commitCount / maxCommits;
        
        if (ratio < 0.25f)
            return ContributionColors[1];
        else if (ratio < 0.5f)
            return ContributionColors[2];
        else if (ratio < 0.75f)
            return ContributionColors[3];
        else
            return ContributionColors[4];
    }

    private void DrawLegend(Image<Rgba32> image, int imageWidth, int imageHeight, int maxCommits)
    {
        var font = SystemFonts.CreateFont("Segoe UI", 12, FontStyle.Regular);
        var brush = new SolidBrush(TextColor);
        
        int lessTextWidth = 35;
        int moreTextWidth = 40;
        
        // Calculate legend width
        int legendWidth = lessTextWidth + 5 + // "Less" + spacing
                          ContributionColors.Length * (CellSize + 2) + // Color squares (5 * 13 = 65)
                          5 + // spacing before "More"
                          moreTextWidth; // "More"
        
        int legendY = imageHeight - LegendHeight - 10;
        int legendX = imageWidth - MarginRight + 10;
        
        var lessOptions = new RichTextOptions(font)
        {
            Origin = new PointF(legendX, legendY),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        image.Mutate(ctx => ctx.DrawText(lessOptions, "Less", brush));
        legendX += lessTextWidth + 5;
        
        for (int i = 0; i < ContributionColors.Length; i++)
        {
            var rect = new Rectangle(legendX, legendY - CellSize / 2, CellSize, CellSize);
            image.Mutate(ctx => ctx.Fill(ContributionColors[i], rect));
            legendX += CellSize + 2;
        }
        
        legendX += 5;
        var moreOptions = new RichTextOptions(font)
        {
            Origin = new PointF(legendX, legendY),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        image.Mutate(ctx => ctx.DrawText(moreOptions, "More", brush));
    }

    private void DrawTitle(Image<Rgba32> image, int imageWidth, int totalContributions)
    {
        var font = SystemFonts.CreateFont("Segoe UI", 14, FontStyle.Regular);
        var brush = new SolidBrush(TextColor);
        
        string title = $"{totalContributions} contributions in {DateTime.Now.Year}";
        int x = MarginLeft + DayLabelWidth;
        int y = 15;
        
        var options = new RichTextOptions(font)
        {
            Origin = new PointF(x, y),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        
        image.Mutate(ctx => ctx.DrawText(options, title, brush));
    }
}

