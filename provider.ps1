Import-Module Pester
Update-FormatData .\types.format.ps1xml
Import-Module "./bin/Debug/net9.0/TypeProvider.dll"
New-PSDrive -Name at -PSProvider TypeProvider -Root . -pow ([System.AppDomain]::CurrentDomain)
#dir at:
# dir at: -Name | % {if ($_.IndexOf('.') -eq -1) {$_} else {$_.SubString(0,$_.IndexOF('.'))}} | sort -Unique