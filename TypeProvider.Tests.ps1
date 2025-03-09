Import-Module Pester

Describe "Powershell Provider Tests" {
    BeforeAll {
        add-Type -TypeDefinition @"
namespace Alibaba1234 {
    namespace Alibaba5678 {
        public class Alibaba9012 {}
        class Alibaba3456 {}
    }
}
"@
        Import-Module $PSScriptRoot\PSTypeProvider.psd1
        New-PSDrive -Name types -PSProvider TypeProvider -Root ""
        New-PSDrive -Name SystemIO -PSProvider TypeProvider -Root "System.IO"

    }

    AfterAll {
        Remove-Module PSTypeProvider
    }

    It "System Namespace should exist" {
        Test-Path types:\System -PathType Container | Should -BeTrue
    }
    It "System Namespace should have items" {
        Get-Item types:\System | Should -BeOfType 'Serpen.PS.NamespaceType'
    }
    It "Root should exist" {
        Test-Path types:\ -PathType Container | Should -BeTrue
        Test-Path types: -PathType Container | Should -BeTrue
    }

    It "String Class should exist" {
        Test-Path types:\System\String -PathType Leaf | Should -BeTrue
    }

    It "SystemIO Root Provider" {
        dir SystemIO: | Measure-Object | select -ExpandProperty Count | Should -BeGreaterThan 10
    }

    It "Alibaba class found recursive" {
        dir types:\Alibaba1234\ -Recurse | ? Name -eq Alibaba9012 | Should -Not -BeNullOrEmpty
    }
    It "Alibaba class not found" {
        dir types:\Alibaba1234\ | ? Name -eq Alibaba9012 | Should -BeNullOrEmpty
    }
    It "Alibaba private class not found" {
        Test-Path types:\Alibaba1234\Alibaba5678\Alibaba3456 | Should -BeFalse
    }

    It "Should return String Names only" {
        dir types:\System\IO\ -Name | Should -BeOfType 'string'
    }
    It "Should return objects" {
        dir types:\System\IO\ | Should -not -BeOfType 'string'
    }

    It "Root should contain namespaces" {
        dir types:\ | ? PSIsContainer | Measure-Object | Select-Object -ExpandProperty Count | Should -BeGreaterThan 1
    }
    It "System should contain namespaces" {
        dir types:\System | ? PSIsContainer | Measure-Object | Select-Object -ExpandProperty Count | Should -BeGreaterThan 1
    }

    It "ForeignAssembly Type" {
        gi types:\System\Net\HttpStatusCode | should -not -BeNullOrEmpty
    }
    It "non existing Type" {
        {gi types:\System\Net\HttpStatusCodeAlibaba -ErrorAction Stop} | should -throw
    }
}