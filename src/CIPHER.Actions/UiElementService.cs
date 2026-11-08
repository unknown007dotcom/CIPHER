using System.Windows;
using System.Windows.Automation;

namespace CIPHER.Actions;

/// <summary>
/// Finds and activates named UI elements inside a window through UI Automation.
/// Matching prefers exact names, then AutomationIds, then partial names; actionable
/// control types and supported patterns score higher. Activation prefers semantic
/// patterns (Invoke/Toggle/SelectionItem/ExpandCollapse/Legacy) and only falls back
/// to a synthesized mouse click when an element exposes a clickable point.
/// </summary>
public sealed class UiElementService
{
    private readonly InputService _inputService;

    private static readonly ControlType[] InteractiveTypes =
    {
        ControlType.Button, ControlType.CheckBox, ControlType.RadioButton,
        ControlType.MenuItem, ControlType.ListItem, ControlType.TabItem,
        ControlType.Hyperlink, ControlType.SplitButton, ControlType.MenuBar
    };

    private static readonly ControlType[] TextEntryTypes =
    {
        ControlType.Edit, ControlType.Document, ControlType.ComboBox
    };

    public UiElementService()
        : this(new InputService())
    {
    }

    public UiElementService(InputService inputService)
    {
        _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
    }

    public sealed record ElementMatch(AutomationElement Element, int Score, string MatchedBy);

    public sealed record ActivationResult(bool Activated, string Method, string? StateAfter);

    /// <summary>Finds the best element matching a spoken/typed name within a window.</summary>
    public ElementMatch? FindElement(AutomationElement scope, string name, bool preferTextEntry = false)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmedName = name.Trim();
        var variants = NameVariants(trimmedName);

        // 1. Exact name (case-insensitive), including type-word variants such as
        //    "Save button" -> "Save" and "Search field" -> "Search".
        foreach (var variant in variants)
        {
            var exact = FindBest(
                scope,
                new PropertyCondition(AutomationElement.NameProperty, variant, PropertyConditionFlags.IgnoreCase),
                variant,
                "name",
                100,
                preferTextEntry);
            if (exact is not null)
            {
                return exact;
            }
        }

        // 2. AutomationId.
        foreach (var variant in variants)
        {
            var byId = FindBest(
                scope,
                new PropertyCondition(AutomationElement.AutomationIdProperty, variant),
                variant,
                "automationId",
                95,
                preferTextEntry);
            if (byId is not null)
            {
                return byId;
            }
        }

        // 3. Partial name scan.
        try
        {
            var all = scope.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            ElementMatch? best = null;
            foreach (AutomationElement element in all)
            {
                string elementName;
                try
                {
                    elementName = element.Current.Name ?? string.Empty;
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }

                if (elementName.Length == 0 ||
                    !variants.Any(variant => elementName.Contains(variant, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var scored = Score(element, 50, "partial name", preferTextEntry);
                if (scored is not null && (best is null || scored.Score > best.Score))
                {
                    best = scored;
                }
            }

            return best;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static readonly string[] TypeWordSuffixes =
    {
        "text field", "text box", "radio button", "check box", "menu item",
        "textbox", "button", "link", "checkbox", "radio", "tab", "menu",
        "item", "field", "box", "input", "editor"
    };

    /// <summary>
    /// Builds match candidates: the name as given, plus the name without a trailing
    /// control-type word, so "Save button" also matches an element named "Save" while
    /// a real name such as "CIPHER command input" still matches exactly first.
    /// </summary>
    private static IReadOnlyList<string> NameVariants(string name)
    {
        var variants = new List<string> { name };
        foreach (var suffix in TypeWordSuffixes)
        {
            var tail = " " + suffix;
            if (name.Length > tail.Length &&
                name.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
            {
                var stripped = name[..^tail.Length].Trim();
                if (stripped.Length > 0 && !variants.Contains(stripped, StringComparer.OrdinalIgnoreCase))
                {
                    variants.Add(stripped);
                }
            }
        }

        return variants;
    }

    /// <summary>Activates an element using the most semantic mechanism it supports.</summary>
    public ActivationResult Activate(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var stateBefore = ReadState(element);

        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject) &&
            invokeObject is InvokePattern invoke)
        {
            invoke.Invoke();
            return FinishActivation(element, "InvokePattern");
        }

        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggleObject) &&
            toggleObject is TogglePattern toggle)
        {
            toggle.Toggle();
            return FinishActivation(element, "TogglePattern");
        }

        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionObject) &&
            selectionObject is SelectionItemPattern selectionItem)
        {
            selectionItem.Select();
            return FinishActivation(element, "SelectionItemPattern");
        }

        if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandObject) &&
            expandObject is ExpandCollapsePattern expandCollapse)
        {
            expandCollapse.Expand();
            return FinishActivation(element, "ExpandCollapsePattern");
        }

        if (element.TryGetCurrentPattern(LegacyIAccessiblePattern.Pattern, out var legacyObject) &&
            legacyObject is LegacyIAccessiblePattern legacy)
        {
            legacy.DoDefaultAction();
            return FinishActivation(element, "LegacyIAccessiblePattern");
        }

        if (element.TryGetClickablePoint(out Point point))
        {
            _inputService.ClickAt(point.X, point.Y);
            return FinishActivation(element, "mouse click at clickable point");
        }

        return new ActivationResult(false, "none", stateBefore);
    }

    /// <summary>Returns a comparable state string for toggle/selection verification.</summary>
    public static string? ReadState(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggleObject) &&
                toggleObject is TogglePattern toggle)
            {
                return $"toggle:{toggle.Current.ToggleState}";
            }

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionObject) &&
                selectionObject is SelectionItemPattern selectionItem)
            {
                return $"selected:{selectionItem.Current.IsSelected}";
            }

            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandObject) &&
                expandObject is ExpandCollapsePattern expandCollapse)
            {
                return $"expand:{expandCollapse.Current.ExpandCollapseState}";
            }
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        return null;
    }

    private ActivationResult FinishActivation(AutomationElement element, string method)
    {
        System.Threading.Thread.Sleep(150);
        return new ActivationResult(true, method, ReadState(element));
    }

    private static ElementMatch? FindBest(
        AutomationElement scope,
        Condition condition,
        string name,
        string matchedBy,
        int baseScore,
        bool preferTextEntry)
    {
        AutomationElementCollection candidates;
        try
        {
            candidates = scope.FindAll(TreeScope.Descendants, condition);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        ElementMatch? best = null;
        foreach (AutomationElement candidate in candidates)
        {
            var scored = Score(candidate, baseScore, matchedBy, preferTextEntry);
            if (scored is not null && (best is null || scored.Score > best.Score))
            {
                best = scored;
            }
        }

        return best;
    }

    private static ElementMatch? Score(
        AutomationElement element,
        int baseScore,
        string matchedBy,
        bool preferTextEntry)
    {
        try
        {
            var score = baseScore;
            var controlType = element.Current.ControlType;
            var preferred = preferTextEntry ? TextEntryTypes : InteractiveTypes;

            if (preferred.Contains(controlType))
            {
                score += 25;
            }

            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out _) ||
                element.TryGetCurrentPattern(TogglePattern.Pattern, out _) ||
                element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _) ||
                element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _) ||
                element.TryGetCurrentPattern(ValuePattern.Pattern, out _) ||
                element.TryGetCurrentPattern(TextPattern.Pattern, out _))
            {
                score += 15;
            }

            if (!element.Current.IsEnabled)
            {
                score -= 60;
            }

            if (element.Current.IsOffscreen)
            {
                score -= 40;
            }

            return new ElementMatch(element, score, matchedBy);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }
}
