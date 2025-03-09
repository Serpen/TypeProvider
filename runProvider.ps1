$PSModuleAutoLoadingPreference = 'None'
Import-Module Microsoft.PowerShell.Management
Remove-Module PSReadLine

Import-Module .\PSTypeProvider.psd1
# Update-TypeData -PrependPath $PSScriptRoot\PSTypeProvider.types.ps1xml
# Update-FormatData $PSScriptRoot\PSTypeProvider.format.ps1xml
$VerbosePreference = 'Continue'
#$DebugPreference = 'Continue'
# Import-Module "$PSScriptRoot/bin/Debug/net9.0/TypeProvider.dll"
New-PSDrive -Name types -PSProvider TypeProvider -Root "" # -pow ([System.AppDomain]::CurrentDomain)
New-PSDrive -Name SystemIO -PSProvider TypeProvider -Root "System.IO" # -pow ([System.AppDomain]::CurrentDomain)

cd types:
#dir at:
# dir at: -Name | % {if ($_.IndexOf('.') -eq -1) {$_} else {$_.SubString(0,$_.IndexOF('.'))}} | sort -Unique