# PSTypeProvider
A Powershell PSProvider which allows browsing through the loaded .net Namespaces and Assemblies

# Sample
Import-Module PSTypeProvider
New-PSDrive -Name Types -Provider PSTypeProvider -Root ""

dir Types:\System