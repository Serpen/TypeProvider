# Bugs

## Nested Types
### Access Them
[System.TimeZoneInfo+AdjustmentRule].ToString()

### List them
Dir System\A* -> "System\AdjustmentRule"

## Format Def for Types Only
when only Types are shown, the format Def isn't used

## AppDomain not showing all dlls
Microsoft.VisualBasic can be autocompleted with [Microsoft.Vis]
but the dll is first loaded when going beyong [Microsoft.VisualBasic.]
so we didnt see it in the provider

