Import-Module Pester
Update-FormatData .\types.format.ps1xml
Import-Module "./bin/Debug/net9.0/TypeProvider.dll"
New-PSDrive -Name types -PSProvider TypeProvider -Root "" -pow ([System.AppDomain]::CurrentDomain)
New-PSDrive -Name SystemIO -PSProvider TypeProvider -Root "System.IO" -pow ([System.AppDomain]::CurrentDomain)

cd types:
#dir at:
# dir at: -Name | % {if ($_.IndexOf('.') -eq -1) {$_} else {$_.SubString(0,$_.IndexOF('.'))}} | sort -Unique