using System.Security.Principal;

namespace THJPatcher.Utilities
{
    internal static class SystemInfoUtils
    {
        internal static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
} 