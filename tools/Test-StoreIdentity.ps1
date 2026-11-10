# Check the Partner Center identity without installing or registering a package.
param([Parameter(Mandatory)][xml]$Manifest)
$ErrorActionPreference = 'Stop'
if ([string]$Manifest.Package.Identity.Name -cne 'sodarx.BiliPlay' -or
    [string]$Manifest.Package.Identity.Publisher -cne 'CN=43D94D0E-79B6-4582-B52D-D4AE86E09E83' -or
    [string]$Manifest.Package.Properties.PublisherDisplayName -cne 'soda_rx') {
    throw '包名、发布者或发布者显示名称与 Partner Center 不一致。'
}
if (-not ('BiliPlay.Packaging.PackageIdentity' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace BiliPlay.Packaging {
    public static class PackageIdentity {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PackageId {
            public uint Reserved;
            public uint Architecture;
            public ulong Version;
            [MarshalAs(UnmanagedType.LPWStr)] public string Name;
            [MarshalAs(UnmanagedType.LPWStr)] public string Publisher;
            [MarshalAs(UnmanagedType.LPWStr)] public string ResourceId;
            public IntPtr PublisherId;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int PackageFamilyNameFromId(ref PackageId id, ref uint length, StringBuilder name);
        public static string GetFamilyName(string name, string publisher) {
            var id = new PackageId { Architecture = 9, Name = name, Publisher = publisher, ResourceId = "" };
            uint length = 0;
            int result = PackageFamilyNameFromId(ref id, ref length, null);
            if (result != 122) throw new Win32Exception(result);
            var family = new StringBuilder((int)length);
            result = PackageFamilyNameFromId(ref id, ref length, family);
            if (result != 0) throw new Win32Exception(result);
            return family.ToString();
        }
    }
}
'@
}
$familyName = [BiliPlay.Packaging.PackageIdentity]::GetFamilyName(
    [string]$Manifest.Package.Identity.Name, [string]$Manifest.Package.Identity.Publisher)
if ($familyName -cne 'sodarx.BiliPlay_9fmmj7hf39ng8') { throw 'Windows 计算出的包系列名称与商店不一致。' }
Write-Output ('PASS Store identity and package family: ' + $familyName)
