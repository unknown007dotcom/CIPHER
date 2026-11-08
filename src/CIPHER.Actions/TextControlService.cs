using System.Windows.Automation;

namespace CIPHER.Actions;

/// <summary>
/// Finds readable text controls (editors) inside a window and reads their content through
/// UI Automation patterns. This is how CIPHER verifies typed text without screenshots or OCR.
/// </summary>
public sealed class TextControlService
{
    private const int MaxCharactersToRead = 20_000;

    /// <summary>
    /// Finds the most likely main text editor inside a window: an enabled Edit or Document
    /// control that exposes a readable Value or Text pattern. Larger controls win ties.
    /// </summary>
    public AutomationElement? FindTextControl(AutomationElement window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var condition = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));

        AutomationElementCollection candidates;
        try
        {
            candidates = window.FindAll(TreeScope.Descendants, condition);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        AutomationElement? best = null;
        var bestScore = double.MinValue;

        foreach (AutomationElement candidate in candidates)
        {
            double score;
            try
            {
                if (!candidate.Current.IsEnabled || candidate.Current.IsOffscreen)
                {
                    continue;
                }

                score = 0;
                if (candidate.TryGetCurrentPattern(ValuePattern.Pattern, out _))
                {
                    score += 40;
                }

                if (candidate.TryGetCurrentPattern(TextPattern.Pattern, out _))
                {
                    score += 40;
                }

                if (score == 0)
                {
                    continue;
                }

                if (candidate.Current.ControlType == ControlType.Document)
                {
                    score += 10;
                }

                var bounds = candidate.Current.BoundingRectangle;
                if (!bounds.IsEmpty)
                {
                    score += Math.Min(20, bounds.Width * bounds.Height / 100_000d);
                }
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Reads the text of the window's main editor, or null when it is not readable.</summary>
    public string? ReadWindowText(AutomationElement window)
    {
        var control = FindTextControl(window);
        return control is null ? null : ReadText(control);
    }

    /// <summary>Reads text from a specific control via ValuePattern, then TextPattern.</summary>
    public string? ReadText(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) &&
                valuePatternObject is ValuePattern valuePattern)
            {
                return valuePattern.Current.Value;
            }

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject) &&
                textPatternObject is TextPattern textPattern)
            {
                var text = textPattern.DocumentRange.GetText(MaxCharactersToRead);
                return text;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
            return null;
        }

        return null;
    }
}
