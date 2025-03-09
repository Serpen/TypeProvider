# have to prepend because there is already a Type Format Definition
Import-Module Microsoft.PowerShell.Utility # -Scope Local 
Microsoft.PowerShell.Utility\Update-FormatData -PrependPath .\PSTypeProvider.format.ps1xml