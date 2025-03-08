# Bugs
## AppDomain
AppDomain has to be submitted to provider else is uses only own codebase

## Namespace without Types gets no Assembly
in genNS before type adding loop through .-Parts

## Nested Types
[System.TimeZoneInfo+AdjustmentRule].ToString()

## Format Def for Types Only
when only Types are shown, the format Def isn't used