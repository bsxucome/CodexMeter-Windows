function Owned-Window([int]$OwnerId, [string]$Name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $OwnerId)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)))
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
