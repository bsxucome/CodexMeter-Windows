function Wait-UiCondition([scriptblock]$Probe, [string]$Description, [int]$TimeoutSeconds = 15) {
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    do {
        try { if (& $Probe) { return } } catch [System.Windows.Automation.ElementNotAvailableException] { }
        if ($elapsed.Elapsed.TotalSeconds -ge $TimeoutSeconds) { break }
        Start-Sleep -Milliseconds 100
    } while ($true)
    throw "UI state not ready: $Description"
}
function Wait-ElementValue($Root, [string]$Id, [string]$Expected, [int]$TimeoutSeconds = 15) {
    $handle = $Root.Current.NativeWindowHandle
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    Wait-UiCondition {
        $provider = if ($handle -ne 0) { [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$handle) } else { $Root }
        $element = $provider.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if (!$element) { return $false }
        $value = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        return [string]::Equals($value.Current.Value, $Expected, [StringComparison]::Ordinal)
    } "value of $Id" $TimeoutSeconds
}
function Owned-Window([int]$OwnerId, [string]$Name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $OwnerId)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)))
    # A title TextBlock can have the same name as its window. Never accept that stale child as a window.
    $condition = New-Object System.Windows.Automation.AndCondition($condition,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $result = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($result) { return $result }
        $processCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $OwnerId)
        $parents = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $processCondition)
        foreach ($parent in $parents) {
            $result = $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            if ($result) { return $result }
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Owned window missing: $Name"
}
function Find-Element($Root, [string]$Value, [switch]$ById, [int]$TimeoutSeconds = 15) {
    $property = if ($ById) { [System.Windows.Automation.AutomationElement]::AutomationIdProperty } else { [System.Windows.Automation.AutomationElement]::NameProperty }
    $condition = New-Object System.Windows.Automation.PropertyCondition($property, $Value)
    $handle = $Root.Current.NativeWindowHandle
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            # Refresh top-level providers while WPF materializes its child tree.
            if ($handle -ne 0) { $Root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$handle) }
            $result = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            if ($result) { return $result }
        } catch [System.Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Owned element missing: $Value"
}
function Find-ScrolledElement($Root, [string]$Value, [string]$ScrollId) {
    $handle = $Root.Current.NativeWindowHandle
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Value)
    $viewport = Find-Element $Root $ScrollId -ById
    $scroll = $viewport.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($scroll.Current.VerticallyScrollable) { $scroll.SetScrollPercent(-1, 0); Start-Sleep -Milliseconds 100 }
    for ($page = 0; $page -lt 100; $page++) {
        try {
            if ($handle -ne 0) { $Root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$handle) }
            $element = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            $viewport = Find-Element $Root $ScrollId -ById
            if ($element -and !$element.Current.IsOffscreen) {
                # WPF can report IsOffscreen=false for children clipped by a ScrollViewer.
                $visible = [System.Windows.Rect]::Intersect($element.Current.BoundingRectangle, $viewport.Current.BoundingRectangle)
                if (!$visible.IsEmpty -and $visible.Width -gt 0 -and $visible.Height -gt 0) { return $element }
            }
            $scroll = $viewport.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
            if (!$scroll.Current.VerticallyScrollable -or $scroll.Current.VerticalScrollPercent -ge 99.99) { return $null }
            $scroll.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount, [System.Windows.Automation.ScrollAmount]::LargeIncrement)
        } catch [System.Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 100
    }
    throw "Scrolling did not finish within its page limit: $Value"
}
